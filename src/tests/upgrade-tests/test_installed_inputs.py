#!/usr/bin/env python3
"""Artifact provenance failures; no downloads or native applications."""
import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("installed_inputs", Path(__file__).with_name("prepare-installed-inputs.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)
SHA = "a" * 40


class InputGuards(unittest.TestCase):
    def test_only_successful_same_repository_runs_are_reused(self):
        run = {"status": "completed", "conclusion": "success", "head_sha": SHA,
               "repository": {"full_name": "owner/repo"}, "head_repository": {"full_name": "owner/repo"},
               "path": ".github/workflows/ci.yml"}
        self.assertEqual(SHA, runner.validate_run(run, "owner/repo"))
        for changes in ({"status": "in_progress"}, {"conclusion": "failure"}, {"head_sha": "main"},
                        {"head_repository": {"full_name": "fork/repo"}},
                        {"repository": {"full_name": "another/repo"}}, {"path": ".github/workflows/deploy.yml"}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                runner.validate_run(dict(run, **changes), "owner/repo")

    def test_changed_product_or_submodule_requires_new_packages(self):
        runner.validate_changes(["docs/acceptance.md", "src/tests/upgrade-tests/example.py", ".github/workflows/ci.yml"])
        for path in ("src/apps/Bakabase.App/Program.cs", "src/web/index.html", "src/libs/Bakabase.Infrastructures",
                     "global.json", "src/scripts/prepare-macos-plist.py", ".github/workflows/_build.yml"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                runner.validate_changes([path])

    def test_missing_duplicate_expired_or_unverifiable_artifacts_are_rejected(self):
        item = {"id": 1, "name": "fixture", "expired": False, "size_in_bytes": 3, "digest": "sha256:" + "b" * 64}
        self.assertEqual(item, runner.select_artifact([item], "fixture"))
        for items in ([], [item, item], [dict(item, expired=True)], [dict(item, digest=None)],
                      [dict(item, size_in_bytes=runner.LIMIT + 1)], [dict(item, id="1")]):
            with self.subTest(items=items), self.assertRaises(ValueError):
                runner.select_artifact(items, "fixture")

    def test_archive_bytes_must_match_digest_and_size(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "archive.zip"
            path.write_bytes(b"fixture")
            expected = {"size_in_bytes": 7, "digest": "sha256:" + hashlib.sha256(b"fixture").hexdigest()}
            self.assertEqual(expected["digest"], runner.verify_archive(path, expected))
            for bad in (dict(expected, size_in_bytes=8), dict(expected, digest="sha256:" + "0" * 64)):
                with self.subTest(bad=bad), self.assertRaises(ValueError):
                    runner.verify_archive(path, bad)

    def test_evidence_must_pass_and_match_commit_role_architecture_and_cleanup(self):
        report = {"passed": True, "headSHA": SHA, "role": "client", "rid": "osx-arm64",
                  "ownedFilesRemoved": True, "cleanupErrors": [], "version": "0.0.1-acceptance.123.1"}
        self.assertEqual(report["version"], runner.validate_report(report, SHA, "client", "osx-arm64"))
        for changes in ({"passed": False}, {"headSHA": "b" * 40}, {"role": "unified"}, {"rid": "win-x64"},
                        {"ownedFilesRemoved": False}, {"cleanupErrors": ["error"]}, {"version": "2.4.0"}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                runner.validate_report(dict(report, **changes), SHA, "client", "osx-arm64")

    def test_package_files_require_safe_names_and_individual_hashes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            fixtures = {}
            for kind in ("portable", "full", "installer"):
                name = kind + ".fixture"
                (root / name).write_bytes(kind.encode())
                fixtures[kind] = {"file": name, "sizeBytes": len(kind), "sha256": hashlib.sha256(kind.encode()).hexdigest()}
            report = {"packages": {"artifacts": fixtures}}
            self.assertEqual(fixtures, runner.verify_package_files(root, report))
            for name in ("../escape", "/tmp/escape", "C:escape", "dir\\escape", "name\nextra"):
                bad = copy.deepcopy(report)
                bad["packages"]["artifacts"]["installer"]["file"] = name
                with self.subTest(name=name), self.assertRaises(ValueError):
                    runner.verify_package_files(root, bad)
            (root / "installer.fixture").write_bytes(b"x" * len("installer"))
            with self.assertRaisesRegex(ValueError, "hash differs"):
                runner.verify_package_files(root, report)


if __name__ == "__main__":
    unittest.main()
