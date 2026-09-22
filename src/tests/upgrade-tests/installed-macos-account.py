#!/usr/bin/env python3
"""Verify one owned disposable CI account password without exposing credentials.

The caller supplies bounded JSON on stdin and enforces its preparation deadline
on this separate process. This never creates an account, changes a password, or
grants Authorization Services rights. It must never authenticate a local user.
"""
import ctypes
import json
import os
from pathlib import Path
import platform
import re
import stat
import sys
from types import SimpleNamespace

try:
    import grp
    import pwd
except ImportError:  # Pure tests also run on Windows.
    grp = SimpleNamespace(getgrnam=None)
    pwd = SimpleNamespace(getpwnam=None, getpwuid=None)

ERROR_CODES = {"HostedRunnerRequired", "NativeMacRequired", "InvalidInput", "InvalidIdentity",
               "AccountIdentityChanged", "AccountShellInvalid", "AccountHomeChanged", "AdministratorRequired", "NativeAllocationFailed",
               "LocalNodeUnavailable", "LocalRecordUnavailable", "PasswordVerificationFailed", "InputTooLarge",
               "VerificationUnavailable"}
PHASES = {"initial", "account-lookup", "home", "admin", "load-frameworks", "local-node", "local-record", "verify-password"}
EXCEPTION_KINDS = {"AccountFailure", "KeyError", "FileNotFoundError", "PermissionError", "NotADirectoryError",
                   "OSError", "ValueError", "TypeError", "AttributeError", "ImportError", "RuntimeError",
                   "JSONDecodeError", "OverflowError", "MemoryError", "Other"}


class VerificationTrace:
    def __init__(self):
        self.phase = "initial"

    def mark(self, phase):
        require(phase in PHASES, "VerificationUnavailable")
        self.phase = phase


class AccountFailure(Exception):
    def __init__(self, code, native_code=None):
        super().__init__(code)
        self.code = code
        self.native_code = native_code


def require(value, code):
    if not value:
        raise AccountFailure(code)


def require_hosted():
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and
            bool(os.environ.get("RUNNER_TEMP")), "HostedRunnerRequired")
    require(platform.system() == "Darwin" and platform.machine() in {"arm64", "x86_64"},
            "NativeMacRequired")


def owned_account(payload, trace=None):
    trace = VerificationTrace() if trace is None else trace
    require(type(payload) is dict and set(payload) == {"username", "uid", "secret"}, "InvalidInput")
    username, uid, secret = payload["username"], payload["uid"], payload["secret"]
    require(type(username) is str and re.fullmatch(r"bbci_[0-9a-f]{12}", username), "InvalidIdentity")
    require(type(uid) is int and 10000 <= uid < 50000, "InvalidIdentity")
    require(type(secret) is str and re.fullmatch(r"[A-Za-z0-9_-]{43}", secret), "InvalidInput")
    home = Path("/Users") / username
    trace.mark("account-lookup")
    by_name, by_uid = pwd.getpwnam(username), pwd.getpwuid(uid)
    require(all(entry.pw_name == username and entry.pw_uid == uid and entry.pw_gid == 20 and
                Path(entry.pw_dir) == home for entry in (by_name, by_uid)), "AccountIdentityChanged")
    # Password verification alone does not cover PAM account checks. Apple's
    # pam_modules/Common.c od_record_check_shell rejects /usr/bin/false, so the
    # fixture must retain the ordinary shell selected at account creation.
    require(all(getattr(entry, "pw_shell", None) == "/bin/zsh" for entry in (by_name, by_uid)), "AccountShellInvalid")
    trace.mark("home")
    try:
        info = home.lstat()
    except FileNotFoundError:
        # The fixture owns the directory-service home attribute. sysadminctl
        # need not create that directory, and password verification does not
        # require one. Never follow a present symlink or accept an alien owner.
        info = None
    if info is not None:
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == uid, "AccountHomeChanged")
    trace.mark("admin")
    admin = grp.getgrnam("admin")
    require(admin.gr_gid == 80 and admin.gr_gid in os.getgrouplist(username, by_name.pw_gid),
            "AdministratorRequired")
    return username, secret


class NativeDirectory:
    """Small typed C API surface; no CF error descriptions or record enumeration."""
    def __init__(self, trace=None):
        self.trace = VerificationTrace() if trace is None else trace
        self.trace.mark("load-frameworks")
        self.cf = ctypes.CDLL("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")
        self.od = ctypes.CDLL("/System/Library/Frameworks/OpenDirectory.framework/OpenDirectory")
        pointer = ctypes.c_void_p
        error_pointer = ctypes.POINTER(pointer)
        signatures = ((self.cf, "CFStringCreateWithCString", [pointer, ctypes.c_char_p, ctypes.c_uint32], pointer),
                      (self.cf, "CFRelease", [pointer], None),
                      (self.cf, "CFErrorGetCode", [pointer], ctypes.c_long),
                      (self.od, "ODNodeCreateWithName", [pointer, pointer, pointer, error_pointer], pointer),
                      (self.od, "ODNodeCopyRecord", [pointer, pointer, pointer, pointer, error_pointer], pointer),
                      (self.od, "ODRecordVerifyPassword", [pointer, pointer, error_pointer], ctypes.c_bool))
        for library, name, arguments, result in signatures:
            function = getattr(library, name)
            function.argtypes, function.restype = arguments, result
        self.session = pointer.in_dll(self.od, "kODSessionDefault").value
        self.users = pointer.in_dll(self.od, "kODRecordTypeUsers").value

    def verify(self, username, secret):
        owned = []

        def string(value):
            result = self.cf.CFStringCreateWithCString(None, value.encode("utf-8"), 0x08000100)
            require(result, "NativeAllocationFailed")
            owned.append(result)
            return result

        def call(function, arguments, code, returns_object=False):
            error = ctypes.c_void_p()
            result = function(*arguments, ctypes.byref(error))
            if result and returns_object:
                owned.append(result)
            if error.value:
                owned.append(error.value)
                number = self.cf.CFErrorGetCode(error.value)
                number = number if type(number) is int and -(2 ** 31) <= number < 2 ** 31 else None
                raise AccountFailure(code, number)
            require(result, code)
            return result

        try:
            self.trace.mark("local-node")
            node_name, record_name, password = (string(value) for value in ("/Local/Default", username, secret))
            node = call(self.od.ODNodeCreateWithName, (None, self.session, node_name), "LocalNodeUnavailable", True)
            self.trace.mark("local-record")
            record = call(self.od.ODNodeCopyRecord, (node, self.users, record_name, None), "LocalRecordUnavailable", True)
            # This single verification checks a password, not administrator UI
            # acceptance. Do not retry, change credentials, or acquire rights.
            self.trace.mark("verify-password")
            call(self.od.ODRecordVerifyPassword, (record, password), "PasswordVerificationFailed")
        finally:
            for value in reversed(owned):
                self.cf.CFRelease(value)


def main(input_stream=None, output_stream=None):
    input_stream = sys.stdin if input_stream is None else input_stream
    output_stream = sys.stdout if output_stream is None else output_stream
    trace = VerificationTrace()
    try:
        # Reject before reading a credential, inspecting an account, or loading
        # frameworks. A parent process timeout bounds native OD RPC duration.
        require_hosted()
        raw = input_stream.read(2049)
        require(len(raw) <= 2048, "InputTooLarge")
        payload = json.loads(raw)
        username, secret = owned_account(payload, trace)
        trace.mark("load-frameworks")
        NativeDirectory(trace).verify(username, secret)
        result = {"verified": True}
    except AccountFailure as error:
        result = {"verified": False, "error": error.code if type(error.code) is str and error.code in ERROR_CODES else "VerificationUnavailable",
                  "phase": trace.phase, "exceptionKind": "AccountFailure"}
        if type(error.native_code) is int and -(2 ** 31) <= error.native_code < 2 ** 31:
            result["nativeErrorCode"] = error.native_code
    except Exception as error:
        # Exceptions may contain input or native record details. Never serialize
        # their messages, repr, traceback, CFError descriptions, or stderr.
        kind = type(error).__name__
        result = {"verified": False, "error": "VerificationUnavailable", "phase": trace.phase,
                  "exceptionKind": kind if kind in EXCEPTION_KINDS else "Other"}
    if result.get("verified") is False and (type(result.get("phase")) is not str or result["phase"] not in PHASES):
        result["phase"] = "initial"
    output_stream.write(json.dumps(result) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
