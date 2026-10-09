#!/usr/bin/env python3
"""Read-only Compose contract checks; never starts a container or reads user AppData."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class ComposeContract(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="bakabase-compose-contract-")
        self.root = Path(self.temporary.name)
        self.env_file = self.root / ".env"
        self.env_file.write_text("BAKABASE_IMAGE=bakabase:contract\nBAKABASE_PORT=34567\n"
                                 f'BAKABASE_DATA_DIR="{self.root}/app data"\n')
        self.env = {key: value for key, value in os.environ.items()
                    if not key.startswith(("BAKABASE_", "COMPOSE_"))}
        self.env.update(BAKABASE_ENV_FILE=str(self.env_file), BAKABASE_COMPOSE_OVERRIDE="",
                        BAKABASE_VERSION="2.4.0-beta.511")

    def tearDown(self):
        self.temporary.cleanup()

    def config(self, mode):
        result = subprocess.run([str(ROOT / "docker/compose.sh"), mode, "config", "--format", "json"],
                                env=self.env, text=True, capture_output=True, check=True)
        return json.loads(result.stdout)

    def test_source_and_image_share_deployment_identity(self):
        image, source = self.config("image"), self.config("source")
        self.assertEqual(image["name"], "bakabase")
        self.assertEqual(source["name"], image["name"])
        self.assertEqual(list(image["services"]), ["server"])
        image_service, source_service = image["services"]["server"], source["services"]["server"]
        self.assertNotIn("build", image_service)
        self.assertEqual(source_service.pop("build")["context"], str(ROOT))
        source_service.pop("pull_policy")
        self.assertEqual(source_service, image_service)
        self.assertNotIn("platform", image_service)
        self.assertEqual([v["target"] for v in image_service["volumes"]], ["/data"])
        self.assertEqual(image_service["volumes"][0]["source"], str(self.root / "app data"))
        self.assertFalse(image_service["volumes"][0]["bind"]["create_host_path"])

    def test_unedited_local_example_adds_no_machine_specific_configuration(self):
        baseline = self.config("image")
        self.env["BAKABASE_COMPOSE_OVERRIDE"] = str(ROOT / "docker/compose.local.example.yaml")
        self.assertEqual(self.config("image"), baseline)
        source = self.config("source")["services"]["server"]
        self.assertEqual(source["volumes"], baseline["services"]["server"]["volumes"])
        self.assertNotIn("platform", source)

    def test_explicit_local_mounts_apply_unchanged_to_both_modes(self):
        entries = [
            {"type": "bind", "source": str(self.root / "chosen data"), "target": "/data",
             "bind": {"create_host_path": False}},
            {"type": "bind", "source": str(self.root / "chosen downloads"), "target": "/downloads",
             "bind": {"create_host_path": False}},
            {"type": "bind", "source": str(self.root / "chosen media"), "target": "/media",
             "read_only": True, "bind": {"create_host_path": False}},
            {"type": "bind", "source": str(self.root / "chosen import"), "target": "/import",
             "read_only": True, "bind": {"create_host_path": False}}]
        override = self.root / "explicit.json"
        override.write_text(json.dumps({"services": {"server": {"volumes": entries}}}))
        self.env["BAKABASE_COMPOSE_OVERRIDE"] = str(override)
        image, source = self.config("image"), self.config("source")
        service = image["services"]["server"]
        self.assertEqual(service["volumes"], source["services"]["server"]["volumes"])
        mounts = {entry["target"]: entry for entry in service["volumes"]}
        self.assertEqual(set(mounts), {entry["target"] for entry in entries})
        for expected in entries:
            mount = mounts[expected["target"]]
            self.assertEqual(mount["source"], expected["source"])
            self.assertFalse(mount["bind"]["create_host_path"])
            self.assertEqual(mount.get("read_only", False), expected.get("read_only", False))

    def test_missing_or_empty_data_directory_fails_with_actionable_message(self):
        for value in ("", "BAKABASE_DATA_DIR=\n"):
            self.env_file.write_text("BAKABASE_IMAGE=bakabase:contract\n" + value)
            for mode in ("image", "source"):
                with self.subTest(value=value, mode=mode):
                    result = subprocess.run([str(ROOT / "docker/compose.sh"), mode, "config"],
                                            env=self.env, text=True, capture_output=True)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("Set BAKABASE_DATA_DIR to an existing absolute host data directory", result.stderr)

    def test_environment_example_requires_a_user_selected_data_directory(self):
        self.env_file.write_text((ROOT / "docker/.env.example").read_text())
        result = subprocess.run([str(ROOT / "docker/compose.sh"), "image", "config"],
                                env=self.env, text=True, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Set BAKABASE_DATA_DIR", result.stderr)

    def test_explicit_missing_config_fails_before_start(self):
        for key in ("BAKABASE_ENV_FILE", "BAKABASE_COMPOSE_OVERRIDE"):
            with self.subTest(key=key):
                env = {**self.env, key: str(self.root / "missing")}
                result = subprocess.run([str(ROOT / "docker/compose.sh"), "image", "config"], env=env,
                                        text=True, capture_output=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Missing deployment", result.stderr)


if __name__ == "__main__":
    unittest.main()
