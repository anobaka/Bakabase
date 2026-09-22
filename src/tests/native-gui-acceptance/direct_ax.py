#!/usr/bin/env python3
"""Hosted-only, nonprompting direct AX availability diagnostic for an owned PID."""
import importlib.util
import json
from pathlib import Path
import time

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_direct_ax_probe", HERE / "probe.py")
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)
STAGES = {"load-framework", "check-existing-trust", "create-owned-application", "read-owned-application-role",
          "read-owned-window-count", "complete", "verify-owned-process"}
CODES = {"DirectAXNotTrusted", "InvalidProductPid", "DirectAXProcessMismatch", "DirectAXTimeoutUnavailable",
         "DirectAXApplicationUnavailable", "DirectAXWindowsUnavailable", "DirectAXWindowBudgetExceeded",
         "DirectAXReadBudgetExceeded", "DirectAXNoOwnedWindow", "DirectAXDiagnosticUnavailable",
         "NativeProbeTimedOut", "NativeProbeCommandFailed", "NativeProbeInvalidJson", "ProductProcessChangedDuringProbe",
         "DirectAXRoleReadFailed", "DirectAXRoleTypeMismatch", "DirectAXRoleMismatch",
         "DirectAXWindowsReadFailed", "DirectAXWindowsTypeMismatch"}
DIAGNOSTIC_ERRORS = {"pidReadError", "messagingTimeoutError", "roleReadError", "windowsReadError"}
DIAGNOSTIC_TYPES = {"roleRawTypeId", "roleTypeId", "expectedRoleTypeId", "windowsRawTypeId",
                    "windowsTypeId", "expectedWindowsTypeId"}
DIAGNOSTIC_FLAGS = {"roleValueWasRef", "roleMatches", "windowsValueWasRef", "windowCountObserved"}


def safe_diagnostics(raw):
    """Only fixed numeric/boolean evidence; never serialize native objects or text."""
    probe.require(isinstance(raw, dict), "DirectAXDiagnosticUnavailable")
    safe = {}
    for key in DIAGNOSTIC_ERRORS | DIAGNOSTIC_TYPES | DIAGNOSTIC_FLAGS:
        if key not in raw:
            continue
        value = raw[key]
        valid = type(value) is bool if key in DIAGNOSTIC_FLAGS else (
            type(value) is int and (-(2**31) <= value < 2**31 if key in DIAGNOSTIC_ERRORS else 0 <= value < 2**32))
        probe.require(valid, "DirectAXDiagnosticUnavailable")
        safe[key] = value
    probe.require(type(safe.get("windowCountObserved")) is bool, "DirectAXDiagnosticUnavailable")
    return safe


def capture(app, pid):
    # This guard precedes every native read, including AXIsProcessTrusted. The
    # diagnostic never grants permission and never contributes a workflow pass.
    probe.hosted(app)
    probe.require(app["rid"].startswith("osx-"), "DirectAXRequiresMacOS")
    probe.require(type(pid) is int and pid > 0, "InvalidProductPid")
    began = time.monotonic()
    result = {"scope": "owned-macos-direct-ax-availability-only", "observer": "osascript-jxa-ApplicationServices", "readOnly": True, "pid": pid,
              "trusted": None, "available": False, "ownedWindowCount": 0, "mainFlowPassed": False,
              "code": "DirectAXDiagnosticUnavailable", "stage": "verify-owned-process",
              "diagnostics": {"windowCountObserved": False}}
    try:
        before = probe.mac_identity(pid)
        probe.require(Path(before["executable"]).resolve() == Path(app["exe"]).resolve(), "DirectAXProcessMismatch")
        payload = ("const input=" + json.dumps({"pid": pid}) + ";\n" +
                   (HERE / "macos-cf-values.js").read_text() + "\n" + (HERE / "macos-direct-ax.js").read_text())
        raw = probe.bounded_command(["/usr/bin/osascript", "-l", "JavaScript", "-"], payload, 5)
        probe.require(probe.mac_identity(pid) == before, "ProductProcessChangedDuringProbe")
        code, count = raw.get("code"), raw.get("ownedWindowCount")
        probe.require(raw.get("readOnly") is True and raw.get("stage") in STAGES and
                      (type(raw.get("trusted")) is bool or raw.get("trusted") is None) and (code is None or code in CODES) and
                      type(count) is int and 0 <= count <= 8, "DirectAXDiagnosticUnavailable")
        diagnostics = safe_diagnostics(raw.get("diagnostics"))
        available = (code is None and raw.get("trusted") is True and raw.get("available") is True and count > 0 and
                     raw.get("stage") == "complete" and diagnostics["windowCountObserved"] is True and
                     all(diagnostics.get(key) == 0 for key in DIAGNOSTIC_ERRORS) and
                     all(key in diagnostics for key in ("roleTypeId", "expectedRoleTypeId", "windowsTypeId", "expectedWindowsTypeId")) and
                     diagnostics.get("roleMatches") is True and
                     diagnostics.get("roleTypeId") == diagnostics.get("expectedRoleTypeId") and
                     diagnostics.get("windowsTypeId") == diagnostics.get("expectedWindowsTypeId"))
        probe.require(code is not None or available, "DirectAXDiagnosticUnavailable")
        result.update(trusted=raw.get("trusted"), available=available, ownedWindowCount=count,
                      code=code, stage=raw["stage"], diagnostics=diagnostics)
    except Exception as error:
        code = str(error) if isinstance(error, probe.ProbeFailure) else None
        result["code"] = code if code in CODES else "DirectAXDiagnosticUnavailable"
    result["elapsedMs"] = round((time.monotonic()-began)*1000)
    return result
