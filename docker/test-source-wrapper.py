"""Exercise the source command wrapper without building or starting containers."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class SourceWrapperTest(unittest.TestCase):
    def test_passes_resolved_version_argv_and_exit_code(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            docker = folder / "docker"
            docker.write_text('#!/bin/sh\ncase "$1" in image) exit 0;; run) echo 1.2.3;; *) exit 90;; esac\n')
            docker.chmod(0o700)
            wrapper = folder / "custom wrapper"
            wrapper.write_text('#!/usr/bin/env python3\nimport json,os,sys\n'
                               'print(json.dumps([os.environ["BAKABASE_VERSION"], *sys.argv[1:]]))\nsys.exit(37)\n')
            wrapper.chmod(0o700)
            env = {**os.environ, "PATH": tmp + os.pathsep + os.environ["PATH"], "BAKABASE_SOURCE_WRAPPER": str(wrapper)}
            for args, expected in [([], ["up", "-d", "--build"]), (["-f", "a b", "build"], ["-f", "a b", "build"])]:
                result = subprocess.run([ROOT / "docker/source.sh", *args], env=env, capture_output=True, text=True)
                self.assertEqual(result.returncode, 37, result.stderr)
                self.assertEqual(json.loads(result.stdout.splitlines()[-1]),
                                 ["1.2.3", str(ROOT / "docker/compose.sh"), "source", *expected])
            env["BAKABASE_SOURCE_WRAPPER"] = ""
            # The fake docker returns 90 when reached through the normal Compose invocation.
            result = subprocess.run([ROOT / "docker/source.sh", "config"], env=env, capture_output=True)
            self.assertEqual(result.returncode, 90)


if __name__ == "__main__":
    unittest.main()
