#!/usr/bin/env python3
"""Pure byte-stream fixtures; never read system logs or invoke native auth."""
import datetime as dt
import importlib.util
import json
from pathlib import Path
import subprocess
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location("macos_auth_diagnostics", Path(__file__).with_name("installed-macos-auth-diagnostics.py"))
helper = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(helper)

SECRET = "s" * 43
NOW = 1790040000
ENVIRONMENT = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "/tmp/owned-ci"}


class Child:
    def __init__(self, exit_code=0, wait_timeout=False):
        self.stdout = SimpleNamespace(fileno=lambda: 77, close=Mock())
        self.returncode, self.exit_code = None, exit_code
        self.kill = Mock(side_effect=self.killed)
        self.wait = Mock(side_effect=self.waited)
        self.wait_timeout = wait_timeout

    def poll(self):
        return self.returncode

    def killed(self):
        self.returncode = -9

    def waited(self, timeout):
        if self.wait_timeout and self.returncode is None:
            raise subprocess.TimeoutExpired("private " + SECRET, timeout, stderr=SECRET)
        if self.returncode is None:
            self.returncode = self.exit_code
        return self.returncode


class AuthDiagnosticsTests(unittest.TestCase):
    def record(self):
        return {"pid": 321, "started": "Tue Sep 22 03:30:00 2026", "path": sorted(helper.SECURITY_AGENTS)[0],
                "firstObservedEpoch": NOW - 120.2}

    def row(self, **changes):
        # Timestamp shape comes from the already collected owned-process log.
        result = {"processID": 321, "processImagePath": self.record()["path"], "eventType": "logEvent",
                  "timestamp": dt.datetime.fromtimestamp(NOW - 10, dt.timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f%z"),
                  "messageType": "Default", "subsystem": "com.apple.SecurityAgent", "category": "auth",
                  "eventMessage": "password verification result", "privateExtra": SECRET}
        return {**result, **changes}

    def captured(self, data, **kwargs):
        with patch.object(helper, "require_hosted"), patch.object(helper.time, "time", return_value=NOW), \
                patch.object(helper, "read_log", return_value=data):
            return helper.capture(kwargs.get("record", self.record()), kwargs.get("started", NOW - 200), SECRET)

    def test_non_hosted_rejects_before_input_inspection_or_process_start(self):
        with patch.dict(helper.os.environ, {}, clear=True), patch.object(helper.subprocess, "Popen") as popen:
            result = helper.capture(None, None, None)
        self.assertEqual("HostedRunnerRequired", result["error"]["code"])
        self.assertFalse(result["collected"])
        popen.assert_not_called()

    def test_native_guard_rejects_windows_linux_and_unknown_mac_architecture(self):
        for system, machine in (("Windows", "AMD64"), ("Linux", "x86_64"), ("Darwin", "unknown")):
            with self.subTest(system=system), patch.dict(helper.os.environ, ENVIRONMENT, clear=True), \
                    patch.object(helper.platform, "system", return_value=system), patch.object(helper.platform, "machine", return_value=machine), \
                    patch.object(helper.subprocess, "Popen") as popen:
                result = helper.capture(self.record(), NOW - 200, SECRET)
                self.assertEqual("NativeMacRequired", result["error"]["code"])
                popen.assert_not_called()
        for machine in ("arm64", "x86_64"):
            with patch.dict(helper.os.environ, ENVIRONMENT, clear=True), patch.object(helper.platform, "system", return_value="Darwin"), \
                    patch.object(helper.platform, "machine", return_value=machine):
                helper.require_hosted()

    def test_invalid_identity_timing_or_secret_never_starts_a_log_command(self):
        cases = [(dict(self.record(), **change), NOW - 200, SECRET) for change in
                 ({"pid": True}, {"pid": 1}, {"pid": "321"}, {"path": "/unowned/SecurityAgent"}, {"started": SECRET},
                  {"firstObservedEpoch": None}, {"firstObservedEpoch": NOW + 1}, {"firstObservedEpoch": float("nan")})]
        cases += [(self.record(), NOW + 1, SECRET), (self.record(), float("inf"), SECRET),
                  (self.record(), NOW - 200, None), (self.record(), NOW - 200, "")]
        for record, started, secret in cases:
            with self.subTest(record=record, started=started), patch.object(helper, "require_hosted"), \
                    patch.object(helper.time, "time", return_value=NOW), patch.object(helper.subprocess, "Popen") as popen:
                result = helper.capture(record, started, secret)
                self.assertFalse(result["collected"])
                self.assertNotIn(SECRET, json.dumps(result))
                popen.assert_not_called()

    def test_query_is_exact_pid_and_securityagent_with_fixed_message_filter_and_inward_time_window(self):
        with patch.object(helper, "require_hosted"), patch.object(helper.time, "time", return_value=NOW + 0.9), \
                patch.object(helper, "read_log", return_value=b"[]") as read:
            result = helper.capture(self.record(), NOW - 200, SECRET)
        arguments = read.call_args.args[0]
        self.assertEqual(["/usr/bin/log", "show", "--style", "json", "--info", "--debug"], arguments[:6])
        predicate = arguments[arguments.index("--predicate") + 1]
        self.assertTrue(predicate.startswith('(processID == 321) AND process == "SecurityAgent" AND ('))
        for term in helper.TERMS:
            self.assertIn('eventMessage CONTAINS[c] "' + term + '"', predicate)
        self.assertNotIn(SECRET, " ".join(arguments))
        self.assertNotIn(self.record()["path"], json.dumps(result))
        self.assertEqual({"startEpoch": NOW - 120, "endEpoch": NOW}, result["window"])
        self.assertTrue(result["collected"])
        self.assertNotIn("passed", result)
        self.assertIn("not authentication acceptance", result["scope"])
        self.assertEqual((NOW - 180, NOW), helper.validate(dict(self.record(), firstObservedEpoch=NOW - 1000), NOW - 1500, SECRET, NOW))

    def test_each_entry_requires_correct_pid_exact_system_path_logevent_keyword_and_zoned_time(self):
        invalid = [{"processID": 123}, {"processID": "321"}, {"processImagePath": "/foreign/SecurityAgent"},
                   {"processImagePath": self.record()["path"] + "Other"}, {"eventType": "activityCreateEvent"},
                   {"eventMessage": "XPC connection established"}, {"timestamp": "invalid " + SECRET},
                   {"timestamp": "2026-09-22 04:00:00"},
                   {"timestamp": dt.datetime.fromtimestamp(NOW - 200, dt.timezone.utc).isoformat()},
                   {"timestamp": dt.datetime.fromtimestamp(NOW + 1, dt.timezone.utc).isoformat()}]
        rows = [self.row(), *[self.row(**change) for change in invalid], "unrelated " + SECRET]
        result = self.captured(json.dumps(rows).encode())
        self.assertEqual(1, len(result["entries"]))
        self.assertEqual(len(invalid) + 1, result["rejectedEntries"])
        self.assertEqual({"timestamp", "type", "subsystem", "category", "eventMessage"}, set(result["entries"][0]))
        self.assertNotIn(SECRET, json.dumps(result))

    def test_every_retained_string_is_redacted_before_truncation_and_entries_are_bounded(self):
        row = self.row(messageType="x" * 150 + SECRET, subsystem="y" * 150 + SECRET, category="z" * 150 + SECRET,
                       eventMessage="auth " + "m" * 485 + SECRET + " tail")
        result = self.captured(json.dumps([row] * 81).encode())
        self.assertEqual(80, len(result["entries"]))
        self.assertTrue(result["entriesTruncated"])
        self.assertNotIn(SECRET, json.dumps(result))
        for key, prefix in (("type", "x" * 150), ("subsystem", "y" * 150), ("category", "z" * 150)):
            self.assertEqual(prefix + "[redacted]", result["entries"][0][key])
        self.assertEqual(("auth " + "m" * 485 + "[redacted] tail")[:500], result["entries"][0]["eventMessage"])

    def test_malformed_json_and_arbitrary_exception_messages_are_never_retained(self):
        for data in (SECRET.encode(), b"[{", b"{}", b"\xff"):
            result = self.captured(data)
            self.assertEqual("InvalidLogJson", result["error"]["code"])
            self.assertNotIn(SECRET, json.dumps(result))
        with patch.object(helper, "require_hosted"), patch.object(helper.time, "time", return_value=NOW), \
                patch.object(helper, "read_log", side_effect=OSError(SECRET)):
            result = helper.capture(self.record(), NOW - 200, SECRET)
        self.assertEqual({"code": "DiagnosticUnavailable", "type": "OSError"}, result["error"])
        self.assertNotIn(SECRET, json.dumps(result))

    def test_unknown_or_non_string_failure_codes_cannot_enter_report(self):
        for code in (SECRET, "UnexpectedFutureCode", {"private": SECRET}):
            with self.subTest(code=type(code).__name__), patch.object(helper, "require_hosted"), \
                    patch.object(helper.time, "time", return_value=NOW), \
                    patch.object(helper, "read_log", side_effect=helper.DiagnosticFailure(code)):
                result = helper.capture(self.record(), NOW - 200, SECRET)
            self.assertEqual({"code": "DiagnosticUnavailable", "type": "DiagnosticFailure"}, result["error"])
            self.assertNotIn(SECRET, json.dumps(result))

    def read_fixture(self, content, child=None, stalled=False):
        child = child or Child()
        selector = Mock()
        clock, pending, read_sizes = [0.0], bytearray(content), []
        def select(timeout):
            if stalled:
                clock[0] += min(1, timeout)
                return []
            return [True]
        selector.select.side_effect = select
        def read(fd, amount):
            self.assertEqual(77, fd)
            read_sizes.append(amount)
            value = bytes(pending[:amount])
            del pending[:amount]
            return value
        with patch.object(helper.subprocess, "Popen", return_value=child) as popen, \
                patch.object(helper.os, "set_blocking", create=True), patch.object(helper.os, "read", side_effect=read), \
                patch.object(helper.selectors, "DefaultSelector", return_value=selector), \
                patch.object(helper.time, "monotonic", side_effect=lambda: clock[0]):
            try:
                value = helper.read_log(["/usr/bin/log", "show"], 5)
                failure = None
            except helper.DiagnosticFailure as error:
                value, failure = None, error.code
        self.assertTrue(child.stdout.close.called)
        selector.close.assert_called_once_with()
        self.assertEqual(subprocess.PIPE, popen.call_args.kwargs["stdout"])
        self.assertEqual(subprocess.DEVNULL, popen.call_args.kwargs["stderr"])
        return value, failure, child, read_sizes, clock[0]

    def test_popen_pipe_complete_json_reaps_child_without_killing(self):
        value, failure, child, _, _ = self.read_fixture(b"[]")
        self.assertEqual(b"[]", value)
        self.assertIsNone(failure)
        child.kill.assert_not_called()

    def test_output_limit_kills_only_owned_log_child_and_never_reads_beyond_cap(self):
        value, failure, child, sizes, _ = self.read_fixture(b"x" * (helper.MAX_BYTES + 100))
        self.assertIsNone(value)
        self.assertEqual("OutputBudgetExceeded", failure)
        self.assertEqual(helper.MAX_BYTES, sum(sizes))
        child.kill.assert_called_once_with()
        self.assertEqual(-9, child.returncode)

    def test_stalled_pipe_obeys_shared_five_second_budget_and_kills_owned_child(self):
        _, failure, child, _, elapsed = self.read_fixture(b"", stalled=True)
        self.assertEqual("DiagnosticTimedOut", failure)
        self.assertLessEqual(elapsed, 5)
        self.assertTrue(all(call.kwargs["timeout"] <= 5 for call in child.wait.call_args_list))
        child.kill.assert_called_once_with()

    def test_nonzero_exit_and_wait_timeout_never_return_success(self):
        for child, expected in ((Child(exit_code=1), "LogCommandFailed"), (Child(wait_timeout=True), "DiagnosticTimedOut")):
            with self.subTest(expected=expected):
                value, failure, child, _, _ = self.read_fixture(b"[]", child)
                self.assertIsNone(value)
                self.assertEqual(expected, failure)
                if expected == "DiagnosticTimedOut":
                    child.kill.assert_called_once_with()

    def test_read_and_selector_cleanup_errors_still_terminate_only_the_owned_log_child(self):
        child, selector = Child(), Mock()
        selector.select.return_value = [True]
        selector.close.side_effect = OSError(SECRET)
        with patch.object(helper.subprocess, "Popen", return_value=child), \
                patch.object(helper.os, "set_blocking", create=True), patch.object(helper.os, "read", side_effect=ValueError(SECRET)), \
                patch.object(helper.selectors, "DefaultSelector", return_value=selector), \
                patch.object(helper.time, "monotonic", return_value=0):
            with self.assertRaises(OSError):
                helper.read_log(["/usr/bin/log", "show"], 5)
        child.kill.assert_called_once_with()
        self.assertEqual(-9, child.returncode)
        child.stdout.close.assert_called_once_with()


if __name__ == "__main__":
    unittest.main()
