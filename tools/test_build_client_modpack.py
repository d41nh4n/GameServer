import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_client_modpack import ClientPackage, build_modpack, BuildError


def make_zip(path, entries):
    with zipfile.ZipFile(path, "w") as archive:
        for name, content in entries:
            archive.writestr(name, content)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class ClientModpackBuilderTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.loader = self.root / "loader.zip"
        self.mod = self.root / "mod.zip"
        make_zip(self.loader, [
            ("manifest.json", json.dumps({"name": "BepInExPack_Valheim", "version_number": "5.4.2350", "dependencies": []})),
            ("BepInExPack_Valheim/BepInEx/core/BepInEx.dll", b"core"),
            ("BepInExPack_Valheim/winhttp.dll", b"windows"),
            ("BepInExPack_Valheim/doorstop_config.ini", b"config"),
            ("BepInExPack_Valheim/changelog.txt", "ignored"),
            ("BepInExPack_Valheim/start_game_bepinex.sh", "ignored"),
        ])
        make_zip(self.mod, [
            ("manifest.json", json.dumps({"name": "PlantEverything", "version_number": "1.21.2", "dependencies": ["denikson-BepInExPack_Valheim-5.4.2350"]})),
            ("Advize_PlantEverything.dll", b"plugin"),
            ("README.md", "ignored"),
        ])

    def tearDown(self):
        self.temp.cleanup()

    def test_builds_normalized_client_packages_and_manifest(self):
        out = self.root / "out"
        manifest = build_modpack(out, "plant-everything-1.21.2", [
            ClientPackage("denikson-BepInExPack_Valheim", "BepInExPack_Valheim", "5.4.2350", "client-only", self.loader, sha(self.loader), []),
            ClientPackage("Advize-PlantEverything", "PlantEverything", "1.21.2", "client-server", self.mod, sha(self.mod), ["denikson-BepInExPack_Valheim-5.4.2350"]),
        ])
        self.assertEqual(manifest["revision"], "plant-everything-1.21.2")
        self.assertEqual(2, len(manifest["packages"]))
        loader = out / "packages" / "denikson-BepInExPack_Valheim" / "5.4.2350.zip"
        plugin = out / "packages" / "Advize-PlantEverything" / "1.21.2.zip"
        with zipfile.ZipFile(loader) as archive:
            self.assertIn("BepInEx/core/BepInEx.dll", archive.namelist())
            self.assertIn("winhttp.dll", archive.namelist())
        with zipfile.ZipFile(plugin) as archive:
            self.assertIn("BepInEx/plugins/Advize-PlantEverything/Advize_PlantEverything.dll", archive.namelist())

    def test_rejects_wrong_pinned_archive_hash(self):
        with self.assertRaises(BuildError):
            build_modpack(self.root / "out", "bad-revision", [
                ClientPackage("Advize-PlantEverything", "PlantEverything", "1.21.2", "client-server", self.mod, "0" * 64, []),
            ])


if __name__ == "__main__":
    unittest.main()
