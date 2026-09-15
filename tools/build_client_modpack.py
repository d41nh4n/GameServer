#!/usr/bin/env python3
"""Build a hash-pinned Windows Valheim client modpack from pinned archives."""
from __future__ import annotations

import hashlib
import json
import os
import posixpath
import re
import shutil
import stat
import tempfile
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
VER_RE = re.compile(r"^[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}$")
HASH_RE = re.compile(r"^[0-9a-f]{64}$")
MAX_ENTRIES = 8192
MAX_UNCOMPRESSED = 1024 * 1024 * 1024
METADATA = {"manifest.json", "readme.md", "changelog.md", "changelog.txt", "icon.png", "start_game_bepinex.sh", "start_server_bepinex.sh"}
LOADER_ROOT = {".doorstop_version", "doorstop_config.ini", "winhttp.dll"}


class BuildError(ValueError):
    pass


@dataclass(frozen=True)
class ClientPackage:
    package_id: str
    native_name: str
    version: str
    target: str
    archive: Path
    archive_sha256: str
    dependencies: list[str]


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def safe_name(value: str) -> str:
    if not isinstance(value, str) or not value or "\x00" in value:
        raise BuildError("unsafe archive path")
    value = value.replace("\\", "/")
    if value.startswith("/") or re.match(r"^[A-Za-z]:", value):
        raise BuildError("unsafe archive path")
    normalized = posixpath.normpath(value)
    if normalized != value or normalized in {".", ".."} or normalized.startswith("../"):
        raise BuildError("archive path traversal")
    return normalized


def validate_package(package: ClientPackage) -> None:
    if not ID_RE.fullmatch(package.package_id) or not ID_RE.fullmatch(package.native_name):
        raise BuildError("invalid package identity")
    if not VER_RE.fullmatch(package.version) or package.target not in {"client-only", "client-server"}:
        raise BuildError("invalid package version or target")
    if not package.archive.is_file() or package.archive.is_symlink() or not HASH_RE.fullmatch(package.archive_sha256):
        raise BuildError("invalid pinned archive")
    if digest(package.archive) != package.archive_sha256:
        raise BuildError("pinned archive hash mismatch")
    if len(set(package.dependencies)) != len(package.dependencies) or not all(isinstance(x, str) and x for x in package.dependencies):
        raise BuildError("invalid dependencies")


def normalize_path(source: str, package: ClientPackage) -> str | None:
    source = safe_name(source)
    wrapper = package.native_name + "/"
    if source.startswith(wrapper):
        source = source[len(wrapper):]
    if source.lower() in METADATA:
        return None
    if source.startswith("BepInEx/") or source.startswith("doorstop_libs/") or source in LOADER_ROOT:
        return source
    if source.startswith("plugins/"):
        return "BepInEx/" + source
    return f"BepInEx/plugins/{package.package_id}/{source}"


def build_one(package: ClientPackage, destination: Path) -> dict:
    validate_package(package)
    with zipfile.ZipFile(package.archive) as source_zip:
        entries = source_zip.infolist()
        if len(entries) > MAX_ENTRIES:
            raise BuildError("archive has too many entries")
        manifests = [x for x in entries if x.filename == "manifest.json" and not x.is_dir()]
        if len(manifests) != 1:
            raise BuildError("archive needs exactly one root manifest")
        native = json.loads(source_zip.read(manifests[0]))
        if native.get("name") != package.native_name or native.get("version_number") != package.version:
            raise BuildError("native manifest identity mismatch")
        if sorted(native.get("dependencies", [])) != sorted(package.dependencies):
            raise BuildError("native manifest dependency mismatch")

        files: list[dict] = []
        seen: set[str] = set()
        total = 0
        with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as out:
            for entry in entries:
                if entry.is_dir():
                    continue
                source = safe_name(entry.filename)
                total += entry.file_size
                if total > MAX_UNCOMPRESSED:
                    raise BuildError("archive uncompressed size exceeds limit")
                mode = (entry.external_attr >> 16) & 0xFFFF
                if stat.S_ISLNK(mode):
                    raise BuildError("archive symlink is not allowed")
                normalized = normalize_path(source, package)
                if normalized is None:
                    continue
                if normalized in seen:
                    raise BuildError("duplicate normalized client path")
                seen.add(normalized)
                payload = source_zip.read(entry)
                out.writestr(normalized, payload)
                files.append({"path": normalized, "sha256": hashlib.sha256(payload).hexdigest()})
    if not files:
        raise BuildError("package has no client runtime files")
    return {
        "packageId": package.package_id,
        "version": package.version,
        "target": package.target,
        "downloadPath": f"/api/client-updater/packages/{package.package_id}/{package.version}",
        "archiveSha256": digest(destination),
        "files": sorted(files, key=lambda x: x["path"]),
    }


def build_modpack(output_root: Path, revision: str, packages: list[ClientPackage]) -> dict:
    if not ID_RE.fullmatch(revision) or not packages:
        raise BuildError("invalid revision or empty package list")
    if output_root.exists():
        raise BuildError("output root already exists")
    output_root.parent.mkdir(parents=True, exist_ok=True)
    temp = Path(tempfile.mkdtemp(prefix=".client-modpack-", dir=output_root.parent))
    try:
        manifest_packages = []
        for package in packages:
            archive = temp / "packages" / package.package_id / f"{package.version}.zip"
            archive.parent.mkdir(parents=True, exist_ok=True)
            manifest_packages.append(build_one(package, archive))
        if len({x["packageId"] for x in manifest_packages}) != len(manifest_packages):
            raise BuildError("duplicate package id")
        manifest = {
            "schemaVersion": 1,
            "profileId": "valheim-main",
            "revision": revision,
            "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "packages": manifest_packages,
        }
        (temp / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        os.rename(temp, output_root)
        return manifest
    except Exception:
        shutil.rmtree(temp, ignore_errors=True)
        raise


if __name__ == "__main__":
    raise SystemExit("Import build_modpack from an audited promotion script; do not run this module without pinned inputs.")
