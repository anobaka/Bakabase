#!/usr/bin/env python3
"""Read ONLY an owned, installed product's native accessibility tree on hosted CI.

This is a capability gate, not acceptance of any product workflow. It never
accepts HTML/API responses as evidence of visible native WebView content.
"""
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import platform
import subprocess
import threading
import time

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_gui_package_base", HERE.parent / "upgrade-tests/run-package-acceptance.py")
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)
MAX_OUTPUT = 2 * 1024 * 1024
MAX_NODES = 1000
TIMEOUT = 30
READY_SECONDS = 90
INTERACTIVE_ACTIONS = {"AXPress", "AXConfirm", "InvokePatternIdentifiers.Pattern",
                       "TogglePatternIdentifiers.Pattern", "SelectionItemPatternIdentifiers.Pattern",
                       "ExpandCollapsePatternIdentifiers.Pattern", "ValuePatternIdentifiers.Pattern"}
STAGES = {"initialize", "preflight", "resolve-process", "load-uia", "enumerate-windows", "read-tree", "verify-process"}
AX_CODES = {"DirectAXTreeDeadline", "DirectAXReadCountExceeded", "DirectAXValueTypeMismatch",
            "DirectAXCollectionBudgetExceeded", "DirectAXCallFailed", "DirectAXAttributeRejected",
            "DirectAXProcessMismatch", "InvalidDirectAXInput", "DirectAXNotTrusted", "DirectAXApplicationRoleMismatch",
            "DirectAXWindowRoleMismatch", "DirectAXTreeBudgetExceeded", "DirectAXTreeCycle",
            "DirectAXSecureSubtreeUnavailable", "DirectAXTreeUnavailable", "DirectAXControlChanged",
            "DirectAXControlInvisible", "DirectAXControlOutsideWeb", "DirectAXActionTreeIncomplete", "DirectAXControlAmbiguous",
            "DirectAXGeometryUnavailable", "DirectAXChildrenCountMismatch"}
AX_ATTRIBUTES = {"AXRole", "AXSubrole", "AXTitle", "AXDescription", "AXIdentifier", "AXEnabled",
                 "AXHidden", "AXMinimized", "AXChildren", "AXWindows", "AXValue", "AXPosition", "AXSize", "AXParent"}
AX_OPERATIONS = {"initialize", "array-type", "array-count", "array-item", "element-type", "element-timeout",
                 "read-pid", "read-role", "read-subrole", "read-title", "read-description", "read-identifier",
                 "read-enabled", "read-hidden", "read-minimized", "read-children", "read-windows", "read-static-text",
                 "read-position", "read-size", "read-parent", "read-actions", "read-children-count",
                 "geometry-decode", "hit-test", "press"}
AX_CHILDREN_EVIDENCE = {"explicit-array", "static-text-unsupported", "no-value-count-zero"}
AX_COUNT_KINDS = {"number", "decimal-string", "rejected-string", "null", "undefined", "boolean", "other"}
AX_VISIBILITY = {"window-hidden", "zero-size", "outside-window", "no-hit", "owned-hit-test", "other-hit",
                 "other-window", "unverified-hit", "not-observed"}


def ax_diagnostic(raw):
    if not isinstance(raw, dict):
        return None
    code, attribute, error = raw.get("code"), raw.get("attribute"), raw.get("axError")
    return {"code": code if code in AX_CODES else "DirectAXTreeUnavailable",
            "operation": raw.get("operation") if raw.get("operation") in AX_OPERATIONS else None,
            "attribute": attribute if attribute in AX_ATTRIBUTES else None,
            "countKind": raw.get("countKind") if raw.get("countKind") in AX_COUNT_KINDS else None,
            "countValue": raw.get("countValue") if type(raw.get("countValue")) is int and 0 <= raw["countValue"] <= MAX_NODES else None,
            "axError": error if type(error) is int and -(2**31) <= error < 2**31 else None}


def direct_ax_source(entrypoint):
    require(entrypoint in ("macos-ax-snapshot.js", "macos-ax-action.js"), "InvalidDirectAXEntrypoint")
    return "\n".join((HERE / name).read_text() for name in ("macos-cf-values.js", "macos-ax-provider.js", entrypoint))


class ProbeFailure(AssertionError):
    pass


def require(value, code):
    if not value:
        raise ProbeFailure(code)


def hosted(app):
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), app["rid"])
    require(app["role"] in ("unified", "client"), "InvalidProductRole")
    expected = "Bakabase" if app["role"] == "unified" else "Bakabase.Client"
    if app["rid"] == "win-x64":
        expected += ".exe"
    require(Path(app["exe"]).is_absolute() and Path(app["exe"]).name == expected, "UnexpectedProductExecutable")
    require(Path(app["exe"]).is_file(), "ProductExecutableMissing")


def bounded_command(arguments, payload, timeout):
    """Bound both streams; never retain raw stderr or expose exception messages."""
    require(0 < timeout <= TIMEOUT, "InvalidProbeTimeout")
    process = subprocess.Popen([str(a) for a in arguments], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output, overflow = bytearray(), threading.Event()

    def drain(stream, keep):
        count = 0
        try:
            while True:
                chunk = stream.read(4096)
                if not chunk:
                    return
                count += len(chunk)
                limit = MAX_OUTPUT if keep else 8192
                if count > limit:
                    overflow.set()
                    return
                if keep:
                    output.extend(chunk)
        except (OSError, ValueError):
            overflow.set()

    readers = [threading.Thread(target=drain, args=(process.stdout, True), daemon=True),
               threading.Thread(target=drain, args=(process.stderr, False), daemon=True)]
    for reader in readers:
        reader.start()
    deadline = time.monotonic() + timeout
    try:
        process.stdin.write(payload.encode("utf-8"))
        process.stdin.close()
        while process.poll() is None:
            require(not overflow.is_set(), "ProbeOutputBudgetExceeded")
            require(time.monotonic() < deadline, "NativeProbeTimedOut")
            time.sleep(0.02)
        for reader in readers:
            reader.join(timeout=max(0, deadline - time.monotonic()))
        require(not any(reader.is_alive() for reader in readers), "NativeProbeStreamDidNotClose")
        require(not overflow.is_set(), "ProbeOutputBudgetExceeded")
        require(process.returncode == 0, "NativeProbeCommandFailed")
        try:
            result = json.loads(output.decode("utf-8-sig"))
        except (ValueError, UnicodeError):
            raise ProbeFailure("NativeProbeInvalidJson") from None
        require(isinstance(result, dict), "NativeProbeInvalidJson")
        return result
    finally:
        if process.poll() is None:
            process.kill()  # Only the read-only helper created above, never product/system processes.
        process.wait(timeout=5)
        for reader in readers:
            reader.join(timeout=1)
        for stream in (process.stdin, process.stdout, process.stderr):
            stream.close()


def mac_identity(pid):
    require(type(pid) is int and pid > 0, "InvalidProductPid")
    library = ctypes.CDLL("/usr/lib/libproc.dylib")
    buffer = ctypes.create_string_buffer(4096)
    require(library.proc_pidpath(pid, buffer, len(buffer)) > 0, "ProductProcessMissing")
    result = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "lstart="],
                            capture_output=True, text=True, timeout=3)
    require(result.returncode == 0 and result.stdout.strip(), "ProductProcessMissing")
    return {"pid": pid, "executable": buffer.value.decode(), "started": result.stdout.strip()}


def native_snapshot(app, pid, timeout=TIMEOUT):
    """All native reads are behind the hosted guard, including identity checks."""
    hosted(app)
    require(type(pid) is int and pid > 0, "InvalidProductPid")
    data = {"pid": pid, "executable": str(Path(app["exe"]).resolve()), "maxNodes": MAX_NODES, "maxDepth": 40,
            "readBudgetMs": min(24000, max(100, int((timeout-2)*1000)))}
    if app["rid"].startswith("osx-"):
        before = mac_identity(pid)
        require(Path(before["executable"]).resolve() == Path(app["exe"]).resolve(), "ProductExecutableMismatch")
        backend = app.get("nativeBackend", "macos-system-events-ax")
        require(backend in ("macos-system-events-ax", "macos-direct-ax"), "InvalidNativeBackend")
        script = direct_ax_source("macos-ax-snapshot.js") if backend == "macos-direct-ax" else (HERE / "macos-snapshot.js").read_text()
        # osascript's '-' is the script from stdin. JSON is source-escaped data,
        # never evaluated as a command or passed through a shell.
        result = bounded_command(["/usr/bin/osascript", "-l", "JavaScript", "-"],
                                 "const input = " + json.dumps(data) + ";\n" + script, timeout)
        require(mac_identity(pid) == before, "ProductProcessChangedDuringProbe")
        require(result.get("backend") == backend, "UnexpectedNativeBackend")
        result["process"] = before
    else:
        result = bounded_command(["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-STA",
                                  "-ExecutionPolicy", "Bypass", "-File", HERE / "windows-snapshot.ps1"],
                                 json.dumps(data) + "\n", timeout)
        if result.get("enabled") is False and result.get("errorStage") in STAGES and result.get("windows") == []:
            return result  # An explicit failed observation, never a capability pass.
        identity = result.get("process", {})
        require(identity.get("pid") == pid and isinstance(identity.get("started"), str) and
                Path(identity.get("executable", "")).resolve() == Path(app["exe"]).resolve(),
                "ProductExecutableMismatch")
    return result


def node_visible(snapshot, node):
    # UIA explicitly exposes offscreen state, including virtualized/collapsed
    # descendants. Missing UIA visibility is not affirmative evidence. AX keeps
    # its existing visible-window scope; it has no equivalent property here.
    return node.get("visible") is True if snapshot.get("backend") in ("windows-uia", "macos-direct-ax") else node.get("visible") is not False


def valid_runtime_id(value):
    return isinstance(value, list) and 1 <= len(value) <= 64 and all(type(item) is int and -(2**31) <= item < 2**31 for item in value)


def summarize(snapshot):
    require(snapshot.get("backend") in ("macos-system-events-ax", "windows-uia", "macos-direct-ax"), "InvalidNativeBackend")
    require(snapshot.get("readOnly") is True, "NotReadOnlyProbe")
    windows = snapshot.get("windows")
    require(isinstance(windows, list) and len(windows) <= 8, "InvalidWindowCollection")
    total, web_nodes, controls, texts, window_count = 0, 0, 0, 0, 0
    for window in windows:
        require(isinstance(window, dict) and isinstance(window.get("nodes"), list), "InvalidWindowTree")
        if window.get("visible") is not True:
            continue
        window_count += 1
        for node in window["nodes"]:
            require(isinstance(node, dict) and isinstance(node.get("path"), list), "InvalidNativeNode")
            total += 1
            require(total <= MAX_NODES, "InvalidNodeCount")
            if node.get("insideWebContent") is True and node_visible(snapshot, node):
                web_nodes += 1
                if node.get("enabled") is True and INTERACTIVE_ACTIONS.intersection(node.get("actions", [])):
                    controls += 1
                if node.get("name") or node.get("text"):
                    texts += 1
    capable = snapshot.get("enabled") is True and window_count > 0 and web_nodes > 0 and controls > 0 and texts > 0
    return {"capabilityPassed": capable, "mainFlowPassed": False, "visibleWindows": window_count,
            "nodeCount": total, "webContentNodes": web_nodes, "interactiveWebControls": controls,
            "namedWebNodes": texts, "treeTruncated": snapshot.get("truncated") is True,
            "reason": None if capable else "NativeWebContentNotAccessible",
            "nativeErrorStage": snapshot.get("errorStage") if snapshot.get("errorStage") in STAGES else None,
            "diagnostic": ax_diagnostic(snapshot.get("diagnostic")),
            "scope": "read-only-native-accessibility-capability"}


def sanitize(value, secrets=()):
    """Only the named safe schema is persisted; editable field values are absent."""
    def string(raw, limit=300):
        if not isinstance(raw, str):
            return ""
        for secret in secrets:
            if secret:
                raw = raw.replace(secret, "[redacted]")
        return raw[:limit]
    output = {key: value.get(key) for key in ("backend", "readOnly", "enabled", "truncated", "elapsedMs")}
    output["errorStage"] = value.get("errorStage") if value.get("errorStage") in STAGES else None
    output["diagnostic"] = ax_diagnostic(value.get("diagnostic"))
    identity = value.get("process", {})
    output["process"] = {"pid": identity.get("pid"), "started": string(identity.get("started"))}
    output["windows"] = []
    count = 0
    for window in value.get("windows", [])[:8]:
        safe = {"index": window.get("index"), "visible": window.get("visible") is True,
                "name": string(window.get("name")), "nodes": []}
        for node in window.get("nodes", []):
            count += 1
            require(count <= MAX_NODES, "InvalidNodeCount")
            safe["nodes"].append({"path": node.get("path"), "role": string(node.get("role")),
                "name": string(node.get("name")), "text": string(node.get("text")),
                "identifier": string(node.get("identifier")), "enabled": node.get("enabled") is True,
                "visible": node.get("visible") if type(node.get("visible")) is bool else None,
                "visibilityEvidence": node.get("visibilityEvidence") if node.get("visibilityEvidence") in AX_VISIBILITY else None,
                "editableAncestor": node.get("editableAncestor") is True,
                "childrenEvidence": node.get("childrenEvidence") if node.get("childrenEvidence") in AX_CHILDREN_EVIDENCE else None,
                "childrenCountKind": node.get("childrenCountKind") if node.get("childrenCountKind") in AX_COUNT_KINDS else None,
                "childCount": node.get("childCount") if type(node.get("childCount")) is int and 0 <= node["childCount"] <= MAX_NODES else None,
                "runtimeId": node.get("runtimeId") if valid_runtime_id(node.get("runtimeId")) else None,
                "insideWebContent": node.get("insideWebContent") is True,
                "actions": [string(action, 60) for action in node.get("actions", [])[:12]],
                "password": node.get("password") is True})
        output["windows"].append(safe)
    return output


def capture(app, pid, destination, require_complete=False):
    hosted(app)
    destination = Path(destination)
    require(not destination.exists(), "ProbeResultsAlreadyExist")
    destination.mkdir(parents=True)
    report = {"capabilityPassed": False, "mainFlowPassed": False, "role": app["role"], "rid": app["rid"],
              "scope": "read-only-native-accessibility-capability"}
    report["attempts"] = []
    deadline, identity = time.monotonic() + READY_SECONDS, None
    try:
        for attempt in range(1, 11):
            remaining = deadline-time.monotonic()
            require(remaining > 7, "NativeUiReadinessTimedOut")
            snapshot = native_snapshot(app, pid, timeout=min(TIMEOUT, remaining-6))
            current = snapshot.get("process")
            if identity is None:
                identity = current
            require(current == identity, "ProductProcessChangedDuringReadiness")
            summary = summarize(snapshot)
            report.update(summary)
            report["attempts"].append({"attempt": attempt, **summary})
            tree = json.dumps(sanitize(snapshot), indent=2)
            (destination / f"tree-{attempt:02}.json").write_text(tree, encoding="utf-8")
            (destination / "tree.json").write_text(tree, encoding="utf-8")
            if ((summary["capabilityPassed"] and (not require_complete or snapshot.get("truncated") is False)) or
                    snapshot.get("enabled") is not True or snapshot.get("errorStage") and not require_complete):
                break
            time.sleep(min(1, max(0, deadline-time.monotonic())))
    except Exception as error:
        report["capabilityPassed"] = False
        report["error"] = {"type": type(error).__name__,
                           "code": str(error) if isinstance(error, ProbeFailure) else "NativeProbeUnavailable"}
    report["completeTreePassed"] = report.get("capabilityPassed") is True and report.get("treeTruncated") is False
    if require_complete and not report["completeTreePassed"]:
        report["capabilityPassed"] = False
    (destination / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report
