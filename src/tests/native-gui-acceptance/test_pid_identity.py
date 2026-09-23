#!/usr/bin/env python3
"""No OS process reads: exercise only fake libproc results and child execution."""
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, HERE / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


pid = load("pid_identity_test_module", "macos_pid_identity.py")
probe = load("pid_diagnostic_probe_test", "probe.py")
IDENTITY = {"pid": 900, "ppid": 1, "uid": 501, "startSeconds": 100, "startMicroseconds": 123,
            "executable": "/System/Library/Frameworks/WebKit.framework/WebContent"}
DIAGNOSTIC = {"expectedPid": 42, "actualPid": 900, "observedEpochMs": 100001,
              "origin": "owned-child-edge", "path": [0, 1], "status": "observed"}
ENV = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "/fixture"}


class IdentityGuards(unittest.TestCase):
    def test_local_and_wrong_architecture_are_rejected_before_process_read(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(pid, "sample") as sample:
            with self.assertRaisesRegex(pid.IdentityFailure, "HostedMacRequired"):
                pid.observe("osx-arm64", 900, 100001)
        sample.assert_not_called()
        with patch.dict(os.environ, ENV, clear=True), patch.object(pid.platform, "system", return_value="Darwin"), \
                patch.object(pid.platform, "machine", return_value="x86_64"), patch.object(pid, "sample") as sample:
            with self.assertRaisesRegex(pid.IdentityFailure, "HostedMacRequired"):
                pid.observe("osx-arm64", 900, 100001)
        sample.assert_not_called()

    def test_exact_pid_is_sampled_twice_and_only_allowlisted_fields_are_returned(self):
        with patch.object(pid, "hosted"), patch.object(pid, "sample", return_value={**IDENTITY, "argv": "SECRET"}) as sample:
            result = pid.observe("osx-arm64", 900, 100001)
        self.assertEqual([((900,), {}), ((900,), {})], sample.call_args_list)
        self.assertEqual(IDENTITY, result)
        self.assertNotIn("SECRET", json.dumps(result))

    def test_reuse_identity_changes_and_exit_cannot_be_stable(self):
        for key, value in (("ppid", 2), ("uid", 502), ("startMicroseconds", 124), ("startSeconds", 99),
                           ("executable", "/Other")):
            with self.subTest(key=key), patch.object(pid, "hosted"), \
                    patch.object(pid, "sample", side_effect=[IDENTITY, {**IDENTITY, key: value}]):
                with self.assertRaisesRegex(pid.IdentityFailure, "ProcessIdentityChanged"):
                    pid.observe("osx-arm64", 900, 100001)
        with patch.object(pid, "hosted"), patch.object(pid, "sample", side_effect=[IDENTITY, pid.IdentityFailure("ProcessUnavailable")]):
            with self.assertRaisesRegex(pid.IdentityFailure, "ProcessUnavailable"):
                pid.observe("osx-arm64", 900, 100001)
        with patch.object(pid, "hosted"), patch.object(pid, "sample", return_value=IDENTITY):
            with self.assertRaisesRegex(pid.IdentityFailure, "ProcessStartedAfterAXObservation"):
                pid.observe("osx-arm64", 900, 99999)

    def test_libproc_reads_only_requested_pid_with_exact_public_struct_size(self):
        class Call:
            def __init__(self, callback): self.callback, self.calls = callback, []
            def __call__(self, *args):
                self.calls.append(args)
                return self.callback(*args)
        def info(process, flavor, arg, pointer, size):
            self.assertEqual((900, 3, 0, 136), (process, flavor, arg, size))
            record = pointer._obj
            for key, value in {"pid": 900, "ppid": 1, "uid": 501, "start_seconds": 100, "start_microseconds": 123}.items():
                setattr(record, key, value)
            return 136
        def path(process, buffer, size):
            self.assertEqual((900, 4096), (process, size))
            buffer.value = IDENTITY["executable"].encode()
            return len(buffer.value)
        library = SimpleNamespace(proc_pidinfo=Call(info), proc_pidpath=Call(path))
        with patch.object(pid.ctypes, "CDLL", return_value=library):
            self.assertEqual(IDENTITY, pid.sample(900))
        self.assertEqual(1, len(library.proc_pidinfo.calls))
        self.assertEqual(1, len(library.proc_pidpath.calls))

    def test_bounded_child_and_sanitized_failures_never_become_ownership(self):
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / "Bakabase"
            executable.touch()
            app = {"role": "unified", "rid": "osx-arm64", "exe": executable}
            mac_executable = "/fixture/Bakabase"
            def simulated_mac_path(value):
                actual = Path(value)
                if actual == executable:
                    # Keep real temporary-file existence checks, while the
                    # mocked libproc boundary sees a Mac path on every host.
                    return SimpleNamespace(name=actual.name, is_absolute=actual.is_absolute,
                        is_file=actual.is_file, resolve=lambda: PurePosixPath(mac_executable))
                return actual
            for child, expected in ((SimpleNamespace(returncode=0, stdout=json.dumps({"code": "ObservedStable", "identity": IDENTITY,
                                        "ownedIdentity": {**IDENTITY, "pid": 42, "executable": mac_executable}}).encode()), "ObservedStable"),
                                    (SimpleNamespace(returncode=1, stdout=b"SECRET"), "DiagnosticUnavailable"),
                                    (SimpleNamespace(returncode=0, stdout=b'{"code":"SECRET"}'), "DiagnosticUnavailable"),
                                    (subprocess.TimeoutExpired(["fixture"], 2, output=b"SECRET", stderr=b"SECRET"), "DiagnosticTimedOut")):
                with self.subTest(expected=expected), patch.object(pid, "hosted"), \
                        patch.object(pid, "Path", side_effect=simulated_mac_path), \
                        patch.object(pid.subprocess, "run", **({"side_effect": child} if isinstance(child, Exception) else {"return_value": child})) as run:
                    result = pid.capture(app, DIAGNOSTIC, 2)
                self.assertEqual(expected, result["code"])
                self.assertIs(False, result["ownershipEstablished"])
                self.assertEqual(expected == "ObservedStable", result["stable"])
                self.assertNotIn("SECRET", json.dumps(result))
                self.assertLessEqual(run.call_args.kwargs["timeout"], 2)
                self.assertEqual(["osx-arm64", "900", "100001", "42"], run.call_args.args[0][-4:])

    def test_unowned_origin_or_no_remaining_budget_cannot_start_helper(self):
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / "Bakabase"
            executable.touch()
            app = {"role": "unified", "rid": "osx-arm64", "exe": executable}
            for diagnostic, timeout in (({**DIAGNOSTIC, "origin": "other"}, 2), (DIAGNOSTIC, 0), (DIAGNOSTIC, 3)):
                with patch.object(pid, "hosted"), patch.object(pid.subprocess, "run") as run:
                    result = pid.capture(app, diagnostic, timeout)
                run.assert_not_called()
                self.assertFalse(result["stable"])


class ProbeDiagnostics(unittest.TestCase):
    def test_only_safe_schema_survives_and_pointer_results_do_not_establish_completeness(self):
        safe = probe.pid_diagnostic({**DIAGNOSTIC, "raw": "SECRET", "status": "SECRET", "parentMatches": True,
                                     "windowMatches": "SECRET", "parentAXError": -25204, "windowAXError": True,
                                     "parentPath": [0, "SECRET"]})
        self.assertEqual("diagnostic-unavailable", safe["status"])
        self.assertIsNone(safe["windowMatches"])
        self.assertIsNone(safe["windowAXError"])
        self.assertIsNone(safe["parentPath"])
        self.assertNotIn("SECRET", json.dumps(safe))

    def test_capture_keeps_failure_and_collects_first_pid_only_within_original_read_deadline(self):
        snapshot = {"backend": "macos-direct-ax", "readOnly": True, "enabled": True, "truncated": True,
                    "windows": [], "process": {"pid": 42}, "errorStage": "read-tree", "pidMismatch": DIAGNOSTIC,
                    "diagnostic": {"code": "DirectAXProcessMismatch", "operation": "read-pid", "axError": 0}}
        app = {"rid": "osx-arm64", "role": "unified", "exe": Path("/fixture/Bakabase")}
        with tempfile.TemporaryDirectory() as directory, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", return_value=snapshot), \
                patch.object(probe, "collect_pid_identity", return_value={"stable": True, "ownershipEstablished": False}) as collect, \
                patch.object(probe.time, "sleep"), patch.object(probe.time, "monotonic", return_value=100):
            result = probe.capture(app, 42, Path(directory)/"results", require_complete=True)
            self.assertFalse(result["capabilityPassed"])
            self.assertFalse(result["completeTreePassed"])
            self.assertFalse(result["mainFlowPassed"])
            self.assertEqual(10, len(result["attempts"]))
            self.assertEqual(DIAGNOSTIC["actualPid"], json.loads((Path(directory)/"results/tree.json").read_text())["pidMismatch"]["actualPid"])
        collect.assert_called_once()
        self.assertEqual(2, collect.call_args.args[2])

    def test_exhausted_read_budget_passes_zero_without_extending_readiness_deadline(self):
        snapshot = {"backend": "macos-direct-ax", "readOnly": True, "enabled": True, "truncated": True,
                    "windows": [], "process": {"pid": 42}, "errorStage": "read-tree", "pidMismatch": DIAGNOSTIC,
                    "diagnostic": {"code": "DirectAXProcessMismatch", "operation": "read-pid", "axError": 0}}
        clock = [100]
        def read(*args, **kwargs):
            clock[0] += 30
            return snapshot
        with tempfile.TemporaryDirectory() as directory, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", side_effect=read), \
                patch.object(probe, "collect_pid_identity", return_value={"code": "DiagnosticBudgetExhausted"}) as collect, \
                patch.object(probe.time, "sleep"), patch.object(probe.time, "monotonic", side_effect=lambda: clock[0]):
            result = probe.capture({"rid": "osx-arm64", "role": "unified"}, 42, Path(directory)/"results", require_complete=True)
        collect.assert_called_once()
        self.assertEqual(0, collect.call_args.args[2])
        self.assertEqual("NativeUiReadinessTimedOut", result["error"]["code"])


if __name__ == "__main__":
    unittest.main()
