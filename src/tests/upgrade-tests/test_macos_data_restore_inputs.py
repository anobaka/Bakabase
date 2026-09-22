#!/usr/bin/env python3
"""Pure producer-provenance tests; never downloads or launches native software."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile


SPEC = importlib.util.spec_from_file_location("restore_inputs_test", Path(__file__).with_name("prepare-macos-data-restore.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)
HEAD = "a" * 40
RUN = 123
REPO = "anobaka/Bakabase"


def run_metadata():
    return {"id": RUN, "status": "completed", "conclusion": "failure", "head_sha": HEAD,
            "repository": {"full_name": REPO}, "head_repository": {"full_name": REPO},
            "path": ".github/workflows/ci.yml"}


def artifact_metadata():
    return {"id": 456, "name": runner.ARTIFACT, "expired": False, "size_in_bytes": 10,
            "digest": "sha256:" + "b" * 64, "workflow_run": {"id": RUN, "head_sha": HEAD}}


def report_fixture():
    return {"passed": False, "dataRetentionPassed": False, "currentStage": "later-update-failed",
            "executionHeadSHA": HEAD, "rid": "osx-arm64", "sourceSHA": runner.release.CANDIDATE_SHA,
            "candidateCoreVersion": runner.release.CANDIDATE_CORE, "scope": runner.SCOPE,
            "packageVersion": runner.release.OLD_VERSION,
            "originalVersions": {role: runner.release.OLD_VERSION for role in ("client", "unified")},
            "dataSeed": {"passed": True, "oldVersion": runner.release.OLD_VERSION,
                         "oldSourceSHA": runner.release.RELEASE_SHA},
            "originalDatabaseBackup": {"file": "before-update.sqlite", "integrity": "ok",
                                       "foreignKeyViolations": [], "sizeBytes": 8,
                                       "sha256": hashlib.sha256(b"database").hexdigest()},
            "originalConfigurationBackups": {"client": {}, "unified": {}}}


def write_report(root, report=None):
    report = report_fixture() if report is None else report
    target = root / "historical-results/unified"
    target.mkdir(parents=True)
    (target / "before-update.sqlite").write_bytes(b"database")
    (target / "before-tables.json").write_text("{}")
    (root / "historical-results/report.json").write_text(json.dumps(report))
    return report


def fixture_zip(path, report=None):
    report = report_fixture() if report is None else report
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("historical-results/report.json", json.dumps(report))
        archive.writestr("historical-results/unified/before-update.sqlite", b"database")
        archive.writestr("historical-results/unified/before-tables.json", "{}")


class ProducerGuards(unittest.TestCase):
    def test_completed_failure_is_allowed_but_identity_and_workflow_are_exact(self):
        value = run_metadata()
        self.assertEqual(HEAD, runner.validate_run(value, REPO, RUN))
        self.assertEqual(HEAD, runner.validate_run(dict(value, conclusion="success"), REPO, RUN))
        for change in ({"id": RUN + 1}, {"id": True}, {"status": "in_progress"}, {"conclusion": None},
                       {"conclusion": "cancelled"}, {"head_sha": "main"}, {"head_sha": "A" * 40},
                       {"repository": {"full_name": "other/repo"}}, {"head_repository": {"full_name": "other/repo"}},
                       {"path": ".github/workflows/_extended_acceptance.yml"}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                runner.validate_run(dict(value, **change), REPO, RUN)

    def test_artifact_exact_name_inventory_size_digest_and_run_binding(self):
        artifact = artifact_metadata()
        metadata = {"total_count": 1, "artifacts": [artifact]}
        self.assertEqual(artifact, runner.select_artifact(metadata, RUN, HEAD))
        for change in ({"expired": True}, {"id": 0}, {"size_in_bytes": runner.MAX_ZIP_BYTES + 1},
                       {"size_in_bytes": True}, {"digest": None}, {"name": "package-acceptance-unified-osx-arm64-packages"},
                       {"workflow_run": {"id": RUN + 1, "head_sha": HEAD}},
                       {"workflow_run": {"id": RUN, "head_sha": "c" * 40}}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                runner.select_artifact({"total_count": 1, "artifacts": [dict(artifact, **change)]}, RUN, HEAD)
        for metadata in ({"total_count": 101, "artifacts": [artifact]}, {"total_count": True, "artifacts": [artifact]},
                         {"total_count": 2, "artifacts": [artifact]}, {"total_count": 2, "artifacts": [artifact, artifact]},
                         {"total_count": 0, "artifacts": []}):
            with self.subTest(metadata=metadata), self.assertRaises(ValueError):
                runner.select_artifact(metadata, RUN, HEAD)

    def test_report_requires_verified_old_baseline_not_overall_update_success(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            report = write_report(root)
            actual, sha = runner.validate_report(root, HEAD)
            self.assertFalse(actual["passed"])
            self.assertEqual(report, actual)
            self.assertEqual(hashlib.sha256((root / "historical-results/report.json").read_bytes()).hexdigest(), sha)
            for field, value in (("executionHeadSHA", "b" * 40), ("rid", "osx-x64"), ("sourceSHA", "b" * 40),
                                 ("candidateCoreVersion", "2.4.0-beta.399"), ("scope", "historical"),
                                 ("packageVersion", "2.4.0-beta.348"), ("originalVersions", {}),
                                 ("originalConfigurationBackups", {}), ("originalDatabaseBackup", {}),
                                 ("dataSeed", {}), ("dataSeed", dict(report["dataSeed"], passed=False))):
                (root / "historical-results/report.json").write_text(json.dumps(dict(report, **{field: value})))
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    runner.validate_report(root, HEAD)

    def test_report_missing_modified_or_symlink_baseline_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_report(root)
            database = root / "historical-results/unified/before-update.sqlite"
            database.write_bytes(b"modified")
            with self.assertRaisesRegex(ValueError, "bytes differ"):
                runner.validate_report(root, HEAD)
            database.unlink()
            with self.assertRaisesRegex(ValueError, "Missing"):
                runner.validate_report(root, HEAD)
            target = root / "other"
            target.write_bytes(b"database")
            database.symlink_to(target)
            with self.assertRaisesRegex(ValueError, "Missing"):
                runner.validate_report(root, HEAD)
            database.unlink()
            database.write_bytes(b"database")
            (root / "historical-results/unified/before-tables.json").unlink()
            with self.assertRaisesRegex(ValueError, "Missing"):
                runner.validate_report(root, HEAD)

    def test_archive_preflight_rejects_unsafe_paths_symlinks_supplied_marker_and_collisions(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "evidence.zip"
            fixture_zip(path)
            runner.validate_archive(path)
            for name in ("../escape", "historical-results/../escape", "verified-producer.json", "Historical-results/report.json"):
                fixture_zip(path)
                with zipfile.ZipFile(path, "a") as archive:
                    archive.writestr(name, "{}")
                with self.subTest(name=name), self.assertRaises((ValueError, AssertionError)):
                    runner.validate_archive(path)
            fixture_zip(path)
            link = zipfile.ZipInfo("owned-link")
            link.external_attr = (stat.S_IFLNK | 0o777) << 16
            with zipfile.ZipFile(path, "a") as archive:
                archive.writestr(link, "historical-results/report.json")
            with self.assertRaisesRegex(ValueError, "regular files"):
                runner.validate_archive(path)

    def test_archive_expansion_is_bounded_before_unpacking(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "evidence.zip"
            fixture_zip(path)
            with patch.object(runner, "MAX_EXPANDED_BYTES", 1), self.assertRaisesRegex(ValueError, "expansion"):
                runner.validate_archive(path)
            with patch.object(runner, "MAX_DATABASE_BYTES", 1), self.assertRaisesRegex(ValueError, "member"):
                runner.validate_archive(path)

    def test_hosted_guard_is_before_filesystem_git_or_network(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(runner.subprocess, "check_output") as git, \
             patch.object(runner.inputs, "gh_json") as network, patch.object(runner.inputs, "download") as download, \
             patch.object(runner.Path, "resolve", side_effect=AssertionError("filesystem accessed")):
            with self.assertRaisesRegex(AssertionError, "GitHub-hosted"):
                runner.prepare(REPO, str(RUN), "osx-x64", Path("/never-created"))
        git.assert_not_called()
        network.assert_not_called()
        download.assert_not_called()

    def test_only_new_owned_runner_directory_and_clean_checkout_are_allowed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            (root / "exists").mkdir()
            (root / "alias").symlink_to(root / "exists", target_is_directory=True)
            with patch.object(runner.release, "hosted"), patch.dict(os.environ, {"RUNNER_TEMP": str(root)}), \
                 patch.object(runner.inputs, "gh_json") as network, patch.object(runner.subprocess, "check_output", return_value=b"") as git:
                for output in (root, root / "exists", root.parent / "outside", Path("relative"), root / "alias/new"):
                    with self.subTest(output=output), self.assertRaises(ValueError):
                        runner.prepare(REPO, str(RUN), "osx-x64", output)
                git.return_value = b" M file"
                with self.assertRaisesRegex(ValueError, "clean tracked"):
                    runner.prepare(REPO, str(RUN), "osx-x64", root / "new")
                network.assert_not_called()
                self.assertFalse((root / "new").exists())

    def test_wrong_repository_run_id_and_consumer_rid_never_request_metadata(self):
        with patch.object(runner.release, "hosted"), patch.object(runner.inputs, "gh_json") as network:
            for repository, run_id, rid in (("other/repo", "123", "osx-x64"), (REPO, "0", "osx-x64"),
                                           (REPO, "123/other", "osx-x64"), (REPO, "123", "osx-arm64")):
                with self.subTest(repository=repository, run_id=run_id, rid=rid), self.assertRaises(ValueError):
                    runner.prepare(repository, run_id, rid, Path("/unused"))
            network.assert_not_called()

    def test_single_download_hash_validation_exact_marker_and_failure_outcome(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            archive = root / "source.zip"
            fixture_zip(archive)
            artifact = dict(artifact_metadata(), size_in_bytes=archive.stat().st_size,
                            digest="sha256:" + hashlib.sha256(archive.read_bytes()).hexdigest())
            def download(repository, actual, target):
                self.assertEqual(REPO, repository)
                self.assertEqual(artifact, actual)
                target.write_bytes(archive.read_bytes())
            with patch.object(runner.release, "hosted"), patch.dict(os.environ, {"RUNNER_TEMP": str(root)}), \
                 patch.object(runner.subprocess, "check_output", return_value=b""), \
                 patch.object(runner.inputs, "gh_json", side_effect=[run_metadata(), {"total_count": 1, "artifacts": [artifact]}]) as metadata, \
                 patch.object(runner.inputs, "download", side_effect=download) as fetched:
                evidence, provenance = runner.prepare(REPO, str(RUN), "osx-x64", root / "output")
            fetched.assert_called_once()
            self.assertEqual(2, metadata.call_count)
            marker = json.loads((evidence / "verified-producer.json").read_text())
            self.assertEqual({"verified": True, "area": "macos-data", "rid": "osx-arm64", "runId": RUN,
                              "headSHA": HEAD, "artifactSHA256": artifact["digest"][7:],
                              "reportSHA256": hashlib.sha256((evidence / "historical-results/report.json").read_bytes()).hexdigest()}, marker)
            result = json.loads(provenance.read_text())
            self.assertTrue(result["passed"])
            self.assertFalse(result["producerOutcome"]["passed"])
            self.assertEqual("failure", result["runConclusion"])
            self.assertEqual(artifact["id"], result["artifact"]["id"])

    def test_bad_download_never_unpacks_retries_or_writes_verified_marker(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            output = root / "output"
            with patch.object(runner.release, "hosted"), patch.dict(os.environ, {"RUNNER_TEMP": str(root)}), \
                 patch.object(runner.subprocess, "check_output", return_value=b""), \
                 patch.object(runner.inputs, "gh_json", side_effect=[run_metadata(), {"total_count": 1, "artifacts": [artifact_metadata()]}]), \
                 patch.object(runner.inputs, "download", side_effect=lambda _repo, _meta, path: path.write_bytes(b"incorrect!")) as fetched, \
                 patch.object(runner.inputs.package, "unpack") as unpack:
                with self.assertRaisesRegex(ValueError, "digest differs"):
                    runner.prepare(REPO, str(RUN), "osx-x64", output)
            fetched.assert_called_once()
            unpack.assert_not_called()
            self.assertFalse((output / "evidence/verified-producer.json").exists())
            self.assertFalse(json.loads((output / "provenance.json").read_text())["passed"])

    def test_cli_publishes_exact_producer_and_provenance_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            output = root / "output"
            github_output = root / "github-output"
            with patch.object(sys, "argv", ["prepare-macos-data-restore.py", "--repository", REPO,
                                            "--run-id", str(RUN), "--rid", "osx-x64",
                                            "--output-directory", str(output)]), \
                 patch.object(runner, "prepare", return_value=(output / "evidence", output / "provenance.json")) as prepare, \
                 patch.dict(os.environ, {"GITHUB_OUTPUT": str(github_output)}), patch("builtins.print"):
                runner.main()
            prepare.assert_called_once_with(REPO, str(RUN), "osx-x64", output)
            self.assertEqual("producer_directory=" + str(output / "evidence") + "\nprovenance=" +
                             str(output / "provenance.json") + "\n", github_output.read_text())


if __name__ == "__main__":
    unittest.main()
