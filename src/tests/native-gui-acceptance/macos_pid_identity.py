#!/usr/bin/env python3
"""Hosted-only single-PID libproc diagnostics; never establishes AX ownership."""
import ctypes
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_pid_package_base", HERE.parent / "upgrade-tests/run-package-acceptance.py")
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)
CODES = {"ObservedStable", "InvalidDiagnosticInput", "HostedMacRequired", "DiagnosticBudgetExhausted",
         "DiagnosticTimedOut", "DiagnosticUnavailable", "ProcessUnavailable", "ProcessIdentityChanged",
         "ProcessStartedAfterAXObservation"}


class IdentityFailure(AssertionError):
    pass


def require(condition, code):
    if not condition:
        raise IdentityFailure(code)


def integer(value, low=0, high=2**31-1):
    return type(value) is int and low <= value <= high


def hosted(rid):
    try:
        base.require_hosted_runner(os.environ, platform.system(), platform.machine(), rid)
        require(rid in ("osx-x64", "osx-arm64"), "HostedMacRequired")
    except AssertionError:
        raise IdentityFailure("HostedMacRequired") from None


class ProcBsdInfo(ctypes.Structure):
    # Public sys/proc_info.h PROC_PIDTBSDINFO, MAXCOMLEN=16. Unneeded comm/name
    # fields exist for ABI alignment only and are never decoded or returned.
    _fields_ = [(name, ctypes.c_uint32) for name in (
        "flags", "status", "xstatus", "pid", "ppid", "uid", "gid", "ruid", "rgid", "svuid", "svgid", "reserved")]
    _fields_ += [("comm", ctypes.c_char * 16), ("name", ctypes.c_char * 32)]
    _fields_ += [(name, ctypes.c_uint32) for name in ("nfiles", "pgid", "pjobc", "tdev", "tpgid")]
    _fields_ += [("nice", ctypes.c_int32), ("start_seconds", ctypes.c_uint64), ("start_microseconds", ctypes.c_uint64)]


def sample(pid):
    """Only called by the guarded child. No enumeration, argv, env or UI reads."""
    require(integer(pid, 1), "InvalidDiagnosticInput")
    require(ctypes.sizeof(ProcBsdInfo) == 136, "DiagnosticUnavailable")
    library = ctypes.CDLL("/usr/lib/libproc.dylib")
    library.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]
    library.proc_pidinfo.restype = ctypes.c_int
    library.proc_pidpath.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
    library.proc_pidpath.restype = ctypes.c_int
    record, path = ProcBsdInfo(), ctypes.create_string_buffer(4096)
    require(library.proc_pidinfo(pid, 3, 0, ctypes.byref(record), ctypes.sizeof(record)) == ctypes.sizeof(record),
            "ProcessUnavailable")
    require(record.pid == pid and library.proc_pidpath(pid, path, len(path)) > 0, "ProcessUnavailable")
    return {"pid": record.pid, "ppid": record.ppid, "uid": record.uid,
            "startSeconds": record.start_seconds, "startMicroseconds": record.start_microseconds,
            "executable": path.value.decode("utf-8", errors="strict")}


def identity(raw, pid):
    require(isinstance(raw, dict), "DiagnosticUnavailable")
    require(raw.get("pid") == pid and integer(raw.get("pid"), 1) and integer(raw.get("ppid")) and
            integer(raw.get("uid"), 0, 2**32-1) and integer(raw.get("startSeconds"), 1, 2**53-1) and
            integer(raw.get("startMicroseconds"), 0, 999999), "DiagnosticUnavailable")
    path = raw.get("executable")
    require(isinstance(path, str) and 1 < len(path) < 4096 and path.startswith("/") and
            not any(ord(char) < 32 or ord(char) == 127 for char in path), "DiagnosticUnavailable")
    return {key: raw[key] for key in ("pid", "ppid", "uid", "startSeconds", "startMicroseconds", "executable")}


def observe(rid, pid, observed_ms):
    hosted(rid)  # Precedes every native call, even on CLI use outside capture().
    require(integer(pid, 1) and integer(observed_ms, 0, 2**53-1), "InvalidDiagnosticInput")
    before = identity(sample(pid), pid)
    after = identity(sample(pid), pid)
    require(before == after, "ProcessIdentityChanged")
    # AX provides no start token. A process born after that observation cannot
    # be its target; an earlier stable process is still diagnostic, not proof.
    require(before["startSeconds"] * 1000000 + before["startMicroseconds"] <= observed_ms * 1000 + 999,
            "ProcessStartedAfterAXObservation")
    return before


def failure_code(error):
    code = str(error) if isinstance(error, IdentityFailure) else None
    return code if code in CODES else "DiagnosticUnavailable"


def capture(app, diagnostic, timeout):
    result = {"scope": "single-ax-mismatch-pid-os-identity-only", "readOnly": True,
              "ownershipEstablished": False, "code": "DiagnosticUnavailable", "stable": False,
              "identity": None, "observedEpochMs": None}
    began = time.monotonic()
    try:
        hosted(app["rid"])
        expected = {"unified": "Bakabase", "client": "Bakabase.Client"}.get(app.get("role"))
        require(expected is not None and Path(app["exe"]).is_absolute() and Path(app["exe"]).name == expected and
                Path(app["exe"]).is_file(), "InvalidDiagnosticInput")
        pid, expected_pid, observed_ms = (diagnostic.get(key) for key in ("actualPid", "expectedPid", "observedEpochMs"))
        require(diagnostic.get("origin") == "owned-child-edge" and integer(pid, 1) and integer(expected_pid, 1) and
                pid != expected_pid and integer(observed_ms, 0, 2**53-1), "InvalidDiagnosticInput")
        result.update(pid=pid, expectedPid=expected_pid, observedEpochMs=observed_ms)
        require(type(timeout) in (float, int) and math.isfinite(timeout) and 0 < timeout <= 2,
                "DiagnosticBudgetExhausted")
        remaining = timeout - (time.monotonic() - began)
        require(remaining > 0, "DiagnosticBudgetExhausted")
        child = subprocess.run([sys.executable, "-B", str(Path(__file__).resolve()), app["rid"], str(pid), str(observed_ms)],
                               capture_output=True, timeout=remaining)
        # The child writes only this fixed schema; raw streams/exceptions are
        # never evidence. subprocess.run kills and waits for its own child on timeout.
        require(child.returncode == 0 and len(child.stdout) <= 8192, "DiagnosticUnavailable")
        raw = json.loads(child.stdout)
        require(isinstance(raw, dict), "DiagnosticUnavailable")
        if raw.get("code") != "ObservedStable":
            raise IdentityFailure(raw.get("code") if raw.get("code") in CODES else "DiagnosticUnavailable")
        result.update(code="ObservedStable", stable=True, identity=identity(raw.get("identity"), pid))
    except subprocess.TimeoutExpired:
        result["code"] = "DiagnosticTimedOut"
    except Exception as error:
        result["code"] = failure_code(error)
    result["elapsedMs"] = round((time.monotonic() - began) * 1000)
    return result


def main():
    try:
        require(len(sys.argv) == 4, "InvalidDiagnosticInput")
        result = {"code": "ObservedStable", "identity": observe(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]))}
    except Exception as error:
        result = {"code": failure_code(error)}
    print(json.dumps(result, separators=(",", ":")))


if __name__ == "__main__":
    main()
