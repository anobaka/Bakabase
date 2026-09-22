#!/usr/bin/env python3
"""Pure account verification guards; no native account authentication or UI."""
import importlib.util
import io
import json
from pathlib import Path
import stat
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location("installed_macos_account", Path(__file__).with_name("installed-macos-account.py"))
helper = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(helper)

USERNAME = "bbci_123456abcdef"
SECRET = "s" * 43
UID = 12001
ENVIRONMENT = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "/tmp/owned-ci"}


class NativeFixture:
    def __init__(self, failed=None, error=None, object_and_error=False):
        self.calls, self.strings, self.released = [], {}, []
        self.failed, self.error, self.object_and_error = failed, error, object_and_error
        self.helper = helper.NativeDirectory.__new__(helper.NativeDirectory)
        self.helper.trace = helper.VerificationTrace()
        self.helper.session, self.helper.users = 901, 902
        self.helper.cf = SimpleNamespace(CFStringCreateWithCString=self.string, CFRelease=self.released.append,
                                        CFErrorGetCode=lambda value: -14090)
        self.helper.od = SimpleNamespace(ODNodeCreateWithName=self.node, ODNodeCopyRecord=self.record,
                                        ODRecordVerifyPassword=self.password)

    def string(self, allocator, value, encoding):
        assert allocator is None and encoding == 0x08000100
        identifier = 100 + len(self.strings)
        self.strings[identifier] = value.decode()
        return identifier

    def result(self, stage, value, output_error):
        if self.failed == stage:
            if self.error:
                output_error._obj.value = self.error
            return value if self.object_and_error else 0
        return value

    def node(self, allocator, session, name, output_error):
        self.calls.append(("node", allocator, session, self.strings[name]))
        return self.result("node", 200, output_error)

    def record(self, node, record_type, name, attributes, output_error):
        self.calls.append(("record", node, record_type, self.strings[name], attributes))
        return self.result("record", 201, output_error)

    def password(self, record, password, output_error):
        self.calls.append(("verify", record, self.strings[password]))
        return self.result("verify", True, output_error)


class AccountVerificationTests(unittest.TestCase):
    def failure(self, code, kind="AccountFailure", phase="initial", native_code=None):
        result = {"verified": False, "error": code, "phase": phase, "exceptionKind": kind}
        if native_code is not None:
            result["nativeErrorCode"] = native_code
        return result

    def payload(self):
        return {"username": USERNAME, "uid": UID, "secret": SECRET}

    def account(self):
        return SimpleNamespace(pw_name=USERNAME, pw_uid=UID, pw_gid=20, pw_dir="/Users/" + USERNAME, pw_shell="/bin/zsh")

    def run_main(self, stream=None):
        output = io.StringIO()
        status = helper.main(io.StringIO(json.dumps(self.payload())) if stream is None else stream, output)
        self.assertEqual(0, status)
        self.assertNotIn(SECRET, output.getvalue())
        return json.loads(output.getvalue())

    def test_non_hosted_rejects_before_input_account_or_native_access(self):
        stream = Mock()
        with patch.dict(helper.os.environ, {}, clear=True), patch.object(helper, "owned_account") as account, \
                patch.object(helper.ctypes, "CDLL") as native:
            self.assertEqual(self.failure("HostedRunnerRequired"), self.run_main(stream))
        stream.read.assert_not_called()
        account.assert_not_called()
        native.assert_not_called()

    def test_non_mac_and_unknown_mac_architecture_reject_before_input(self):
        for system, machine in (("Windows", "AMD64"), ("Linux", "x86_64"), ("Darwin", "unknown")):
            with self.subTest(system=system, machine=machine), patch.dict(helper.os.environ, ENVIRONMENT, clear=True), \
                    patch.object(helper.platform, "system", return_value=system), \
                    patch.object(helper.platform, "machine", return_value=machine), \
                    patch.object(helper, "owned_account") as account, patch.object(helper.ctypes, "CDLL") as native:
                stream = Mock()
                self.assertEqual(self.failure("NativeMacRequired"), self.run_main(stream))
                stream.read.assert_not_called()
                account.assert_not_called()
                native.assert_not_called()

    def test_both_supported_hosted_mac_architectures_accept_guard(self):
        for machine in ("arm64", "x86_64"):
            with patch.dict(helper.os.environ, ENVIRONMENT, clear=True), \
                    patch.object(helper.platform, "system", return_value="Darwin"), \
                    patch.object(helper.platform, "machine", return_value=machine):
                helper.require_hosted()

    def test_stdin_is_bounded_and_failures_never_echo_input(self):
        for content in (SECRET, "[" + SECRET, "x" * 2049 + SECRET):
            with self.subTest(length=len(content)), patch.object(helper, "require_hosted"), \
                    patch.object(helper, "NativeDirectory") as native:
                result = self.run_main(io.StringIO(content))
                self.assertFalse(result["verified"])
                native.assert_not_called()

    def test_input_rejects_foreign_identity_and_invalid_secret_before_account_lookup(self):
        mutations = ({"username": "runner"}, {"username": "bbci_123456abcdef/other"}, {"uid": True},
                     {"uid": 501}, {"uid": 50000}, {"secret": "short"}, {"secret": SECRET + "\n"},
                     {"home": "/Users/runner"})
        for change in mutations:
            with self.subTest(change=list(change)), patch.object(helper.pwd, "getpwnam") as account:
                with self.assertRaises(helper.AccountFailure):
                    helper.owned_account({**self.payload(), **change})
                account.assert_not_called()

    def test_exact_owned_identity_home_and_admin_are_verified(self):
        home_stat = SimpleNamespace(st_mode=stat.S_IFDIR | 0o700, st_uid=UID)
        with patch.object(helper.pwd, "getpwnam", return_value=self.account()) as by_name, \
                patch.object(helper.pwd, "getpwuid", return_value=self.account()) as by_uid, \
                patch.object(helper.Path, "lstat", return_value=home_stat) as home, \
                patch.object(helper.grp, "getgrnam", return_value=SimpleNamespace(gr_gid=80)) as group, \
                patch.object(helper.os, "getgrouplist", return_value=[20, 80], create=True) as memberships:
            self.assertEqual((USERNAME, SECRET), helper.owned_account(self.payload()))
            by_name.assert_called_once_with(USERNAME)
            by_uid.assert_called_once_with(UID)
            home.assert_called_once()
            group.assert_called_once_with("admin")
            memberships.assert_called_once_with(USERNAME, 20)

    def test_changed_account_alias_home_owner_symlink_or_membership_fail(self):
        for change in ("name", "uid", "gid", "home", "uid-record", "home-owner", "symlink", "membership", "admin-gid"):
            with self.subTest(change=change):
                by_name, by_uid = self.account(), self.account()
                home_stat = SimpleNamespace(st_mode=stat.S_IFDIR | 0o700, st_uid=UID)
                if change == "name": by_name.pw_name = "runner"
                if change == "uid": by_name.pw_uid = 501
                if change == "gid": by_name.pw_gid = 80
                if change == "home": by_name.pw_dir = "/Users/runner"
                if change == "uid-record": by_uid.pw_name = "runner"
                if change == "home-owner": home_stat.st_uid = 501
                if change == "symlink": home_stat.st_mode = stat.S_IFLNK | 0o777
                with patch.object(helper.pwd, "getpwnam", return_value=by_name), \
                        patch.object(helper.pwd, "getpwuid", return_value=by_uid), \
                        patch.object(helper.Path, "lstat", return_value=home_stat), \
                        patch.object(helper.grp, "getgrnam", return_value=SimpleNamespace(gr_gid=81 if change == "admin-gid" else 80)), \
                        patch.object(helper.os, "getgrouplist", return_value=[20] if change == "membership" else [20, 80], create=True):
                    with self.assertRaises(helper.AccountFailure):
                        helper.owned_account(self.payload())

    def test_disabled_or_changed_shell_rejects_before_native_password_verification(self):
        for shell in ("/usr/bin/false", "/bin/false", "/usr/sbin/nologin", "/bin/bash", SECRET, None):
            for changed_record in ("name", "uid"):
                with self.subTest(shell=shell if shell != SECRET else "unknown", record=changed_record):
                    by_name, by_uid = self.account(), self.account()
                    entry = by_name if changed_record == "name" else by_uid
                    if shell is None:
                        del entry.pw_shell
                    else:
                        entry.pw_shell = shell
                    with patch.object(helper, "require_hosted"), \
                            patch.object(helper.pwd, "getpwnam", return_value=by_name), \
                            patch.object(helper.pwd, "getpwuid", return_value=by_uid), \
                            patch.object(helper.Path, "lstat") as home, patch.object(helper, "NativeDirectory") as native:
                        self.assertEqual(self.failure("AccountShellInvalid", phase="account-lookup"), self.run_main())
                        home.assert_not_called()
                        native.assert_not_called()

    def test_absent_home_is_not_required_for_password_verification(self):
        with patch.object(helper.pwd, "getpwnam", return_value=self.account()), \
                patch.object(helper.pwd, "getpwuid", return_value=self.account()), \
                patch.object(helper.Path, "lstat", side_effect=FileNotFoundError(SECRET)), \
                patch.object(helper.grp, "getgrnam", return_value=SimpleNamespace(gr_gid=80)), \
                patch.object(helper.os, "getgrouplist", return_value=[20, 80], create=True):
            self.assertEqual((USERNAME, SECRET), helper.owned_account(self.payload()))

    def test_home_lookup_permission_and_other_errors_remain_failures(self):
        for error in (PermissionError(SECRET), NotADirectoryError(SECRET), OSError(SECRET)):
            with patch.object(helper, "require_hosted"), \
                    patch.object(helper.pwd, "getpwnam", return_value=self.account()), \
                    patch.object(helper.pwd, "getpwuid", return_value=self.account()), \
                    patch.object(helper.Path, "lstat", side_effect=error), patch.object(helper, "NativeDirectory") as native:
                self.assertEqual(self.failure("VerificationUnavailable", type(error).__name__, "home"), self.run_main())
                native.assert_not_called()

    def test_native_uses_exact_local_record_and_verifies_once_releasing_owned_objects(self):
        fixture = NativeFixture()
        fixture.helper.verify(USERNAME, SECRET)
        self.assertEqual([("node", None, 901, "/Local/Default"),
                          ("record", 200, 902, USERNAME, None), ("verify", 201, SECRET)], fixture.calls)
        self.assertEqual([201, 200, 102, 101, 100], fixture.released)
        self.assertNotIn(901, fixture.released)  # Borrowed default session and Users constant.
        self.assertNotIn(902, fixture.released)

    def test_native_errors_are_numeric_only_and_all_created_objects_are_released(self):
        expected = {"node": ([777, 102, 101, 100], "LocalNodeUnavailable"),
                    "record": ([777, 200, 102, 101, 100], "LocalRecordUnavailable"),
                    "verify": ([777, 201, 200, 102, 101, 100], "PasswordVerificationFailed")}
        for stage, (released, code) in expected.items():
            with self.subTest(stage=stage):
                fixture = NativeFixture(stage, 777)
                with self.assertRaises(helper.AccountFailure) as raised:
                    fixture.helper.verify(USERNAME, SECRET)
                self.assertEqual(code, raised.exception.code)
                self.assertEqual(-14090, raised.exception.native_code)
                self.assertNotIn(SECRET, str(raised.exception))
                self.assertEqual(released, fixture.released)
                self.assertEqual({"node": "local-node", "record": "local-record", "verify": "verify-password"}[stage],
                                 fixture.helper.trace.phase)
                self.assertLessEqual(sum(call[0] == "verify" for call in fixture.calls), 1)

    def test_false_password_result_without_cf_error_is_still_failure_and_not_retried(self):
        fixture = NativeFixture("verify")
        with self.assertRaisesRegex(helper.AccountFailure, "PasswordVerificationFailed"):
            fixture.helper.verify(USERNAME, SECRET)
        self.assertEqual(1, sum(call[0] == "verify" for call in fixture.calls))
        self.assertEqual([201, 200, 102, 101, 100], fixture.released)

    def test_native_partial_object_and_error_are_both_released(self):
        fixture = NativeFixture("record", 777, object_and_error=True)
        with self.assertRaises(helper.AccountFailure):
            fixture.helper.verify(USERNAME, SECRET)
        self.assertEqual([777, 201, 200, 102, 101, 100], fixture.released)

    def test_unexpected_exception_cannot_leak_password_or_description(self):
        for error in (ValueError(SECRET), OSError(SECRET)):
            with patch.object(helper, "require_hosted"), patch.object(helper, "owned_account", return_value=(USERNAME, SECRET)), \
                    patch.object(helper, "NativeDirectory", side_effect=error):
                self.assertEqual(self.failure("VerificationUnavailable", type(error).__name__, "load-frameworks"), self.run_main())

    def test_exception_kind_and_phase_are_fixed_allowlists(self):
        foreign_error = type(SECRET, (Exception,), {})

        def reject(payload, trace):
            trace.phase = SECRET
            raise foreign_error(SECRET)

        with patch.object(helper, "require_hosted"), patch.object(helper, "owned_account", side_effect=reject):
            self.assertEqual(self.failure("VerificationUnavailable", "Other"), self.run_main())

    def test_account_lookup_and_admin_errors_have_distinct_safe_phases(self):
        with patch.object(helper, "require_hosted"), patch.object(helper.pwd, "getpwnam", side_effect=KeyError(SECRET)):
            self.assertEqual(self.failure("VerificationUnavailable", "KeyError", "account-lookup"), self.run_main())
        with patch.object(helper, "require_hosted"), \
                patch.object(helper.pwd, "getpwnam", return_value=self.account()), \
                patch.object(helper.pwd, "getpwuid", return_value=self.account()), \
                patch.object(helper.Path, "lstat", side_effect=FileNotFoundError(SECRET)), \
                patch.object(helper.grp, "getgrnam", side_effect=KeyError(SECRET)):
            self.assertEqual(self.failure("VerificationUnavailable", "KeyError", "admin"), self.run_main())

    def test_main_authenticates_only_after_validation_and_prints_boolean_success(self):
        with patch.object(helper, "require_hosted"), patch.object(helper, "owned_account", return_value=(USERNAME, SECRET)) as account, \
                patch.object(helper, "NativeDirectory") as native:
            self.assertEqual({"verified": True}, self.run_main())
            self.assertEqual(self.payload(), account.call_args.args[0])
            self.assertIsInstance(account.call_args.args[1], helper.VerificationTrace)
            native.return_value.verify.assert_called_once_with(USERNAME, SECRET)

    def test_native_safe_failure_is_structured_without_traceback(self):
        with patch.object(helper, "require_hosted"), patch.object(helper, "owned_account", return_value=(USERNAME, SECRET)), \
                patch.object(helper, "NativeDirectory") as native:
            native.return_value.verify.side_effect = helper.AccountFailure("PasswordVerificationFailed", -14090)
            self.assertEqual(self.failure("PasswordVerificationFailed", phase="load-frameworks", native_code=-14090), self.run_main())

    def test_failure_output_rejects_unknown_text_and_out_of_range_native_numbers(self):
        for number in (SECRET, True, 2 ** 31, -(2 ** 31) - 1):
            with patch.object(helper, "require_hosted"), patch.object(helper, "owned_account", return_value=(USERNAME, SECRET)), \
                    patch.object(helper, "NativeDirectory") as native:
                native.return_value.verify.side_effect = helper.AccountFailure(SECRET, number)
                self.assertEqual(self.failure("VerificationUnavailable", phase="load-frameworks"), self.run_main())

    def test_cli_local_guard_never_authenticates_and_credentials_are_only_on_stdin(self):
        # Explicitly non-hosted even when this pure test runs on CI. No framework
        # or account is accessed; argv contains only Python and this script.
        arguments = [sys.executable, str(Path(helper.__file__))]
        self.assertNotIn(SECRET, " ".join(arguments))
        environment = dict(helper.os.environ, GITHUB_ACTIONS="false", PYTHONDONTWRITEBYTECODE="1")
        result = subprocess.run(arguments, input=json.dumps(self.payload()), text=True, capture_output=True,
                                env=environment, timeout=5)
        self.assertEqual(0, result.returncode)
        self.assertEqual(self.failure("HostedRunnerRequired"), json.loads(result.stdout))
        self.assertEqual("", result.stderr)
        self.assertNotIn(SECRET, result.stdout)


if __name__ == "__main__":
    unittest.main()
