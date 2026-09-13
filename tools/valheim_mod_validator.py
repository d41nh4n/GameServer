#!/usr/bin/env python3
"""Validate a Valheim mod archive before staging; never extracts files."""
from __future__ import annotations

import argparse
import hashlib
import json
import posixpath
import re
import stat
import zipfile
from pathlib import Path

MAX_ARCHIVE_BYTES = 256 * 1024 * 1024
MAX_FILES = 4096
MAX_UNCOMPRESSED_BYTES = 512 * 1024 * 1024
ALLOWED_PREFIXES = ("BepInEx/plugins/", "BepInEx/config/", "BepInEx/patchers/", "BepInEx/monomod/")
REQUIRED_MANIFEST_KEYS = ("packageId", "version", "source", "target", "enabled", "dependencies", "files", "sha256", "testedGameBuild", "labTested")
ALLOWED_TARGETS = {"server-only", "client-only", "client-server"}


class ValidationError(ValueError):
    pass


def safe_member_name(name: str) -> str:
    if not name or "\\" in name or "\x00" in name:
        raise ValidationError(f"invalid archive path: {name!r}")
    if name.startswith("/") or (len(name) >= 2 and name[1] == ":"):
        raise ValidationError(f"absolute archive path: {name!r}")
    normalized = posixpath.normpath(name)
    if normalized == ".." or normalized.startswith("../"):
        raise ValidationError(f"path traversal: {name!r}")
    return normalized


def route_allowed(name: str) -> bool:
    return name == "manifest.json" or any((name + "/").startswith(prefix) for prefix in ALLOWED_PREFIXES)


def validate_manifest(raw: bytes) -> None:
    try:
        manifest = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ValidationError("manifest.json is not valid JSON") from exc
    if not isinstance(manifest, dict) or set(manifest) != set(REQUIRED_MANIFEST_KEYS):
        raise ValidationError("manifest.json fields do not match the typed schema")
    if not isinstance(manifest["packageId"], str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{1,127}", manifest["packageId"]):
        raise ValidationError("invalid packageId")
    if not isinstance(manifest["version"], str) or not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}", manifest["version"]):
        raise ValidationError("invalid version")
    if manifest.get("source") not in {"thunderstore", "local"} or manifest.get("target") not in ALLOWED_TARGETS:
        raise ValidationError("source or target is not allowed")
    if not isinstance(manifest["enabled"], bool) or not isinstance(manifest["labTested"], bool):
        raise ValidationError("enabled/labTested must be boolean")
    if not isinstance(manifest.get("dependencies"), list) or len(manifest["dependencies"]) > 128 or len(set(manifest["dependencies"])) != len(manifest["dependencies"]) or not all(isinstance(x, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{1,127}@[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}", x) for x in manifest["dependencies"]):
        raise ValidationError("manifest dependencies must be a bounded string array")
    if not isinstance(manifest.get("files"), list) or len(manifest["files"]) > MAX_FILES or len(set(manifest["files"])) != len(manifest["files"]) or not all(isinstance(x, str) and 0 < len(x) <= 512 and any(x.startswith(route) for route in ALLOWED_PREFIXES) and safe_member_name(x) == x for x in manifest["files"]):
        raise ValidationError("manifest files must be a bounded string array")
    if not isinstance(manifest.get("testedGameBuild"), str) or not re.fullmatch(r"[0-9]{1,20}", manifest["testedGameBuild"]):
        raise ValidationError("invalid testedGameBuild")
    digest = manifest.get("sha256")
    if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdefABCDEF" for c in digest):
        raise ValidationError("manifest sha256 must be 64 hexadecimal characters")


def validate_archive(path: Path, max_archive_bytes: int = MAX_ARCHIVE_BYTES, max_files: int = MAX_FILES) -> None:
    if path.is_symlink() or not path.is_file():
        raise ValidationError("archive must be a regular file, not a symlink")
    if path.stat().st_size > max_archive_bytes:
        raise ValidationError("archive exceeds size limit")
    with zipfile.ZipFile(path) as archive:
        members = archive.infolist()
        if len(members) > max_files:
            raise ValidationError("archive exceeds file-count limit")
        total = 0
        manifest = None
        manifest_count = 0
        seen = set()
        archive_files = set()
        for info in members:
            name = safe_member_name(info.filename)
            if name != info.filename.rstrip("/"):
                raise ValidationError(f"non-canonical archive path: {name}")
            if name in seen:
                raise ValidationError(f"duplicate archive path: {name}")
            seen.add(name)
            mode = (info.external_attr >> 16) & 0xFFFF
            if stat.S_ISLNK(mode):
                raise ValidationError(f"symlink member is not allowed: {name}")
            if not route_allowed(name):
                raise ValidationError(f"archive route is not allowed: {name}")
            total += info.file_size
            if total > MAX_UNCOMPRESSED_BYTES:
                raise ValidationError("archive uncompressed size exceeds limit")
            if name == "manifest.json":
                if info.is_dir():
                    raise ValidationError("manifest.json must be a file")
                manifest_count += 1
                manifest = archive.read(info)
            elif not info.is_dir():
                archive_files.add(name)
        if manifest_count != 1 or manifest is None:
            raise ValidationError("archive must contain exactly one root manifest.json")
        validate_manifest(manifest)
        manifest_data = json.loads(manifest)
        digest = hashlib.sha256()
        for name in sorted(archive_files):
            digest.update(name.encode("utf-8") + b"\0")
            digest.update(archive.read(name))
        if digest.hexdigest().lower() != manifest_data["sha256"].lower():
            raise ValidationError("archive sha256 does not match manifest")
        if set(manifest_data["files"]) != archive_files:
            raise ValidationError("manifest files do not match archive")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    try:
        validate_archive(args.archive)
    except (OSError, zipfile.BadZipFile, ValidationError) as exc:
        print(f"INVALID: {exc}")
        return 1
    print("VALID")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
