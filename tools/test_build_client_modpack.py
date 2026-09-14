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

    def test_normalizes_client_only_mod_files_under_package_directory(self):
        archive_path = self.root / "client-only.zip"
        make_zip(archive_path, [
            ("manifest.json", json.dumps({"name": "ClientQoL", "version_number": "1.0.0", "dependencies": []})),
            ("ClientQoL.dll", b"plugin"),
        ])

        out = self.root / "out-client-only"
        build_modpack(out, "client-only-1", [
            ClientPackage("Author-ClientQoL", "ClientQoL", "1.0.0", "client-only", archive_path, sha(archive_path), []),
        ])

        with zipfile.ZipFile(out / "packages" / "Author-ClientQoL" / "1.0.0.zip") as archive:
            self.assertIn("BepInEx/plugins/Author-ClientQoL/ClientQoL.dll", archive.namelist())

    def test_keeps_package_config_assets_beside_the_plugin(self):
        archive_path = self.root / "assets.zip"
        make_zip(archive_path, [
            ("manifest.json", json.dumps({"name": "Seasonal", "version_number": "1.0.0", "dependencies": []})),
            ("Seasonal.dll", b"plugin"),
            ("config/Seasonal/texture.png", b"asset"),
        ])

        out = self.root / "out-assets"
        build_modpack(out, "assets-1", [
            ClientPackage("Author-Seasonal", "Seasonal", "1.0.0", "client-server", archive_path, sha(archive_path), []),
        ])

        with zipfile.ZipFile(out / "packages" / "Author-Seasonal" / "1.0.0.zip") as archive:
            self.assertIn("BepInEx/plugins/Author-Seasonal/config/Seasonal/texture.png", archive.namelist())

    def test_normalizes_legacy_backslash_separators(self):
        archive_path = self.root / "backslash-only.zip"
        make_zip(archive_path, [
            ("manifest.json", json.dumps({"name": "Legacy", "version_number": "1.0.0", "dependencies": []})),
            ("plugins\\Legacy.dll", b"plugin"),
        ])

        out = self.root / "out-backslash-only"
        build_modpack(out, "backslash-only-1", [
            ClientPackage("Author-Legacy", "Legacy", "1.0.0", "client-only", archive_path, sha(archive_path), []),
        ])

        with zipfile.ZipFile(out / "packages" / "Author-Legacy" / "1.0.0.zip") as archive:
            self.assertIn("BepInEx/plugins/Legacy.dll", archive.namelist())

    def test_rejects_paths_that_collide_after_separator_normalization(self):
        archive_path = self.root / "backslash.zip"
        make_zip(archive_path, [
            ("manifest.json", json.dumps({"name": "Legacy", "version_number": "1.0.0", "dependencies": []})),
            ("plugins\\Legacy.dll", b"one"),
            ("plugins/Legacy.dll", b"two"),
        ])

        with self.assertRaisesRegex(BuildError, "duplicate normalized client path"):
            build_modpack(self.root / "out-backslash", "backslash-1", [
                ClientPackage("Author-Legacy", "Legacy", "1.0.0", "client-only", archive_path, sha(archive_path), []),
            ])


if __name__ == "__main__":
    unittest.main()
