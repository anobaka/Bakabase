#!/usr/bin/env python3
"""Pure automatic-updater guards; never run an updater, application or installer."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path, PurePosixPath
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location("installed_update_exercise", Path(__file__).with_name("installed-update-exercise.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class UpdaterApiGuards(unittest.TestCase):
    def test_check_requires_exact_installed_running_channel_and_target_versions(self):
        valid = {"installedVersion": "old", "runningVersion": "core", "channel": "win",
                 "version": "new", "updateCheckUnavailable": False}
        self.assertEqual(valid, runner.validate_check(valid, "old", "core", "win", "new"))
        for key, value in (("installedVersion", "other"), ("runningVersion", "new"),
                           ("channel", "acceptance"), ("version", None), ("updateCheckUnavailable", True)):
            with self.subTest(key=key), self.assertRaises(AssertionError):
                runner.validate_check(dict(valid, **{key: value}), "old", "core", "win", "new")
        with self.assertRaises(AssertionError):
            runner.validate_check(None, "old", "core", "win", "new")

    def test_after_update_check_requires_no_higher_version(self):
        valid = {"installedVersion": "new", "runningVersion": "core", "channel": "osx",
                 "updateCheckUnavailable": False}
        runner.validate_check(valid, "new", "core", "osx", None)
        with self.assertRaises(AssertionError):
            runner.validate_check(dict(valid, version="future"), "new", "core", "osx", None)

    def test_pending_restart_requires_completed_download_not_only_http_success(self):
        valid = {"status": 3, "failedFileCount": 0, "downloadedFileCount": 1, "totalFileCount": 1}
        with patch.object(runner, "read_state", return_value=valid):
            self.assertEqual(3, runner.wait_pending({})[0]["state"]["status"])
        for state in (dict(valid, status=5), dict(valid, status=6), dict(valid, error="checksum mismatch"),
                      dict(valid, downloadedFileCount=0), dict(valid, failedFileCount=1)):
            with self.subTest(state=state), patch.object(runner, "read_state", return_value=state), self.assertRaises(AssertionError):
                runner.wait_pending({})

    def test_restart_http_errors_fail_while_disconnect_is_only_an_unproven_outcome(self):
        failure = urllib.error.HTTPError("http://127.0.0.1", 500, "failed", {}, None)
        with patch.object(runner, "api", side_effect=failure), self.assertRaises(urllib.error.HTTPError):
            runner.trigger_restart({})
        with patch.object(runner, "api", side_effect=ConnectionResetError("exit")):
            result = runner.trigger_restart({})
            self.assertEqual("connection-ended", result["outcome"])
            self.assertNotIn("passed", result)

    def test_failed_download_retains_exact_state_for_diagnosis(self):
        history = []
        failure = {"status": 5, "error": "SHA256 does not match"}
        with patch.object(runner, "read_state", return_value=failure), self.assertRaisesRegex(AssertionError, "SHA256"):
            runner.wait_pending({}, history)
        self.assertEqual([failure], [entry["state"] for entry in history])


class PackageAndCacheGuards(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.app = {"role": "unified", "rid": "win-x64", "exe": self.root / "Bakabase.exe"}
        (self.root / "Bakabase.exe").write_bytes(b"MZfixture")
        (self.root / "Bakabase.dll").write_bytes(b"unchanged product")
        self.old = {"id": "Bakabase", "mainExe": "Bakabase.exe", "rid": "win-x64", "version": "old", "channel": "acceptance"}
        self.new = dict(self.old, version="new", channel="win")
        self.write_manifest(self.old)
        self.entry = {"oldManifest": self.old, "newManifest": self.new, "oldPayloadHashes": runner.inventory(self.root),
                      "vendorGenerated": {"allowedNames": ["sq.version", "Update.exe", "Squirrel.exe", "Bakabase_ExecutionStub.exe"]},
                      "markerPath": "updater-acceptance.json", "marker": {"role": "unified", "newVersion": "new", "nonce": "fixture"}}
        self.write_manifest(self.new)
        (self.root / "updater-acceptance.json").write_text(json.dumps(self.entry["marker"]))
        self.entry["newPayloadHashes"] = runner.inventory(self.root)

    def write_manifest(self, data):
        element = ET.Element("package")
        for key, value in data.items():
            ET.SubElement(element, key).text = value
        (self.root / "sq.version").write_bytes(ET.tostring(element))

    def test_new_manifest_marker_and_every_product_file_are_required(self):
        self.assertTrue(runner.validate_payload(self.app, self.entry, True)["passed"])
        product = self.root / "Bakabase.dll"
        product.write_bytes(b"unexpected replacement")
        with self.assertRaisesRegex(AssertionError, "payload differs"):
            runner.validate_payload(self.app, self.entry, True)

    def test_new_pid_cannot_hide_an_old_manifest_or_wrong_marker(self):
        self.write_manifest(self.old)
        with self.assertRaisesRegex(AssertionError, "manifest differs"):
            runner.validate_payload(self.app, self.entry, True)
        self.write_manifest(self.new)
        (self.root / "updater-acceptance.json").write_text("{}")
        self.entry["newPayloadHashes"] = runner.inventory(self.root)
        with self.assertRaisesRegex(AssertionError, "marker differs"):
            runner.validate_payload(self.app, self.entry, True)

    def test_metadata_allowlist_cannot_exempt_product_assemblies(self):
        self.entry["vendorGenerated"]["allowedNames"].append("Bakabase.dll")
        with self.assertRaisesRegex(AssertionError, "exact platform metadata"):
            runner.validate_payload(self.app, self.entry, True)

    def test_cache_requires_size_hash_and_completed_matching_role_delivery(self):
        data = b"the actual full package"
        filename = "Bakabase-new-full.nupkg"
        (self.root / filename).write_bytes(data)
        entry = {"packageChecksums": {"newFull": {"fileName": filename, "sizeBytes": len(data),
                                                  "sha256": hashlib.sha256(data).hexdigest()}}}
        delivery = {"role": "unified", "kind": "new", "file": filename, "bytes": len(data), "completed": True}
        with patch.object(runner, "update_paths", return_value={"cache": self.root}):
            self.assertTrue(runner.validate_download(self.app, entry, [delivery])["passed"])
            for field, wrong in (("role", "client"), ("kind", "old"), ("completed", False), ("bytes", 0)):
                with self.subTest(field=field), self.assertRaises(AssertionError):
                    runner.validate_download(self.app, entry, [dict(delivery, **{field: wrong})])
            (self.root / filename).write_bytes(b"x" * len(data))
            with self.assertRaisesRegex(AssertionError, "Default-cache"):
                runner.validate_download(self.app, entry, [delivery])


class NativeProcessEvidence(unittest.TestCase):
    def test_first_multipath_snapshot_identifies_both_apps_and_allows_absent_updaters(self):
        root = Path(tempfile.gettempdir())
        paths = [root / "Bakabase/current/Bakabase.exe", root / "Bakabase/Update.exe",
                 root / "Bakabase.Client/current/Bakabase.Client.exe", root / "Bakabase.Client/Update.exe"]
        observer = runner.ProcessObserver(paths)
        rows = [{"pid": pid, "executable": str(path), "startedUtc": "2026-09-22T01:02:03Z", "startedEpoch": 1}
                for pid, path in ((42, paths[0]), (43, paths[2]))]
        wire = json.dumps({"observedAt": 10, "processes": rows,
                           "query": {"targets": [str(path) for path in paths]}})
        observer._remember_windows_stdout(wire)
        snapshot = runner.parse_windows_snapshot(wire)
        observer._accept(snapshot["processes"], snapshot["observedAt"])
        self.assertEqual(1, observer.sample_count)
        self.assertEqual(42, runner.one_process(observer, paths[0])["pid"])
        self.assertEqual(43, runner.one_process(observer, paths[2])["pid"])
        self.assertEqual([], observer.current(paths[1]))
        self.assertEqual([], observer.current(paths[3]))
        evidence = observer.diagnostics()
        self.assertEqual({42, 43}, {row["pid"] for row in evidence["current"]})
        self.assertEqual(2, evidence["seenCount"])
        self.assertEqual(wire, evidence["windowsStdout"]["first"]["text"])
        with self.assertRaisesRegex(AssertionError, "found 0"):
            runner.one_process(observer, paths[1])

    def test_snapshot_diagnostics_retain_first_and_last_with_bounded_history(self):
        path = Path(tempfile.gettempdir()) / "fixture-native-app"
        observer = runner.ProcessObserver([path])
        first = "bad-first-line" + "x" * 9000
        observer._remember_windows_stdout(first)
        observer._remember_windows_stdout("intermediate")
        observer._remember_windows_stdout("last")
        for pid in range(1, 71):
            observer._accept([{"pid": pid, "executable": str(path), "startedUtc": "start", "startedEpoch": 1}], 10)
        evidence = observer.diagnostics()
        self.assertEqual({"text": first[:8192], "characters": len(first), "truncated": True},
                         evidence["windowsStdout"]["first"])
        self.assertEqual("last", evidence["windowsStdout"]["last"]["text"])
        self.assertEqual(3, evidence["windowsStdout"]["lines"])
        self.assertEqual(64, len(evidence["seen"]))
        self.assertEqual(70, evidence["seenCount"])
        self.assertEqual(1, evidence["currentCount"])
        self.assertEqual(70, evidence["current"][0]["pid"])
        self.assertTrue(evidence["processRecordsTruncated"])

    def test_powershell_stdout_is_strict_json_and_stderr_is_bounded_separate_evidence(self):
        snapshot = {"observedAt": 10, "processes": []}
        self.assertEqual(snapshot, runner.parse_windows_snapshot(json.dumps(snapshot)))
        for noise in ("#< CLIXML\n", "Preparing modules for first use", "", "[broken"):
            with self.subTest(noise=noise), self.assertRaisesRegex(ValueError, "Non-JSON PowerShell observer stdout"):
                runner.parse_windows_snapshot(noise)
        with self.assertRaises(AssertionError):
            runner.parse_windows_snapshot("[]")
        observer = runner.ProcessObserver([Path(tempfile.gettempdir()) / "fixture-native-app"])
        diagnostics = "#< CLIXML\n" + "module diagnostic " * 2000
        observer._helper = SimpleNamespace(stderr=io.StringIO(diagnostics))
        observer._windows_stderr_loop()
        actual = observer.diagnostics()
        self.assertEqual(diagnostics[:8192], actual["stderr"])
        self.assertEqual(len(diagnostics), actual["stderrCharacters"])
        self.assertTrue(actual["stderrTruncated"])
        self.assertEqual(0, observer.sample_count)

    def test_macos_full_paths_with_spaces_and_utc_start_times_are_preserved(self):
        # This is macOS ps wire text even when the test host is Windows.
        path = PurePosixPath("/Applications/Bakabase Client.app/Contents/MacOS/Bakabase.Client")
        records = runner.parse_macos_processes("42 Tue Sep 22 01:02:03 2026 " + str(path), [path], 10)
        self.assertEqual(42, records[0]["pid"])
        self.assertEqual(str(path), records[0]["executable"])
        self.assertEqual("2026-09-22T01:02:03Z", records[0]["startedUtc"])
        self.assertEqual(1, records[0]["startResolutionSeconds"])

    def test_pid_reuse_does_not_overwrite_start_identity_or_first_seen(self):
        path = Path(tempfile.gettempdir()) / "fixture-native-app"
        observer = runner.ProcessObserver([path])  # No context entry: no native query.
        first = {"pid": 42, "executable": str(path), "startedUtc": "start-1", "startedEpoch": 1}
        observer._accept([first], 10)
        observer._accept([first], 20)
        self.assertEqual(10, observer.current(path)[0]["firstSeenEpoch"])
        observer._accept([dict(first, startedUtc="start-2", startedEpoch=2)], 30)
        self.assertEqual(2, len(observer.seen(path)))
        self.assertNotEqual(runner.identity(first), runner.identity(observer.current(path)[0]))

    def test_native_apply_chain_requires_observed_pid_correct_package_wait_and_restart(self):
        cache = Path(tempfile.gettempdir()) / "fixture-full.nupkg"
        prefix = "[update:45] [12:00:00] [INFO] "
        lines = ["Command: Apply", "Restart: true", "Wait: WaitPid(42)",
                 "Package: " + str(cache), "Package version new applied successfully."]
        log = "\n".join(prefix + line for line in lines)
        record = {"pid": 45, "startedUtc": "start", "executable": "/owned/UpdateMac"}
        entry = {"newManifest": {"version": "new"}}
        self.assertEqual(record, runner.verify_native_chain(log, entry, 42, cache, [record])["process"])
        for changed in (log.replace("Restart: true", "Restart: false"), log.replace("WaitPid(42)", "WaitPid(1)"),
                        log.replace("version new", "version old"), log.replace(str(cache), "/another/package")):
            with self.subTest(changed=changed), self.assertRaises(AssertionError):
                runner.verify_native_chain(changed, entry, 42, cache, [record])
        with self.assertRaises(AssertionError):
            runner.verify_native_chain(log, entry, 42, cache, [])

    def test_observer_failure_cannot_use_a_stale_snapshot_as_live_evidence(self):
        observer = SimpleNamespace(errors=["snapshot deadline"], current=lambda path: [{"pid": 1}])
        with self.assertRaisesRegex(AssertionError, "observer failed"):
            runner.one_process(observer, Path("/fixture"))


if __name__ == "__main__":
    unittest.main()
