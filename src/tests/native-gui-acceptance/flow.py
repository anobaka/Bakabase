#!/usr/bin/env python3
"""Native AX/UIA controls, never DOM/HTTP mutations, drive the empty-library flow."""
import importlib.util
import http.client
import json
from pathlib import Path
import time

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_flow_probe", HERE / "probe.py")
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)
KINDS = {
    "button": {"AXButton", "ControlType.Button"},
    "menu": {"AXMenuItem", "ControlType.MenuItem"},
    "link": {"AXLink", "ControlType.Hyperlink"},
    "input": {"AXTextField", "AXTextArea", "ControlType.Edit"},
    # WKWebView exposes HTML aria-pressed buttons as AXCheckBox.
    "scope": {"AXCheckBox", "ControlType.Button", "ControlType.CheckBox"},
    "checkbox": {"AXCheckBox", "ControlType.CheckBox"},
    "region": {"AXGroup", "AXScrollArea", "ControlType.Group", "ControlType.Pane"},
    "text": {"AXStaticText", "ControlType.Text"},
}
ACTION_STAGES = {"initialize", "preflight", "resolve-process", "resolve-control", "validate-control", "perform-action",
                 "enumerate-windows", "read-tree", "verify-process"}


def complete(snapshot):
    return snapshot.get("enabled") is True and snapshot.get("truncated") is False


def nodes(snapshot, *, visible=True):
    probe.require(complete(snapshot), "IncompleteNativeTree")
    return [node for window in snapshot.get("windows", []) if window.get("visible") is True
            for node in window.get("nodes", []) if node.get("insideWebContent") is True and
            (not visible or probe.node_visible(snapshot, node))]


def matches(snapshot, label, kind, *, visible=True, enabled=True):
    found = [node for node in nodes(snapshot, visible=visible) if node.get("role") in KINDS[kind] and
             (enabled is None or node.get("enabled") is enabled) and node.get("name") == label]
    return unique_nodes(snapshot, found)


def unique_nodes(snapshot, found):
    if snapshot.get("backend") != "windows-uia":
        return found
    unique = {}
    for node in found:
        runtime_id = node.get("runtimeId")
        probe.require(probe.valid_runtime_id(runtime_id), "InvalidObservedRuntimeId")
        key = tuple(runtime_id)
        previous = unique.get(key)
        if previous is not None:
            # WebView2 can expose one element through two RawView paths. Only
            # the provider's exact opaque identity permits alias collapsing;
            # equal labels or geometry never establish that identity.
            probe.require(all(previous.get(field) == node.get(field) for field in
                              ("role", "name", "identifier", "password", "enabled", "visible", "editableAncestor",
                               "valueSettable", "scrollToVisible")),
                          "InconsistentRuntimeIdObservation")
            if (len(node["path"]), node["path"]) < (len(previous["path"]), previous["path"]):
                unique[key] = node
        else:
            unique[key] = node
    return list(unique.values())


def has_text(snapshot, text):
    return any(node.get("name") == text or node.get("text") == text for node in nodes(snapshot))


def select_node(snapshot, found, kind, *, operation=None):
    probe.require(len(found) == 1, "MissingOrAmbiguousNativeControl")
    node = found[0]
    probe.require(not node.get("password"), "SecureControlNotAllowed")
    probe.require(not node.get("editableAncestor"), "EditableDescendantNotAllowed")
    path = node.get("path")
    probe.require(isinstance(path, list) and 2 <= len(path) <= 42 and
                  all(type(index) is int and 0 <= index < 1000 for index in path), "InvalidObservedControlPath")
    selected = {key: node.get(key, "") for key in ("path", "role", "name", "identifier", "runtimeId")}
    if operation:
        selected["operation"] = operation
    elif snapshot.get("backend") == "windows-uia" and kind in ("scope", "checkbox"):
        probe.require("TogglePatternIdentifiers.Pattern" in node.get("actions", []), "NativeScopeToggleUnavailable")
        selected["operation"] = "toggle"
    return selected


def one(snapshot, label, kind, *, operation=None):
    return select_node(snapshot, matches(snapshot, label, kind, visible=operation != "scroll"), kind,
                       operation=operation)


def descendant(path, parent):
    return isinstance(path, list) and isinstance(parent, list) and len(path) > len(parent) and path[:len(parent)] == parent


def resource_card(snapshot, title):
    # The card's accessible name includes source and availability. Its exact
    # heading identifies the card without guessing concatenation or clicking text.
    observed = nodes(snapshot)
    headings = [node for node in observed if node.get("role") in
                {"AXHeading", "AXStaticText", "ControlType.Text"} and
                (node.get("name") == title or node.get("text") == title)]
    cards = unique_nodes(snapshot, [node for node in observed if node.get("role") in KINDS["button"] and
        node.get("enabled") is True and any(descendant(child.get("path"), node.get("path")) for child in headings)])
    return select_node(snapshot, cards, "button")


def perform(app, snapshot, selector, operation="press", value=None):
    probe.hosted(app)
    probe.require(complete(snapshot), "IncompleteNativeTree")
    probe.require(operation in ("press", "set", "toggle", "scroll"), "UnsupportedNativeOperation")
    if operation == "toggle":
        probe.require(app["rid"] == "win-x64" and selector.get("operation") == "toggle" and
                      selector.get("role") in ("ControlType.Button", "ControlType.CheckBox"), "UnsupportedNativeToggle")
    identity = snapshot["process"]
    pid = identity["pid"]
    record = {"pid": pid, "executable": str(Path(app["exe"]).resolve()), "started": identity["started"],
              "selector": selector, "operation": operation}
    if operation == "set":
        probe.require(isinstance(value, str) and len(value) <= 4096, "InvalidNativeInput")
        record["value"] = value
    if app["rid"].startswith("osx-"):
        backend = app.get("nativeBackend", "macos-system-events-ax")
        probe.require(snapshot.get("backend") == backend, "NativeActionProviderMismatch")
        if backend == "macos-direct-ax":
            probe.require(operation in ("press", "set", "scroll"), "UnsupportedDirectAXOperation")
            record.update(maxNodes=1000, maxDepth=40)
            result = probe.direct_action(app, snapshot, record, timeout=15)
        else:
            probe.require(backend == "macos-system-events-ax", "InvalidNativeBackend")
            probe.require(probe.mac_identity(pid) == identity, "ProductProcessChangedBeforeAction")
            source = (HERE / "macos-action.js").read_text()
            script = "const input = " + json.dumps(record) + ";\n" + source
            result = probe.bounded_command(["/usr/bin/osascript", "-l", "JavaScript", "-"], script, 15)
            probe.require(probe.mac_identity(pid) == identity, "ProductProcessChangedAfterAction")
    else:
        result = probe.bounded_command(["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-STA",
                                        "-ExecutionPolicy", "Bypass", "-File", HERE / "windows-action.ps1"],
                                       json.dumps(record) + "\n", 15)
    if result.get("performed") is not True:
        stage = result.get("errorStage")
        diagnostic = probe.ax_diagnostic(result.get("diagnostic"))
        detail = ":" + diagnostic["code"] + ":" + str(diagnostic["axError"]) if diagnostic else ""
        raise probe.ProbeFailure("NativeActionFailed:" + (stage if stage in ACTION_STAGES else "unknown") + detail)
    probe.require(result.get("operation") == operation, "UnexpectedNativeActionResult")


class Driver:
    def __init__(self, app, pid, directory, *, deadline=None, scope="installed-native-empty-library"):
        probe.hosted(app)
        self.app, self.pid, self.directory = app, pid, Path(directory)
        probe.require(not self.directory.exists(), "FlowResultsAlreadyExist")
        self.directory.mkdir(parents=True)
        self.deadline = min(time.monotonic() + 600, deadline) if deadline is not None else time.monotonic() + 600
        self.identity, self.current = None, None
        self.report = {"scope": scope, "emptyLibraryFlowPassed": False,
                       "nativeBackend": app.get("nativeBackend", "windows-uia" if app.get("rid") == "win-x64" else "macos-system-events-ax"),
                       "mainFlowPassed": False, "actions": [], "observations": [], "checkpoints": []}
        self.save()

    def save(self):
        (self.directory / "report.json").write_text(json.dumps(self.report, indent=2), encoding="utf-8")

    def read(self, step, deadline=None):
        deadline = min(self.deadline, deadline if deadline is not None else self.deadline)
        probe.require(time.monotonic() < deadline-7, "NativeFlowDeadlineExceeded")
        probe.require(len(self.report["observations"]) < 40, "NativeObservationBudgetExceeded")
        view = probe.native_snapshot(self.app, self.pid, timeout=min(probe.TIMEOUT, deadline-time.monotonic()-6))
        identity = view.get("process")
        if self.identity is None:
            self.identity = identity
        probe.require(identity == self.identity and identity is not None, "ProductProcessChangedDuringFlow")
        self.current = view
        index = len(self.report["observations"])+1
        filename = f"{index:02}-{step}.json"
        (self.directory / filename).write_text(json.dumps(probe.sanitize(view), indent=2), encoding="utf-8")
        self.report["observations"].append({"step": step, "tree": filename, **probe.summarize(view)})
        self.save()
        return view

    def wait(self, step, condition):
        deadline = min(self.deadline, time.monotonic()+90)
        for _ in range(8):
            view = self.read(step, deadline)
            # A partial tree cannot establish uniqueness or the absence of a
            # dialog. Keep the observation, then re-read within this deadline.
            if complete(view) and condition(view):
                self.report["checkpoints"].append({"step": step, "passed": True})
                self.save()
                return view
            probe.require(time.monotonic() < deadline-7, "NativeExpectedStateUnavailable:" + step)
            time.sleep(1)
        raise probe.ProbeFailure("NativeExpectedStateUnavailable:" + step)

    def press(self, label, kind="button"):
        selector = one(self.current, label, kind)
        operation = selector.get("operation", "press")
        self.act(selector, operation, label, kind)

    def act(self, selector, operation, label, kind, value=None):
        probe.require(time.monotonic() < self.deadline-16, "NativeFlowDeadlineExceeded")
        probe.require(len(self.report["actions"]) < 20, "NativeActionBudgetExceeded")
        action = {"operation": operation, "label": label, "kind": kind, "submitted": False}
        self.report["actions"].append(action)
        self.save()
        if value is None:
            perform(self.app, self.current, selector, operation)
        else:
            perform(self.app, self.current, selector, operation, value)
        action["submitted"] = True
        self.save()

    def set(self, label, value):
        self.act(one(self.current, label, "input"), "set", label, "input", value)

    def reveal(self, label, kind="button", *, enabled=True):
        self.wait("find-" + kind, lambda view: len(matches(view, label, kind, visible=False, enabled=enabled)) == 1)
        if len(matches(self.current, label, kind, enabled=enabled)) == 1:
            return
        found = matches(self.current, label, kind, visible=False, enabled=enabled)
        probe.require(found[0].get("scrollToVisible") is True, "NativeScrollUnavailable")
        self.act(select_node(self.current, found, kind, operation="scroll"), "scroll", label, kind)
        self.wait("revealed-" + kind, lambda view: len(matches(view, label, kind, enabled=enabled)) == 1)

    def open_resource(self, title):
        self.act(resource_card(self.current, title), "press", title, "resource-card")

    def reveal_text(self, label):
        self.reveal(label, "text", enabled=None)
        probe.require(has_text(self.current, label), "NativeTextNotVisibleAfterReveal")


def status(app, *, deadline=None):
    # A read-only cross-check of persisted outcome, never a substitute for the
    # native visible-state assertions and never a way to change the setting.
    deadline = min(time.monotonic() + 3, deadline) if deadline is not None else time.monotonic() + 3
    def remaining():
        value = deadline - time.monotonic()
        probe.require(value > 0, "NativeFlowDeadlineExceeded")
        return value
    connection = http.client.HTTPConnection("127.0.0.1", app["port"], timeout=remaining())
    try:
        connection.request("GET", "/federation/local/peers")
        active_socket = connection.sock
        probe.require(active_socket is not None, "NativeStateCrossCheckFailed")
        active_socket.settimeout(remaining())
        response = connection.getresponse()
        probe.require(response.status == 200, "NativeStateCrossCheckFailed")
        body = bytearray()
        while True:
            active_socket.settimeout(remaining())
            block = response.read1(min(65536, 1024*1024 + 1 - len(body)))
            if not block:
                break
            body.extend(block)
            probe.require(len(body) <= 1024*1024, "NativeStateCrossCheckFailed")
            if response.isclosed():
                break
        remaining()
        return json.loads(body)
    finally:
        connection.close()


def empty_library(driver, status_reader=status):
    driver.wait("initial-native-ui", lambda view: probe.summarize(view)["capabilityPassed"])
    for _ in range(3):
        view = driver.current
        if matches(view, "Accept and start to use", "button"):
            driver.press("Accept and start to use")
            driver.wait("after-native-terms", lambda current: not matches(current, "Accept and start to use", "button"))
        elif has_text(view, "Help Center") and len(matches(view, "Close", "button")) == 1:
            driver.press("Close")
            # The permanent navigation Help Center button remains visible.
            driver.wait("help-dismissed", lambda current: not matches(current, "Close", "button") and
                        len(matches(current, "Multi-device library", "menu")) == 1)
        else:
            break
    driver.wait("local-menu", lambda view: len(matches(view, "Multi-device library", "menu")) == 1)
    driver.press("Multi-device library", "menu")
    driver.wait("browsing-default-off", lambda view: has_text(view, "Browsing is off") and
                len(matches(view, "Enable browsing", "button")) == 1)
    before = status_reader(driver.app)
    probe.require(before.get("browsingEnabled") is False and before.get("sharingEnabled") is False and
                  before.get("peers") == [], "UnexpectedInitialFederationState")
    driver.press("Devices and sharing", "menu")
    driver.wait("device-settings-off", lambda view: has_text(view, "Browse libraries on this device") and
                has_text(view, "Browsing is off") and len(matches(view, "Enable browsing", "button")) == 1)
    driver.press("Enable browsing")
    driver.wait("device-settings-enabled", lambda view: has_text(view, "Browsing enabled") and
                len(matches(view, "Turn browsing off", "button")) == 1)
    enabled = status_reader(driver.app)
    probe.require(enabled.get("browsingEnabled") is True and enabled.get("sharingEnabled") is False and
                  enabled.get("peers") == [], "NativeBrowsingDidNotPersistIndependently")
    driver.press("Multi-device library", "menu")
    driver.wait("search-scopes-visible", lambda view: all(len(matches(view, name, "scope")) == 1
                for name in ("This device", "All enabled devices", "Choose devices")) and
                len(matches(view, "Search", "button")) == 1)
    driver.press("This device", "scope")
    driver.wait("local-source-selected", lambda view: len(matches(view, "Search", "button")) == 1)
    driver.press("Search")
    driver.wait("empty-library-results", lambda view: has_text(view, "No matching resources") and
                has_text(view, "0 resources · 1/1 devices searched") and has_text(view, "Read-only view"))
    # Navigate away and back to prove the state survives normal native routing.
    driver.press("Devices and sharing", "menu")
    driver.wait("saved-browsing-state", lambda view: has_text(view, "Browsing enabled") and
                len(matches(view, "Turn browsing off", "button")) == 1)
    driver.report.update(emptyLibraryFlowPassed=True,
        stateCrossChecks={"initialBrowsing": False, "savedBrowsing": True, "sharingStayedOff": True, "peersStayedEmpty": True},
        remaining=["native-pairing", "remote-query-and-detail", "offline-recovery"])
    driver.save()
    return driver.report


def run_empty_library(app, pid, directory):
    driver = Driver(app, pid, directory)
    try:
        return empty_library(driver)
    except Exception as error:
        driver.report["error"] = {"type": type(error).__name__,
                                  "code": str(error) if isinstance(error, probe.ProbeFailure) else "NativeFlowUnavailable"}
        driver.save()
        raise
