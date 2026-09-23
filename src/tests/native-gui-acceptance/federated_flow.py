#!/usr/bin/env python3
"""Native pairing, remote detail and actual source stop/restart on hosted CI.

One installed reader talks to the explicitly named production Shell/Service
source fixture. HTTP writes prepare three resources only; every user workflow
operation goes through the native accessibility provider.
"""
import importlib.util
import json
from pathlib import Path
import time

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("federated_native_driver", HERE / "flow.py")
flow = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(flow)
require = flow.probe.require
SCOPE = "installed-native-reader-with-production-shell-service-source-fixture"
REMOTE_SETTING = ("Also set remote access to Enabled and require pairing for existing interfaces. "
                  "This changes this device’s remote-access settings.")
READ_ONLY = ("These values were resolved by the source device. No metadata or playback history "
             "is written back from this view.")
PHASES = ("source-pairing", "reader-pairing", "reader-query", "reader-recovery")


def initial_ui(driver):
    driver.wait("initial-native-ui", lambda view: flow.probe.summarize(view)["capabilityPassed"])
    for _ in range(3):
        if flow.matches(driver.current, "Accept and start to use", "button"):
            driver.press("Accept and start to use")
            driver.wait("terms-accepted", lambda view: not flow.matches(view, "Accept and start to use", "button"))
        elif flow.has_text(driver.current, "Help Center") and len(flow.matches(driver.current, "Close", "button")) == 1:
            driver.press("Close")
            driver.wait("help-dismissed", lambda view: not flow.matches(view, "Close", "button"))
        else:
            break
    driver.wait("devices-menu", lambda view: len(flow.matches(view, "Devices and sharing", "menu")) == 1)


def press(driver, label, kind="button"):
    driver.reveal(label, kind)
    driver.press(label, kind)


def confirm(driver):
    # Only the exact single native confirmation can be used. The page's primary
    # buttons have distinct labels; an ambiguous second modal fails the gate.
    driver.wait("confirmation", lambda view: len(flow.matches(view, "Confirm", "button")) == 1 and
                len(flow.matches(view, "Cancel", "button")) == 1)
    driver.press("Confirm")
    driver.wait("confirmation-dismissed", lambda view: not flow.matches(view, "Confirm", "button"))


def initial_state(state):
    require(isinstance(state.get("identity"), dict) and state["identity"].get("nodeId") and
            state["identity"].get("libraryEpoch") and state.get("peers") == [] and
            state.get("sharingEnabled") is False and state.get("browsingEnabled") is False,
            "NativeFederationDefaultStateChanged")
    return dict(state["identity"])


def paired_state(reader, source, reader_identity, source_identity, *, browsing=False):
    require(reader.get("identity") == reader_identity and source.get("identity") == source_identity and
            reader_identity["nodeId"] != source_identity["nodeId"], "NativePairingChangedIdentity")
    require(reader.get("sharingEnabled") is False and reader.get("browsingEnabled") is browsing and
            source.get("sharingEnabled") is True and source.get("browsingEnabled") is False,
            "NativePairingChangedIndependentSettings")
    outbound = [p for p in reader.get("peers", []) if p.get("nodeId") == source_identity["nodeId"]]
    inbound = [p for p in source.get("peers", []) if p.get("nodeId") == reader_identity["nodeId"]]
    require(len(reader.get("peers", [])) == len(outbound) == 1 and
            len(source.get("peers", [])) == len(inbound) == 1 and
            bool(outbound[0].get("outboundGrant")) and not outbound[0].get("inboundGrant") and
            bool(inbound[0].get("inboundGrant")) and not inbound[0].get("outboundGrant") and
            outbound[0].get("enabled") is True and outbound[0].get("pathMappings") == [],
            "NativePairingNotDirectionalReadOnly")
    grant = outbound[0]["outboundGrant"]
    require(isinstance(grant, dict) and isinstance(grant.get("grantId"), str) and bool(grant["grantId"]) and
            type(grant.get("revision")) is int and grant["revision"] > 0 and grant == inbound[0]["inboundGrant"],
            "NativePairingGrantDoesNotMatch")


def open_detail(driver, seed):
    title = seed["detailTitle"]
    driver.wait("remote-card", lambda view: flow.has_text(view, title))
    driver.open_resource(title)
    driver.reveal("Close details")
    driver.wait("remote-detail", lambda view: flow.has_text(view, title) and flow.has_text(view, "Read-only view") and
                flow.has_text(view, READ_ONLY) and len(flow.matches(view, "Close details", "button")) == 1)
    driver.reveal("Open containing folder on this device", enabled=False)
    driver.wait("remote-detail-read-only", lambda view:
                len(flow.matches(view, "Open containing folder on this device", "button", enabled=False)) == 1 and
                not flow.matches(view, "Manage in the local library", "link", visible=False, enabled=None))
    # The exact static value is the semantic scroll target; only a freshly
    # visible native value, never offscreen text or an HTTP result, is proof.
    driver.reveal_text(seed["detailIntroduction"])
    driver.wait("source-property", lambda view: flow.has_text(view, seed["detailIntroduction"]))


def exercise(reader_app, reader_pid, fixture, started, seed, directory, *, status_reader=flow.status,
             driver_factory=flow.Driver):
    directory = Path(directory)
    require(not directory.exists(), "FederatedFlowResultsAlreadyExist")
    directory.mkdir(parents=True)
    # Four named phases share ONE 600s wall-clock deadline. Each keeps the
    # existing 20-action / 40-observation cap, 30s read and 90s wait bounds.
    deadline = time.monotonic() + 600
    report = {"scope": SCOPE, "workflowStepsPassed": False, "mainFlowPassed": False,
              "phaseOrder": list(PHASES), "phases": {}, "checkpoints": {},
              "budget": {"totalSeconds": 600, "actionsPerPhase": 20, "observationsPerPhase": 40},
              "limitations": ["The source is a test entry point using production Shell and Service, not a second installed package.",
                              "Loopback on one hosted machine does not certify separate physical devices."]}
    def save():
        (directory / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    def phase(name, app, pid):
        require(name in PHASES and name not in report["phases"], "NativeFlowPhaseRepeated")
        driver = driver_factory(app, pid, directory / name, deadline=deadline, scope=SCOPE + "/" + name)
        report["phases"][name] = driver.report
        return driver
    def checkpoint(name):
        report["checkpoints"][name] = True
        save()
    save()
    try:
        reader_identity = initial_state(status_reader(reader_app, deadline=deadline))
        source_identity = initial_state(fixture.status(deadline=deadline))
        source = phase("source-pairing", started["app"], started["pid"])
        initial_ui(source)
        if seed is None:
            seed = fixture.seed(deadline=deadline)
        require(seed.get("passed") is True and len(seed.get("titles", [])) == 3 and
                len(set(seed["titles"])) == 3 and seed.get("detailTitle") in seed["titles"] and
                bool(seed.get("detailIntroduction")), "NativeSourceFixtureSeedIncomplete")
        report["resourceFixture"] = {key: seed[key] for key in ("scope", "resourceIds", "titles", "detailTitle")}
        press(source, "Devices and sharing", "menu")
        source.wait("sharing-default-off", lambda view: flow.has_text(view, "Sharing disabled"))
        press(source, REMOTE_SETTING, "checkbox")
        press(source, "Enable sharing")
        confirm(source)
        source.wait("sharing-enabled", lambda view: flow.has_text(view, "Sharing enabled"))

        reader = phase("reader-pairing", reader_app, reader_pid)
        initial_ui(reader)
        press(reader, "Devices and sharing", "menu")
        reader.reveal("Device address", "input")
        reader.set("Device address", "http://127.0.0.1:" + str(started["port"]))
        press(reader, "Request access")
        reader.wait("awaiting-owner", lambda view: flow.has_text(view, "Waiting for the other device to approve."))
        require(not any(p.get("outboundGrant") for p in status_reader(reader_app, deadline=deadline)["peers"]),
                "NativeRequestAlreadyAuthorized")
        press(source, "Refresh")
        press(source, "Approve read-only access")
        confirm(source)
        # The source window may have hidden the reader, pausing its poll timer.
        # Use the normal native Check approval control when auto-claim has not
        # already completed; an API claim would bypass the promised user flow.
        reader.wait("approval-state", lambda view: flow.has_text(view, "Read-only access granted.") or
                    len(flow.matches(view, "Check approval", "button", visible=False)) == 1)
        if not flow.has_text(reader.current, "Read-only access granted."):
            press(reader, "Check approval")
        reader.wait("access-granted", lambda view: flow.has_text(view, "Read-only access granted."))
        paired_state(status_reader(reader_app, deadline=deadline), fixture.status(deadline=deadline), reader_identity, source_identity)
        checkpoint("native-pairing")
        press(reader, "Enable browsing")
        reader.wait("browsing-enabled", lambda view: flow.has_text(view, "Browsing enabled"))
        paired_state(status_reader(reader_app, deadline=deadline), fixture.status(deadline=deadline), reader_identity, source_identity, browsing=True)
        checkpoint("independent-browsing-and-sharing")

        query = phase("reader-query", reader_app, reader_pid)
        query.wait("reader-unchanged", lambda view: view["process"] == reader.identity)
        press(query, "Multi-device library", "menu")
        press(query, "All enabled devices", "scope")
        press(query, "Search")
        query.wait("both-libraries", lambda view: flow.has_text(view, "3 resources · 2/2 devices searched"))
        for title in seed["titles"]:
            query.reveal_text(title)
        query.reveal("Name search", "input")
        query.set("Name search", seed["detailTitle"])
        press(query, "Search")
        query.wait("filtered-remote-resource", lambda view: flow.has_text(view, "1 resources · 2/2 devices searched") and
                   flow.has_text(view, seed["detailTitle"]))
        open_detail(query, seed)
        report["beforeOfflineReadOnly"] = fixture.read_only_baseline(deadline=deadline)
        require(report["beforeOfflineReadOnly"].get("passed") is True and
                report["beforeOfflineReadOnly"].get("noPlayedAtWrite") is True, "NativeRemoteDetailChangedSource")
        checkpoint("remote-query-and-detail")
        press(query, "Close details")
        query.wait("detail-closed", lambda view: not flow.matches(view, "Close details", "button"))

        recovery = phase("reader-recovery", reader_app, reader_pid)
        recovery.wait("reader-before-offline", lambda view: view["process"] == reader.identity)
        stopped = fixture.stop(deadline=deadline)
        require(stopped.get("passed") is True and stopped.get("remainingProcesses") == [], "NativeSourceDidNotStop")
        report["sourceStopped"] = stopped
        press(recovery, "Search")
        recovery.wait("source-offline-partial", lambda view:
                      flow.has_text(view, "0 resources found · partial coverage (1/2 devices)") and
                      flow.has_text(view, "No matches on the responding devices") and
                      not flow.has_text(view, "No matching resources") and
                      not flow.has_text(view, "0 resources · 2/2 devices searched"))
        checkpoint("offline-is-partial")
        restarted = fixture.restart(deadline=deadline)
        require(restarted.get("passed") is True and restarted.get("identityAndGrantsRetained") is True and
                restarted["pid"] != started["pid"] and restarted["port"] == started["port"],
                "NativeSourceRestartNotVerified")
        report["sourceRestarted"] = {key: restarted[key] for key in
                                     ("pid", "port", "process", "identityAndGrantsRetained")}
        if reader_app.get("rid", "").startswith("osx-"):
            # A new source process also has a new embedded renderer. Re-prove
            # that source's tree before later cleanup relies on its writer
            # identity; never carry the old process binding across a restart.
            capability = flow.probe.capture(restarted["app"], restarted["pid"], directory / "source-restarted-native-tree",
                                            require_complete=True, deadline=deadline)
            report["restartedSourceNative"] = capability
            require(capability.get("completeTreePassed") is True, "NativeRestartedSourceTreeUnavailable")
        press(recovery, "Search")
        recovery.wait("source-recovered", lambda view: view["process"] == reader.identity and
                      flow.has_text(view, "1 resources · 2/2 devices searched") and
                      flow.has_text(view, seed["detailTitle"]))
        open_detail(recovery, seed)
        paired_state(status_reader(reader_app, deadline=deadline), fixture.status(deadline=deadline), reader_identity, source_identity, browsing=True)
        report["afterRecoveryReadOnly"] = fixture.read_only_baseline(deadline=deadline)
        require(report["afterRecoveryReadOnly"].get("passed") is True and
                report["afterRecoveryReadOnly"].get("noPlayedAtWrite") is True, "NativeRecoveredDetailChangedSource")
        checkpoint("offline-recovery-without-reader-restart-or-repairing")
        require(time.monotonic() < deadline, "NativeFlowDeadlineExceeded")
        report["workflowStepsPassed"] = True
        save()
        return report
    except BaseException as error:
        report["error"] = {"type": type(error).__name__, "code": str(error) if isinstance(error, flow.probe.ProbeFailure)
                           else "NativeFederatedFlowFailed"}
        save()
        raise
