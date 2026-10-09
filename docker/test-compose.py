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
                                 f'BAKABASE_DATA_DIR="{self.root}/app data"\n'
                                 f'BAKABASE_DOWNLOADS_DIR="{self.root}/downloads"\n')
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
        self.assertEqual(image_service["volumes"][0]["source"], str(self.root / "app data"))
        self.assertFalse(image_service["volumes"][0]["bind"]["create_host_path"])
        downloads = next(v for v in image_service["volumes"] if v["target"] == "/downloads")
        self.assertEqual(downloads["source"], str(self.root / "downloads"))
        self.assertFalse(downloads.get("read_only", False))
        self.assertFalse(downloads["bind"]["create_host_path"])

    def test_local_mounts_apply_identically_to_both_modes(self):
        self.env.update(BAKABASE_COMPOSE_OVERRIDE=str(ROOT / "docker/compose.local.example.yaml"),
                        BAKABASE_MEDIA_ROOT_1=str(self.root / "nas one"),
                        BAKABASE_MEDIA_ROOT_2=str(self.root / "nas two"),
                        BAKABASE_IMPORT_DIR=str(self.root / "import copy"))
        image, source = self.config("image"), self.config("source")
        service = image["services"]["server"]
        self.assertEqual(service["platform"], "linux/arm64")
        self.assertEqual(service["volumes"], source["services"]["server"]["volumes"])
        mounts = {entry["target"]: entry for entry in service["volumes"]}
        self.assertEqual(set(mounts), {"/data", "/downloads", "/import", "/Volumes/nas-bakabase", "/Volumes/nas-anobaka"})
        for target, mount in mounts.items():
            self.assertFalse(mount["bind"]["create_host_path"])
            self.assertEqual(mount.get("read_only", False), target not in ("/data", "/downloads"))

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
