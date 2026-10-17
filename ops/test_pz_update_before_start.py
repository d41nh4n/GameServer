import os
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("pz-update-before-start")


class PzUpdateBeforeStartTests(unittest.TestCase):
    def run_update(self, steam_output: str, create_manifest: bool = True, steam_exit: int = 0):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            install = root / "server"
            fake_steamcmd = root / "steamcmd"
            manifest = install / "steamapps" / "appmanifest_380870.acf"
            if create_manifest:
                manifest.parent.mkdir(parents=True)
                manifest.write_text('"AppState"\n{\n  "appid" "380870"\n  "buildid" "987654"\n}\n')
            fake_steamcmd.write_text(
                "#!/usr/bin/env bash\n"
                f"printf '%s\\n' {steam_output!r}\n"
                "exit \"${FAKE_STEAM_EXIT:-0}\"\n"
            )
            fake_steamcmd.chmod(0o755)
            env = os.environ | {
                "PZ_SERVER_INSTALL_PATH": str(install),
                "PZ_STEAMCMD": str(fake_steamcmd),
                "PZ_UPDATE_LOCK_FILE": str(root / "lock"),
                "FAKE_STEAM_EXIT": str(steam_exit),
            }
            return subprocess.run(["bash", str(SCRIPT)], capture_output=True, text=True, env=env)

    def test_successful_update_requires_and_reports_manifest_build(self):
        result = self.run_update("Success! App '380870' fully installed.")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("installed server build: 987654", result.stdout)

    def test_already_up_to_date_is_a_successful_update(self):
        result = self.run_update("Success! App '380870' already up to date.")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("installed server build: 987654", result.stdout)

    def test_success_marker_and_manifest_allow_steamcmd_first_run_exit(self):
        result = self.run_update("Success! App '380870' fully installed.", steam_exit=7)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("installed server build: 987654", result.stdout)

    def test_failed_update_blocks_start_even_if_old_manifest_exists(self):
        result = self.run_update("ERROR! Update failed.")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("refusing to start", result.stderr)

    def test_missing_manifest_blocks_start(self):
        result = self.run_update("Success! App '380870' fully installed.", create_manifest=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("refusing to start", result.stderr)


if __name__ == "__main__":
    unittest.main()
