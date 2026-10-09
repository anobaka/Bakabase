"""Deployment safety checks using temporary releases; never opens real AppData."""

import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "server.py"
PROBE = (
    "import json, os, sys; "
    "print(json.dumps({'cwd': os.getcwd(), 'data': os.environ.get('BAKABASE_DATA_DIR'), "
    "'bind': os.environ['BAKABASE_BIND_ADDRESS'], 'ports': os.environ['API_LISTENING_PORTS'], "
    "'args': sys.argv[1:]}))"
)


class ServerDeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="bakabase-server-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.install = self.root / "program with spaces"
        self.data = self.root / "data with spaces"
        self.data.mkdir()
        (self.data / "sentinel").write_text("existing user data")
        self.create_release("old")
        self.cli("activate", "old")

    def create_release(self, name):
        release = self.install / "releases" / name
        (release / "web").mkdir(parents=True)
        (release / "web/index.html").write_text("frontend")
        executable = release / "bakabase-server"
        executable.symlink_to(sys.executable)
        os_name = "osx" if sys.platform == "darwin" else "linux"
        arch = "arm64" if platform.machine() in ("arm64", "aarch64") else "x64"
        (release / "release.json").write_text(json.dumps({"runtime": f"{os_name}-{arch}"}))
        return release

    def cli(self, *args, expected=0, env=None):
        result = subprocess.run([sys.executable, SCRIPT, "--install-dir", self.install, *args],
                                text=True, capture_output=True, env=env)
        self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
        return result

    def test_select_and_run_keep_external_data_and_both_releases(self):
        new = self.create_release("new")
        self.cli("activate", "new")
        output = self.cli("run", "--data-dir", str(self.data), "--ports", "34567,34568",
                          "--bind", "127.0.0.1", "--", "-c", PROBE, "--example", "space in argument")
        launch = json.loads(output.stdout.splitlines()[-1])
        self.assertEqual(str(new.resolve()), launch["cwd"])
        self.assertEqual(str(self.data.resolve()), launch["data"])
        self.assertEqual("127.0.0.1", launch["bind"])
        self.assertEqual("34567,34568", launch["ports"])
        self.assertEqual(["--example", "space in argument"], launch["args"])
        self.assertEqual("existing user data", (self.data / "sentinel").read_text())
        self.assertTrue((self.install / "releases/old/bakabase-server").is_file())

    def test_refuses_appdata_inside_programs_even_through_symlink(self):
        alias = self.root / "alias"
        alias.symlink_to(self.install / "releases/old", target_is_directory=True)
        output = self.cli("run", "--data-dir", str(alias / "AppData"), expected=1)
        self.assertIn("separate, non-nested", output.stderr)
        self.assertFalse((alias / "AppData").exists())

    def test_default_run_leaves_first_run_path_selection_available(self):
        env = dict(os.environ)
        env.pop("BAKABASE_DATA_DIR", None)
        output = self.cli("run", "--", "-c", PROBE, env=env)
        self.assertIsNone(json.loads(output.stdout.splitlines()[-1])["data"])
        self.assertIn("choose during first-run setup", output.stdout)

    def test_existing_environment_data_override_remains_fixed(self):
        output = self.cli("run", "--", "-c", PROBE, env=dict(os.environ, BAKABASE_DATA_DIR=str(self.data)))
        self.assertEqual(str(self.data.resolve()), json.loads(output.stdout.splitlines()[-1])["data"])

    def test_failed_publish_leaves_selected_release_and_data_untouched(self):
        binaries = self.root / "bin"
        binaries.mkdir()
        dotnet = binaries / "dotnet"
        dotnet.write_text("#!/bin/sh\nexit 73\n")
        dotnet.chmod(0o755)
        env = dict(os.environ, PATH=f"{binaries}{os.pathsep}{os.environ['PATH']}")
        self.cli("build", "--web-dir", str(self.install / "releases/old/web"), env=env, expected=1)
        self.assertEqual("old", (self.install / "current").resolve().name)
        self.assertEqual(["old"], sorted(p.name for p in (self.install / "releases").iterdir()))
        self.assertEqual("existing user data", (self.data / "sentinel").read_text())

    def test_refuses_invalid_release_and_keeps_current(self):
        self.cli("activate", "../outside", expected=1)
        self.cli("activate", "missing", expected=1)
        self.assertEqual("old", (self.install / "current").resolve().name)

    def test_refuses_current_symlink_outside_installation(self):
        outside = self.root / "outside/old"
        outside.mkdir(parents=True)
        (self.install / "current").unlink()
        (self.install / "current").symlink_to(outside, target_is_directory=True)
        output = self.cli("run", "--data-dir", str(self.data), expected=1)
        self.assertIn("current must point to a release", output.stderr)


if __name__ == "__main__":
    unittest.main()
