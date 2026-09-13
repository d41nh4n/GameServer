#!/usr/bin/env python3
import json
import stat
import tempfile
import unittest
import zipfile
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from valheim_mod_validator import ValidationError, validate_archive

PAYLOAD_HASH = "09cc31f885763cb92deb4899683188e298c42ddd117d1dd70d61c9894a249da0"
MANIFEST = {"packageId":"Example-Placeholder-Mod","version":"0.0.0-example","source":"local","target":"server-only","enabled":False,"dependencies":[],"files":["BepInEx/plugins/example.dll"],"sha256":PAYLOAD_HASH,"testedGameBuild":"25253791","labTested":False}

class ValidatorTests(unittest.TestCase):
    def make_zip(self, entries):
        h = tempfile.NamedTemporaryFile(suffix=".zip", delete=False); h.close(); p = Path(h.name)
        with zipfile.ZipFile(p, "w") as z:
            for name, data in entries: z.writestr(name, data)
        return p
    def assert_invalid(self, entries):
        p = self.make_zip(entries)
        try:
            with self.assertRaises(ValidationError): validate_archive(p)
        finally: p.unlink()
    def test_safe_archive_passes(self):
        p = self.make_zip([("manifest.json", json.dumps(MANIFEST)), ("BepInEx/plugins/example.dll", b"fake")])
        try: validate_archive(p)
        finally: p.unlink()
    def test_allowed_directory_entries_pass(self):
        p = self.make_zip([("manifest.json", json.dumps(MANIFEST)), ("BepInEx/plugins/", b""), ("BepInEx/plugins/example.dll", b"fake")])
        try: validate_archive(p)
        finally: p.unlink()
    def test_zip_slip_is_rejected(self): self.assert_invalid([("manifest.json", json.dumps(MANIFEST)), ("../../etc/passwd", b"x")])
    def test_absolute_path_is_rejected(self): self.assert_invalid([("manifest.json", json.dumps(MANIFEST)), ("/tmp/escape", b"x")])
    def test_noncanonical_path_is_rejected(self): self.assert_invalid([("manifest.json", json.dumps(MANIFEST)), ("BepInEx/plugins/../config/mod.cfg", b"x")])
    def test_manifest_file_list_mismatch_is_rejected(self): self.assert_invalid([("manifest.json", json.dumps(MANIFEST)), ("BepInEx/plugins/extra.dll", b"x")])
    def test_wrong_payload_hash_is_rejected(self):
        bad = dict(MANIFEST); bad["sha256"] = "f" * 64; self.assert_invalid([("manifest.json", json.dumps(bad)), ("BepInEx/plugins/example.dll", b"fake")])
    def test_duplicate_manifest_file_declaration_is_rejected(self):
        bad = dict(MANIFEST); bad["files"] = MANIFEST["files"] * 2; self.assert_invalid([("manifest.json", json.dumps(bad)), ("BepInEx/plugins/example.dll", b"fake")])
    def test_duplicate_member_is_rejected(self):
        h = tempfile.NamedTemporaryFile(suffix=".zip", delete=False); h.close(); p = Path(h.name)
        with zipfile.ZipFile(p, "w") as z: z.writestr("manifest.json", json.dumps(MANIFEST)); z.writestr("manifest.json", json.dumps(MANIFEST)); z.writestr("BepInEx/plugins/example.dll", b"fake")
        try:
            with self.assertRaises(ValidationError): validate_archive(p)
        finally: p.unlink()
    def test_manifest_directory_is_rejected(self): self.assert_invalid([("manifest.json/", b""), ("BepInEx/plugins/example.dll", b"fake")])
    def test_symlink_is_rejected(self):
        h = tempfile.NamedTemporaryFile(suffix=".zip", delete=False); h.close(); p = Path(h.name); info = zipfile.ZipInfo("BepInEx/plugins/link.dll"); info.external_attr = (stat.S_IFLNK | 0o777) << 16
        with zipfile.ZipFile(p, "w") as z: z.writestr("manifest.json", json.dumps(MANIFEST)); z.writestr(info, b"../../outside")
        try:
            with self.assertRaises(ValidationError): validate_archive(p)
        finally: p.unlink()

if __name__ == "__main__": unittest.main()
