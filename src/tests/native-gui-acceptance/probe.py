#!/usr/bin/env python3
"""Read ONLY an owned, installed product's native accessibility tree on hosted CI.

This is a capability gate, not acceptance of any product workflow. It never
accepts HTML/API responses as evidence of visible native WebView content.
"""
import ctypes
import importlib.util
import json
import math
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
            "DirectAXGeometryUnavailable", "DirectAXChildrenCountMismatch", "DirectAXEditableDescendant",
            "DirectAXOperationUnsupported", "DirectAXEmbeddedStructureChanged", "DirectAXEmbeddedRootRoleRejected",
            "DirectAXEmbeddedWrapperRoleRejected", "DirectAXEmbeddedWrapperBranch", "DirectAXEmbeddedContentScopeChanged"}
AX_ATTRIBUTES = {"AXRole", "AXSubrole", "AXTitle", "AXDescription", "AXIdentifier", "AXEnabled",
                 "AXHidden", "AXMinimized", "AXChildren", "AXWindows", "AXValue", "AXPosition", "AXSize", "AXParent", "AXWindow"}
AX_OPERATIONS = {"initialize", "array-type", "array-count", "array-item", "element-type", "element-timeout",
                 "read-pid", "read-role", "read-subrole", "read-title", "read-description", "read-identifier",
                 "read-enabled", "read-hidden", "read-minimized", "read-children", "read-windows", "read-static-text",
                 "read-position", "read-size", "read-parent", "read-actions", "read-children-count",
                 "geometry-decode", "hit-test", "press", "read-value-settable", "set-value", "scroll-to-visible", "verify-embedded-relation"}
AX_CHILDREN_EVIDENCE = {"explicit-array", "static-text-unsupported", "no-value-count-zero"}
AX_COUNT_KINDS = {"number", "decimal-string", "rejected-string", "null", "undefined", "boolean", "other"}
AX_VISIBILITY = {"window-hidden", "zero-size", "outside-window", "no-hit", "owned-hit-test", "other-hit",
                 "other-window", "unverified-hit", "not-observed"}
PID_STATUSES = {"not-attempted", "pid-read-failed", "owned-edge-changed", "pid-changed", "observed",
                "budget-exhausted", "diagnostic-unavailable"}


def pid_diagnostic(raw):
    """Pointer relations are boolean evidence only, never an ownership grant."""
    if not isinstance(raw, dict):
        return None
    def integer(value, low, high):
        return value if type(value) is int and low <= value <= high else None
    def path(value):
        return value if isinstance(value, list) and 1 <= len(value) <= 42 and all(
            type(item) is int and 0 <= item < MAX_NODES for item in value) else None
    output = {"expectedPid": integer(raw.get("expectedPid"), 1, 2**31-1),
              "actualPid": integer(raw.get("actualPid"), 1, 2**31-1),
              "observedEpochMs": integer(raw.get("observedEpochMs"), 0, 2**53-1),
              "origin": raw.get("origin") if raw.get("origin") in {"owned-child-edge", "other"} else None,
              "status": raw.get("status") if raw.get("status") in PID_STATUSES else "diagnostic-unavailable",
              "path": path(raw.get("path")), "parentPath": path(raw.get("parentPath")),
              "windowIndex": integer(raw.get("windowIndex"), 0, 7),
              "childIndex": integer(raw.get("childIndex"), 0, MAX_NODES-1),
              "parentChildCount": integer(raw.get("parentChildCount"), 0, MAX_NODES)}
    for key in ("edgeStillMatches", "windowStillMatches", "parentMatches", "windowMatches", "actualPidStable"):
        output[key] = raw.get(key) if type(raw.get(key)) is bool else None
    for key in ("parentAXError", "windowAXError"):
        output[key] = integer(raw.get(key), -(2**31), 2**31-1)
    return output


def collect_pid_identity(app, diagnostic, timeout):
    # Separate short-lived helper bounds libproc calls and has its own hosted
    # guard. This is evidence collection only; no process is authorized by it.
    return pid_module().capture(app, diagnostic, timeout)


def pid_module():
    spec = importlib.util.spec_from_file_location("native_gui_pid_identity", HERE / "macos_pid_identity.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def embedded_binding(raw):
    """Validate and copy only the fixed, single-subtree binding schema."""
    if not isinstance(raw, dict):
        return None
    try:
        require(type(raw.get("schemaVersion")) is int and raw["schemaVersion"] == 1 and
                raw.get("initialRelationsVerified") is True, "InvalidEmbeddedBinding")
        path, observed = raw.get("rootPath"), raw.get("observedEpochMs")
        require(isinstance(path, list) and 2 <= len(path) <= 42 and type(path[0]) is int and 0 <= path[0] < 8 and
                all(type(i) is int and 0 <= i < MAX_NODES for i in path), "InvalidEmbeddedBinding")
        count = raw.get("parentChildCount")
        require(type(count) is int and path[-1] < count <= MAX_NODES and type(observed) is int and
                0 <= observed <= 2**53-1, "InvalidEmbeddedBinding")
        module = pid_module()
        application = module.identity(raw.get("application"), raw["application"]["pid"])
        embedded = module.identity(raw.get("embedded"), raw["embedded"]["pid"])
        require(application["pid"] != embedded["pid"] and application["uid"] == embedded["uid"], "InvalidEmbeddedBinding")
        require(all(item["startSeconds"] * 1000000 + item["startMicroseconds"] <= observed * 1000 + 999
                    for item in (application, embedded)), "InvalidEmbeddedBinding")
        return {"schemaVersion": 1, "initialRelationsVerified": True, "rootPath": list(path),
                "parentChildCount": count, "observedEpochMs": observed, "application": application, "embedded": embedded}
    except (AssertionError, TypeError, KeyError, ValueError):
        return None


def binding_from_diagnostic(app, snapshot, diagnostic, identity_result):
    flags = ("edgeStillMatches", "windowStillMatches", "parentMatches", "windowMatches", "actualPidStable")
    if not (diagnostic and diagnostic["origin"] == "owned-child-edge" and diagnostic["status"] == "observed" and
            all(diagnostic[key] is True for key in flags) and diagnostic["parentAXError"] == 0 and diagnostic["windowAXError"] == 0 and
            identity_result.get("code") == "ObservedStable" and identity_result.get("stable") is True):
        return None
    path = diagnostic["path"]
    if not (path and diagnostic["parentPath"] == path[:-1] and diagnostic["windowIndex"] == path[0] and
            diagnostic["childIndex"] == path[-1] and identity_result.get("observedEpochMs") == diagnostic["observedEpochMs"]):
        return None
    candidate = embedded_binding({"schemaVersion": 1, "initialRelationsVerified": True, "rootPath": path,
        "parentChildCount": diagnostic["parentChildCount"], "observedEpochMs": diagnostic["observedEpochMs"],
        "application": identity_result.get("ownedIdentity"), "embedded": identity_result.get("identity")})
    if not (candidate and snapshot.get("ownedOSIdentityStable") is True and candidate["application"] == snapshot.get("ownedOSIdentity") and
            candidate["application"]["pid"] == diagnostic["expectedPid"] and candidate["embedded"]["pid"] == diagnostic["actualPid"] and
            candidate["application"]["executable"] == str(Path(app["exe"]).resolve())):
        return None
    return candidate


def embedded_proof(raw):
    if not isinstance(raw, dict):
        return None
    role = raw.get("rootRole")
    chain = raw.get("wrapperChain")
    safe_chain = None
    if isinstance(chain, list) and len(chain) <= 40:
        safe_chain = [{"path": pid_diagnostic({"path": step.get("path")})["path"],
                       "role": step.get("role") if step.get("role") in {"AXGroup", "AXWebArea", "AXScrollArea", "AXUnknown", "Other"} else None,
                       "childCount": step.get("childCount") if type(step.get("childCount")) is int and 0 <= step["childCount"] <= MAX_NODES else None,
                       "childrenEvidence": step.get("childrenEvidence") if step.get("childrenEvidence") in AX_CHILDREN_EVIDENCE else None,
                       "parentMatches": step.get("parentMatches") is True, "windowMatches": step.get("windowMatches") is True}
                      for step in chain if isinstance(step, dict)]
        if len(safe_chain) != len(chain):
            safe_chain = None
    return {"verified": raw.get("verified") is True, "osIdentityStable": raw.get("osIdentityStable") is True,
            "rootRole": role if role in {"AXWebArea", "AXGroup", "AXScrollArea", "AXUnknown", "Other"} else None,
            "applicationPid": raw.get("applicationPid") if type(raw.get("applicationPid")) is int else None,
            "embeddedPid": raw.get("embeddedPid") if type(raw.get("embeddedPid")) is int else None,
            "rootPath": pid_diagnostic({"path": raw.get("rootPath")})["path"],
            "contentRootRole": "AXWebArea" if raw.get("contentRootRole") == "AXWebArea" else None,
            "contentRootPath": pid_diagnostic({"path": raw.get("contentRootPath")})["path"], "wrapperChain": safe_chain}


def content_scope(proof):
    """A fixed single-child wrapper chain, never a same-PID content allowlist."""
    if not proof or proof.get("contentRootRole") != "AXWebArea" or not proof.get("rootPath"):
        return None
    chain = proof.get("wrapperChain")
    if not isinstance(chain, list) or proof.get("rootRole") != ("AXGroup" if chain else "AXWebArea"):
        return None
    path = list(proof["rootPath"])
    for step in chain:
        if not (step["path"] == path and step["role"] == "AXGroup" and step["childCount"] == 1 and
                step["childrenEvidence"] == "explicit-array" and step["parentMatches"] and step["windowMatches"]):
            return None
        path += [0]
    if path != proof.get("contentRootPath") or len(path) > 41:
        return None
    return {"contentRootRole": "AXWebArea", "contentRootPath": path,
            "wrapperChain": [{key: step[key] for key in ("path", "role", "childCount")} for step in chain]}


def ax_read_counts(raw):
    if not isinstance(raw, dict):
        return None
    return {key: raw.get(key) if type(raw.get(key)) is int and 0 <= raw[key] <= maximum else None
            for key, maximum in (("checks", 16001), ("rootProofs", 16001), ("maximumEmbeddedDepth", 40))}


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
    if app.get("role") == "source-fixture":
        spec = importlib.util.spec_from_file_location("native_gui_source_guard", HERE / "source_fixture.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.validate_probe_app(app)
        return
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


def mac_identity(pid, timeout=3):
    require(type(pid) is int and pid > 0, "InvalidProductPid")
    library = ctypes.CDLL("/usr/lib/libproc.dylib")
    buffer = ctypes.create_string_buffer(4096)
    require(library.proc_pidpath(pid, buffer, len(buffer)) > 0, "ProductProcessMissing")
    result = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "lstart="],
                            capture_output=True, text=True, timeout=timeout)
    require(result.returncode == 0 and result.stdout.strip(), "ProductProcessMissing")
    return {"pid": pid, "executable": buffer.value.decode(), "started": result.stdout.strip()}


def owned_identity(app, pid, timeout):
    result = pid_module().capture_owned(app, pid, timeout)
    require(result.get("code") == "ObservedStable" and result.get("stable") is True, "OwnedOSIdentityUnavailable")
    return result["identity"]


def direct_execute(app, pid, data, entrypoint, timeout, binding=None, expected_owned=None):
    """One deadline contains pre/post OS checks and the bounded native helper."""
    require(0 < timeout <= TIMEOUT, "InvalidProbeTimeout")
    deadline = time.monotonic() + timeout
    def remaining(cap):
        value = min(cap, deadline-time.monotonic())
        require(value > 0, "NativeProbeTimedOut")
        return value
    legacy = mac_identity(pid, timeout=remaining(3))
    require(Path(legacy["executable"]).resolve() == Path(app["exe"]).resolve(), "ProductExecutableMismatch")
    if binding is not None:
        require(embedded_binding(binding) == binding and binding["application"]["pid"] == pid and
                binding["application"]["executable"] == str(Path(app["exe"]).resolve()), "InvalidEmbeddedBinding")
        def pair():
            diagnostic = {"origin": "owned-child-edge", "actualPid": binding["embedded"]["pid"],
                          "expectedPid": pid, "observedEpochMs": binding["observedEpochMs"]}
            value = collect_pid_identity(app, diagnostic, remaining(2))
            require(value.get("code") == "ObservedStable" and value.get("stable") is True and
                    value.get("identity") == binding["embedded"] and value.get("ownedIdentity") == binding["application"],
                    "EmbeddedOSIdentityChanged")
            return value["ownedIdentity"]
        before = pair()
        data = {**data, "embeddedBinding": {"pid": binding["embedded"]["pid"], "rootPath": binding["rootPath"],
                                           "parentChildCount": binding["parentChildCount"]}}
    else:
        before = owned_identity(app, pid, remaining(2))
    if expected_owned is not None:
        require(before == expected_owned, "OwnedOSIdentityChanged")
    # Preserve the original ceilings, reserving time for both post checks.
    command_timeout = deadline-time.monotonic()-5
    require(command_timeout > 0, "NativeProbeTimedOut")
    data = {**data, "readBudgetMs": min(24000, int(command_timeout*1000))}
    result = bounded_command(["/usr/bin/osascript", "-l", "JavaScript", "-"],
                             "const input = " + json.dumps(data) + ";\n" + direct_ax_source(entrypoint), command_timeout)
    after = pair() if binding is not None else owned_identity(app, pid, remaining(2))
    require(before == after, "OwnedOSIdentityChanged")
    require(mac_identity(pid, timeout=remaining(3)) == legacy, "ProductProcessChangedDuringProbe")
    require(time.monotonic() <= deadline, "NativeProbeTimedOut")
    result["process"], result["ownedOSIdentity"], result["ownedOSIdentityStable"] = legacy, before, True
    if binding is not None:
        proof = embedded_proof(result.get("embeddedAXProof"))
        success = result.get("performed") is True if entrypoint == "macos-ax-action.js" else (
            result.get("enabled") is True and result.get("truncated") is False)
        if success:
            require(proof and proof["verified"] is True and content_scope(proof) is not None and
                    proof["applicationPid"] == pid and proof["embeddedPid"] == binding["embedded"]["pid"] and
                    proof["rootPath"] == binding["rootPath"], "EmbeddedStructureNotVerified")
            proof["osIdentityStable"] = True
            result["embeddedAXBinding"] = binding
        result["embeddedAXProof"] = proof
    return result


def direct_action(app, snapshot, record, timeout=15):
    hosted(app)
    require(snapshot.get("backend") == "macos-direct-ax" and app["rid"].startswith("osx-") and
            snapshot.get("enabled") is True and snapshot.get("truncated") is False, "IncompleteNativeTree")
    binding = snapshot.get("embeddedAXBinding")
    require(binding == app.get("embeddedAXBinding"), "EmbeddedActionBindingChanged")
    if binding is not None:
        proof = embedded_proof(snapshot.get("embeddedAXProof"))
        require(embedded_binding(binding) == binding and proof and proof == snapshot.get("embeddedAXProof") and
                proof["verified"] is True and proof["osIdentityStable"] is True and content_scope(proof) is not None and
                proof["rootPath"] == binding["rootPath"] and proof["applicationPid"] == binding["application"]["pid"] and
                proof["embeddedPid"] == binding["embedded"]["pid"],
                "EmbeddedActionBindingChanged")
        record = {**record, "expectedContentScope": content_scope(proof)}
    require(record.get("pid") == snapshot.get("process", {}).get("pid") and timeout <= 15, "ProductProcessChangedBeforeAction")
    require(isinstance(snapshot.get("ownedOSIdentity"), dict) and snapshot.get("ownedOSIdentityStable") is True,
            "OwnedOSIdentityUnavailable")
    return direct_execute(app, record["pid"], {**record, "maxNodes": MAX_NODES, "maxDepth": 40},
                          "macos-ax-action.js", timeout, binding, snapshot["ownedOSIdentity"])


def native_snapshot(app, pid, timeout=TIMEOUT):
    """All native reads are behind the hosted guard, including identity checks."""
    hosted(app)
    require(type(pid) is int and pid > 0, "InvalidProductPid")
    data = {"pid": pid, "executable": str(Path(app["exe"]).resolve()), "maxNodes": MAX_NODES, "maxDepth": 40,
            "readBudgetMs": min(24000, max(100, int((timeout-2)*1000)))}
    if app["rid"].startswith("osx-"):
        if app.get("nativeBackend") == "macos-direct-ax":
            binding = app.get("embeddedAXBinding", app.get("_embeddedAXCandidate"))
            result = direct_execute(app, pid, data, "macos-ax-snapshot.js", timeout, binding)
            require(result.get("backend") == "macos-direct-ax", "UnexpectedNativeBackend")
            return result
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
            "pidMismatch": pid_diagnostic(snapshot.get("pidMismatch")),
            "embeddedAXProof": embedded_proof(snapshot.get("embeddedAXProof")),
            "axReadCounts": ax_read_counts(snapshot.get("axReadCounts")),
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
    output["pidMismatch"] = pid_diagnostic(value.get("pidMismatch"))
    output["embeddedAXBinding"] = embedded_binding(value.get("embeddedAXBinding"))
    output["embeddedAXProof"] = embedded_proof(value.get("embeddedAXProof"))
    output["axReadCounts"] = ax_read_counts(value.get("axReadCounts"))
    output["ownedOSIdentityStable"] = value.get("ownedOSIdentityStable") is True
    output["ownedOSIdentity"] = None
    if isinstance(value.get("ownedOSIdentity"), dict):
        try:
            output["ownedOSIdentity"] = pid_module().identity(value["ownedOSIdentity"], value.get("process", {}).get("pid"))
        except (AssertionError, TypeError, ValueError):
            output["ownedOSIdentityStable"] = False
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
                "structureOnly": node.get("structureOnly") is True,
                "valueSettable": node.get("valueSettable") if type(node.get("valueSettable")) is bool else None,
                "scrollToVisible": node.get("scrollToVisible") is True,
                "childrenEvidence": node.get("childrenEvidence") if node.get("childrenEvidence") in AX_CHILDREN_EVIDENCE else None,
                "childrenCountKind": node.get("childrenCountKind") if node.get("childrenCountKind") in AX_COUNT_KINDS else None,
                "childCount": node.get("childCount") if type(node.get("childCount")) is int and 0 <= node["childCount"] <= MAX_NODES else None,
                "runtimeId": node.get("runtimeId") if valid_runtime_id(node.get("runtimeId")) else None,
                "insideWebContent": node.get("insideWebContent") is True,
                "actions": [string(action, 60) for action in node.get("actions", [])[:12]],
                "password": node.get("password") is True})
        output["windows"].append(safe)
    return output


def capture(app, pid, destination, require_complete=False, deadline=None):
    hosted(app)
    destination = Path(destination)
    require(not destination.exists(), "ProbeResultsAlreadyExist")
    destination.mkdir(parents=True)
    report = {"capabilityPassed": False, "mainFlowPassed": False, "role": app["role"], "rid": app["rid"],
              "scope": "read-only-native-accessibility-capability"}
    report["attempts"] = []
    readiness_deadline = time.monotonic() + READY_SECONDS
    if deadline is not None:
        require(type(deadline) in (int, float) and math.isfinite(deadline), "InvalidProbeDeadline")
        readiness_deadline = min(readiness_deadline, deadline)
    deadline, identity = readiness_deadline, None
    pid_identity_attempted = False
    try:
        for attempt in range(1, 11):
            remaining = deadline-time.monotonic()
            require(remaining > 7, "NativeUiReadinessTimedOut")
            read_timeout = min(TIMEOUT, remaining-6)
            read_deadline = time.monotonic() + read_timeout
            snapshot = native_snapshot(app, pid, timeout=read_timeout)
            current = snapshot.get("process")
            if identity is None:
                identity = current
            require(current == identity, "ProductProcessChangedDuringReadiness")
            summary = summarize(snapshot)
            mismatch = summary["pidMismatch"]
            if (not pid_identity_attempted and app["rid"].startswith("osx-") and
                    snapshot.get("backend") == "macos-direct-ax" and snapshot.get("truncated") is True and
                    summary["diagnostic"] and summary["diagnostic"]["code"] == "DirectAXProcessMismatch" and
                    mismatch and mismatch["origin"] == "owned-child-edge" and mismatch["expectedPid"] == pid and
                    mismatch["actualPid"] is not None and mismatch["actualPid"] != pid):
                pid_identity_attempted = True
                report["pidIdentityDiagnostic"] = collect_pid_identity(
                    app, mismatch, max(0, min(2, deadline-time.monotonic(), read_deadline-time.monotonic())))
                if not app.get("embeddedAXBinding") and not app.get("_embeddedAXCandidate"):
                    candidate = binding_from_diagnostic(app, snapshot, mismatch, report["pidIdentityDiagnostic"])
                    if candidate is not None:
                        app["_embeddedAXCandidate"] = candidate
                        report["embeddedCandidatePrepared"] = True
            report.update(summary)
            report["attempts"].append({"attempt": attempt, **summary})
            tree = json.dumps(sanitize(snapshot), indent=2)
            (destination / f"tree-{attempt:02}.json").write_text(tree, encoding="utf-8")
            (destination / "tree.json").write_text(tree, encoding="utf-8")
            if summary["capabilityPassed"] and snapshot.get("truncated") is False and snapshot.get("embeddedAXBinding") is not None:
                require(embedded_binding(snapshot["embeddedAXBinding"]) == app.get("_embeddedAXCandidate", app.get("embeddedAXBinding")),
                        "EmbeddedBindingChangedDuringReadiness")
                app["embeddedAXBinding"] = snapshot["embeddedAXBinding"]
                report["embeddedAXBinding"] = snapshot["embeddedAXBinding"]
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
    app.pop("_embeddedAXCandidate", None)
    (destination / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report
