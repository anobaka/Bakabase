#!/usr/bin/env python3
"""Mount display contract: real Compose parsing, with all container-creating commands intercepted."""
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
HELPER = runpy.run_path(str(ROOT / "docker/compose-metadata.py"))
VARIABLE = HELPER["VARIABLE"]


class ComposeMetadata(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="bakabase-mount-contract-")
        self.root = Path(self.temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.real_docker = shutil.which("docker") or "/usr/local/bin/docker"
        self.env = {key: value for key, value in os.environ.items()
                    if not key.startswith(("BAKABASE_", "COMPOSE_"))}
        self.env_file = self.root / ".env"
        self.env_file.write_text('BAKABASE_IMAGE=bakabase:contract\nBAKABASE_DATA_DIR="/host/initial"\n')
        self.env.update(BAKABASE_ENV_FILE=str(self.env_file), BAKABASE_COMPOSE_OVERRIDE="",
                        BAKABASE_VERSION="2.4.0-beta.511", PATH=str(self.bin) + os.pathsep + self.env.get("PATH", ""))
        # Delegate only config to Docker. `up/create/run` print the environment and final
        # resolved service mounts instead of performing any daemon operation.
        fake = self.bin / "docker"
        fake.write_text(f'''#!{sys.executable}
import json, os, subprocess, sys
args = sys.argv[1:]
commands = [i for i, arg in enumerate(args) if arg in ("up", "create", "run")]
if not commands:
    os.execv({self.real_docker!r}, [{self.real_docker!r}, *args])
index = commands[0]
cfg = json.loads(subprocess.check_output([{self.real_docker!r}, *args[:index], "config", "--format", "json"], text=True))
service = cfg["services"]["server"]
print(json.dumps({{"args": args, "metadata": os.environ.get({VARIABLE!r}), "serviceMetadata": service["environment"].get({VARIABLE!r}), "mounts": service.get("volumes", []), "name": cfg["name"]}}))
''')
        fake.chmod(0o755)

    def tearDown(self):
        self.temporary.cleanup()

    def start(self, mode="image", *args):
        result = subprocess.run([str(ROOT / "docker/compose.sh"), mode, *(args or ("up", "-d"))],
                                env=self.env, text=True, capture_output=True, check=True)
        return json.loads(result.stdout)

    def test_source_and_image_derive_same_mounts_without_second_interpolation(self):
        for name in ("literal $UNEXPANDED data", "${UNEXPANDED} and $$ data"):
            with self.subTest(name=name):
                self.env["BAKABASE_DATA_DIR"] = str(self.root / name)
                image, source = self.start(), self.start("source")
                self.assertEqual(image["metadata"], source["metadata"])
                # config output escapes dollars for reloading as Compose input; inspect
                # its plain value after undoing this one serialization, like mount_metadata.
                self.assertEqual(image["metadata"], image["serviceMetadata"].replace("$$", "$"))
                manifest = json.loads(image["metadata"])
                self.assertEqual(manifest["mounts"], [{"type": "bind", "target": "/data", "readOnly": False,
                                                      "source": self.env["BAKABASE_DATA_DIR"]}])

    def test_cli_env_and_mount_overrides_feed_both_config_and_start(self):
        env_file = self.root / "other.env"
        env_file.write_text('BAKABASE_DATA_DIR="/host/overridden"\n')
        override = self.root / "override.json"
        override.write_text(json.dumps({"services": {"server": {"read_only": True, "volumes": [
            {"type": "bind", "source": str(self.root / "readonly"), "target": "/data/logs", "read_only": True},
            {"type": "volume", "source": "cache", "target": "/data/cache"}]}}, "volumes": {"cache": {}}}))
        result = self.start("image", "--env-file", str(env_file), "-f", str(override), "-p", "mount-contract", "create")
        manifest = json.loads(result["metadata"])
        self.assertEqual(result["name"], "mount-contract")
        self.assertTrue(manifest["readOnlyRoot"])
        mounts = {mount["target"]: mount for mount in manifest["mounts"]}
        self.assertEqual(mounts["/data"]["source"], "/host/overridden")
        self.assertTrue(mounts["/data/logs"]["readOnly"])
        self.assertEqual(mounts["/data/cache"], {"type": "volume", "target": "/data/cache", "readOnly": False})

    def test_run_mount_overrides_fall_back_instead_of_publishing_stale_paths(self):
        result = self.start("image", "run", "--rm", "--volume", "/other:/data", "server")
        self.assertEqual(result["metadata"], "")
        self.assertEqual(result["serviceMetadata"], "")

    def test_standalone_package_entry_uses_its_own_compose_files(self):
        package = self.root / "standalone package"
        package.mkdir()
        for name in ("compose.yaml", "compose.sh", "image.sh", "compose-metadata.py"):
            shutil.copy2(ROOT / "docker" / name, package / name)
        result = subprocess.run([str(package / "image.sh"), "up", "-d", "--no-build"],
                                env=self.env, text=True, capture_output=True, check=True)
        value = json.loads(result.stdout)
        self.assertIn(str((package / "compose.yaml").resolve()), value["args"])
        self.assertNotIn(str(ROOT / "docker/compose.yaml"), value["args"])
        self.assertEqual(json.loads(value["metadata"])["mounts"][0]["source"], "/host/initial")

    def test_tmpfs_masks_bind_and_no_other_configuration_is_copied(self):
        raw = HELPER["mount_metadata"]({"services": {"server": {"environment": {"SECRET": "not-copied"},
            "volumes": [{"type": "bind", "source": "/host", "target": "/data"}], "tmpfs": ["/data/temp:ro,size=1m"]}}})
        self.assertNotIn("not-copied", raw)
        self.assertEqual(json.loads(raw)["mounts"][1], {"type": "tmpfs", "target": "/data/temp", "readOnly": True})

    def test_inherited_mounts_are_unknown_instead_of_incomplete(self):
        self.assertEqual(HELPER["mount_metadata"]({"services": {"server": {
            "volumes_from": ["other"], "volumes": [{"type": "bind", "source": "/host", "target": "/data"}]}}}), "")

    def test_non_start_commands_do_not_require_python(self):
        # This fake Docker is a shell script, and PATH deliberately contains no Python.
        (self.bin / "docker").write_text('#!/bin/bash\nprintf "docker-called\\n"\n')
        (self.bin / "bash").symlink_to("/bin/bash")
        dirname = shutil.which("dirname") or "/usr/bin/dirname"
        (self.bin / "dirname").symlink_to(dirname)
        env = {**self.env, "PATH": str(self.bin)}
        for command in ("config", "build", "pull", "down", "ps", "logs"):
            result = subprocess.run([str(ROOT / "docker/compose.sh"), "image", command], env=env,
                                    text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, "docker-called\n")
        result = subprocess.run([str(ROOT / "docker/compose.sh"), "image", "up"], env=env, text=True, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Python 3", result.stderr)


if __name__ == "__main__":
    unittest.main()
