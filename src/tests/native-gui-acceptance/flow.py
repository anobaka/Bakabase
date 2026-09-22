#!/usr/bin/env python3
"""Native AX/UIA controls, never DOM/HTTP mutations, drive the empty-library flow."""
import importlib.util
import json
from pathlib import Path
import time
import urllib.request

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_flow_probe", HERE / "probe.py")
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)
KINDS = {
    "button": {"AXButton", "ControlType.Button"},
    "menu": {"AXMenuItem", "ControlType.MenuItem"},
    "link": {"AXLink", "ControlType.Hyperlink"},
    "input": {"AXTextField", "AXTextArea", "ControlType.Edit"},
}
ACTION_STAGES = {"initialize", "preflight", "resolve-process", "resolve-control", "validate-control", "perform-action"}


def complete(snapshot):
    return snapshot.get("enabled") is True and snapshot.get("truncated") is False


def nodes(snapshot):
    probe.require(complete(snapshot), "IncompleteNativeTree")
    return [node for window in snapshot.get("windows", []) if window.get("visible") is True
            for node in window.get("nodes", []) if node.get("insideWebContent") is True and probe.node_visible(snapshot, node)]


def matches(snapshot, label, kind):
    return [node for node in nodes(snapshot) if node.get("role") in KINDS[kind] and
            node.get("enabled") is True and node.get("name") == label]


def has_text(snapshot, text):
    return any(node.get("name") == text or node.get("text") == text for node in nodes(snapshot))


def one(snapshot, label, kind):
    found = matches(snapshot, label, kind)
    probe.require(len(found) == 1, "MissingOrAmbiguousNativeControl")
    node = found[0]
    probe.require(not node.get("password"), "SecureControlNotAllowed")
    path = node.get("path")
    probe.require(isinstance(path, list) and 2 <= len(path) <= 42 and
                  all(type(index) is int and 0 <= index < 1000 for index in path), "InvalidObservedControlPath")
    return {key: node.get(key, "") for key in ("path", "role", "name", "identifier")}


def perform(app, snapshot, selector, operation="press", value=None):
    probe.hosted(app)
    probe.require(complete(snapshot), "IncompleteNativeTree")
    probe.require(operation in ("press", "set"), "UnsupportedNativeOperation")
    identity = snapshot["process"]
    pid = identity["pid"]
    record = {"pid": pid, "executable": str(Path(app["exe"]).resolve()), "started": identity["started"],
              "selector": selector, "operation": operation}
    if operation == "set":
        probe.require(isinstance(value, str) and len(value) <= 4096, "InvalidNativeInput")
        record["value"] = value
    if app["rid"].startswith("osx-"):
        probe.require(probe.mac_identity(pid) == identity, "ProductProcessChangedBeforeAction")
        script = "const input = " + json.dumps(record) + ";\n" + (HERE / "macos-action.js").read_text()
        result = probe.bounded_command(["/usr/bin/osascript", "-l", "JavaScript", "-"], script, 15)
        probe.require(probe.mac_identity(pid) == identity, "ProductProcessChangedAfterAction")
    else:
        result = probe.bounded_command(["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-STA",
                                        "-ExecutionPolicy", "Bypass", "-File", HERE / "windows-action.ps1"],
                                       json.dumps(record) + "\n", 15)
    if result.get("performed") is not True:
        stage = result.get("errorStage")
        raise probe.ProbeFailure("NativeActionFailed:" + (stage if stage in ACTION_STAGES else "unknown"))
    probe.require(result.get("operation") == operation, "UnexpectedNativeActionResult")


class Driver:
    def __init__(self, app, pid, directory):
        probe.hosted(app)
        self.app, self.pid, self.directory = app, pid, Path(directory)
        probe.require(not self.directory.exists(), "FlowResultsAlreadyExist")
        self.directory.mkdir(parents=True)
        self.deadline = time.monotonic() + 600
        self.identity, self.current = None, None
        self.report = {"scope": "installed-native-empty-library", "emptyLibraryFlowPassed": False,
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
        probe.require(len(self.report["actions"]) < 20, "NativeActionBudgetExceeded")
        selector = one(self.current, label, kind)
        action = {"operation": "press", "label": label, "kind": kind, "submitted": False}
        self.report["actions"].append(action)
        self.save()
        perform(self.app, self.current, selector)
        action["submitted"] = True
        self.save()


def status(app):
    # A read-only cross-check of persisted outcome, never a substitute for the
    # native visible-state assertions and never a way to change the setting.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(f'http://127.0.0.1:{app["port"]}/federation/local/peers', timeout=3) as response:
        probe.require(response.status == 200, "NativeStateCrossCheckFailed")
        return json.loads(response.read(1024*1024))


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
    driver.wait("search-scopes-visible", lambda view: all(len(matches(view, name, "button")) == 1
                for name in ("This device", "All enabled devices", "Choose devices", "Search")))
    driver.press("This device")
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
