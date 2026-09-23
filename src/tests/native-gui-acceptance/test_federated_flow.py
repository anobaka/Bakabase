#!/usr/bin/env python3
"""Synthetic native frames only; these tests never claim actual OS/UI coverage."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("federated_flow_pure", HERE / "federated_flow.py")
full = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(full)
flow = full.flow


READER_ID = {"nodeId": "reader", "libraryEpoch": "reader-epoch", "name": "reader"}
SOURCE_ID = {"nodeId": "source", "libraryEpoch": "source-epoch", "name": "source"}
GRANT = {"grantId": "directional-grant", "revision": 1}
TITLES = ["Native source alpha.txt", "Native source beta", "Native source gamma"]
SEED = {"passed": True, "scope": "production-api-resource-fixture-only", "resourceIds": [1, 2, 3],
        "titles": TITLES, "detailTitle": TITLES[0], "detailIntroduction": "Native GUI source detail sentinel"}
READER_PROCESS = {"pid": 41, "executable": "/synthetic/Bakabase", "started": "reader-start"}
SOURCE_PROCESS = {"pid": 81, "executable": "/synthetic/Bakabase.NativeGui.SourceHost", "started": "source-start"}


def defaults(identity):
    return {"identity": copy.deepcopy(identity), "sharingEnabled": False, "browsingEnabled": False, "peers": [], "requests": []}


def pair(reader, source):
    reader["peers"] = [{"nodeId": "source", "outboundGrant": dict(GRANT), "inboundGrant": None,
                         "enabled": True, "pathMappings": []}]
    source["peers"] = [{"nodeId": "reader", "inboundGrant": dict(GRANT), "outboundGrant": None,
                         "enabled": True, "pathMappings": []}]
    source["sharingEnabled"] = True


def frame(process, *, texts=(), controls=(), card=None):
    nodes = [{"path": [0, 0], "role": "AXWebArea", "name": "Synthetic owned web", "text": "", "enabled": True,
              "visible": True, "insideWebContent": True, "visibilityEvidence": "owned-hit-test", "actions": []}]
    def append(role, name, enabled, path, text=""):
        nodes.append({"path": path, "role": role, "name": name, "text": text, "enabled": enabled, "visible": True,
                      "insideWebContent": True, "visibilityEvidence": "owned-hit-test", "password": False,
                      "editableAncestor": False, "identifier": "", "actions": ["AXPress"] if role != "AXStaticText" else []})
    for index, text in enumerate(texts, 1):
        append("AXStaticText", "", False, [0, 0, index], text)
    for index, item in enumerate(controls, 100):
        kind, label, enabled = item if len(item) == 3 else (*item, True)
        role = {"button": "AXButton", "menu": "AXMenuItem", "link": "AXLink", "input": "AXTextField",
                "scope": "AXCheckBox", "checkbox": "AXCheckBox", "region": "AXGroup"}[kind]
        append(role, label, enabled, [0, 0, index])
    if card:
        append("AXButton", "Synthetic source " + card, True, [0, 0, 300])
        append("AXStaticText", "", False, [0, 0, 300, 0], card)
    return {"backend": "macos-direct-ax", "enabled": True, "truncated": False, "readOnly": True,
            "process": copy.deepcopy(process), "windows": [{"index": 0, "visible": True, "nodes": nodes}]}


class Scenario:
    """A fixed synthetic UI transcript, never an HTTP or native implementation."""
    def __init__(self):
        self.reader, self.source = defaults(READER_ID), defaults(SOURCE_ID)
        self.reader_app, self.source_app = {"role": "unified", "port": 12001}, {"role": "source-fixture", "port": 12002}
        self.started = {"passed": True, "app": self.source_app, "pid": 81, "port": 12002, "process": dict(SOURCE_PROCESS)}
        self.actions, self.lifecycle, self.drivers = [], [], []
        self.pending, self.approved, self.online = None, False, True
        self.frame_change = lambda _phase, _step, view: view
        self.baseline_result = {"passed": True, "noPlayedAtWrite": True}
        self.stop_result = {"passed": True, "remainingProcesses": []}
        self.restart_result = {"passed": True, "identityAndGrantsRetained": True, "pid": 82, "port": 12002,
                               "process": {"pid": 82, "executable": SOURCE_PROCESS["executable"], "started": "new-source-start"}}
        self.fixture = self.Fixture(self)

    class Fixture:
        def __init__(self, scenario):
            self.owner = scenario
        def status(self, *, deadline):
            return copy.deepcopy(self.owner.source)
        def seed(self, *, deadline):
            self.owner.lifecycle.append("seed")
            return copy.deepcopy(SEED)
        def read_only_baseline(self, *, deadline):
            self.owner.lifecycle.append("baseline")
            return copy.deepcopy(self.owner.baseline_result)
        def stop(self, *, deadline):
            self.owner.lifecycle.append("stop")
            self.owner.online = False
            return copy.deepcopy(self.owner.stop_result)
        def restart(self, *, deadline):
            if not self.owner.lifecycle or self.owner.online:
                raise AssertionError("Synthetic restart requires a real preceding stop in the transcript")
            self.owner.lifecycle.append("restart")
            self.owner.online = True
            return copy.deepcopy(self.owner.restart_result)

    def status(self, _app, *, deadline):
        return copy.deepcopy(self.reader)

    def driver(self, app, pid, directory, *, deadline, scope):
        scenario = self
        class SyntheticDriver:
            def __init__(self):
                self.app, self.pid = app, pid
                self.phase = Path(directory).name
                self.identity, self.current = None, None
                self.report = {"scope": scope, "mainFlowPassed": False, "actions": [], "checkpoints": []}
                self.deadline = deadline
            def read_frame(self, step, value):
                value = scenario.frame_change(self.phase, step, value)
                flow.probe.require(flow.complete(value), "IncompleteNativeTree")
                if self.identity is None:
                    self.identity = value["process"]
                flow.probe.require(self.identity == value["process"], "ProductProcessChangedDuringFlow")
                self.current = value
                return value
            def wait(self, step, condition):
                value = self.read_frame(step, scenario.observation(self.phase, step))
                flow.probe.require(condition(value), "NativeExpectedStateUnavailable:" + step)
                self.report["checkpoints"].append(step)
                return value
            def reveal(self, label, kind="button", *, enabled=True):
                process = SOURCE_PROCESS if self.phase == "source-pairing" else READER_PROCESS
                self.read_frame("reveal:" + label, frame(process, controls=[(kind, label, enabled)]))
                flow.probe.require(len(flow.matches(self.current, label, kind, enabled=enabled)) == 1, "SyntheticRevealMissingControl")
            def reveal_text(self, text):
                self.read_frame("reveal-text:" + text, frame(READER_PROCESS, texts=[text]))
            def press(self, label, kind="button"):
                flow.one(self.current, label, kind)
                scenario.actions.append((self.phase, "press", label))
                self.report["actions"].append({"operation": "press", "label": label, "submitted": True})
                if self.phase == "source-pairing":
                    if label == "Enable sharing": scenario.pending = "enable"
                    elif label == "Approve read-only access": scenario.pending = "approve"
                    elif label == "Confirm" and scenario.pending == "enable": scenario.source["sharingEnabled"] = True
                    elif label == "Confirm" and scenario.pending == "approve": scenario.approved = True
                elif label == "Check approval":
                    if not scenario.approved: raise AssertionError("Synthetic approval not made through source controls")
                    pair(scenario.reader, scenario.source)
                elif label == "Enable browsing": scenario.reader["browsingEnabled"] = True
            def set(self, label, value):
                flow.one(self.current, label, "input")
                scenario.actions.append((self.phase, "set", label))
                self.report["actions"].append({"operation": "set", "label": label, "submitted": True})
                if label == "Device address":
                    if value != "http://127.0.0.1:12002": raise AssertionError("Wrong synthetic source address")
            def open_resource(self, title):
                flow.resource_card(self.current, title)
                scenario.actions.append((self.phase, "open-resource", title))
                self.report["actions"].append({"operation": "press", "label": title, "submitted": True})
        driver = SyntheticDriver()
        self.drivers.append(driver)
        return driver

    def observation(self, phase, step):
        process = SOURCE_PROCESS if phase == "source-pairing" else READER_PROCESS
        if step in ("initial-native-ui", "devices-menu", "reader-unchanged", "reader-before-offline", "confirmation-dismissed", "detail-closed"):
            return frame(process, controls=[("menu", "Devices and sharing"), ("menu", "Multi-device library")])
        if step == "confirmation": return frame(process, controls=[("button", "Confirm"), ("button", "Cancel")])
        texts = {
            "sharing-default-off": ["Sharing disabled"], "sharing-enabled": ["Sharing enabled"],
            "awaiting-owner": ["Waiting for the other device to approve."],
            "access-granted": ["Read-only access granted."], "browsing-enabled": ["Browsing enabled"],
            "both-libraries": ["3 resources · 2/2 devices searched", *TITLES],
            "filtered-remote-resource": ["1 resources · 2/2 devices searched", TITLES[0]],
            "source-property": [SEED["detailIntroduction"]],
            "source-offline-partial": ["0 resources found · partial coverage (1/2 devices)", "No matches on the responding devices"],
            "source-recovered": ["1 resources · 2/2 devices searched", TITLES[0]],
        }
        if step == "approval-state": return frame(process, controls=[("button", "Check approval")])
        if step == "remote-card": return frame(process, card=TITLES[0])
        if step == "remote-detail": return frame(process, texts=[TITLES[0], "Read-only view", full.READ_ONLY], controls=[("button", "Close details")])
        if step == "remote-detail-read-only": return frame(process, controls=[("button", "Open containing folder on this device", False)])
        if step == "source-offline-partial" and self.online: raise AssertionError("Source was not stopped")
        if step == "source-recovered" and not self.online: raise AssertionError("Source was not restarted")
        if step not in texts: raise AssertionError("Undefined fixed synthetic frame: " + step)
        return frame(process, texts=texts[step])

    def run(self, directory):
        return full.exercise(self.reader_app, 41, self.fixture, self.started, None, directory,
                             status_reader=self.status, driver_factory=self.driver)


class FlowContracts(unittest.TestCase):
    def test_defaults_do_not_allow_prepaired_or_already_enabled_fixture(self):
        for change in ({"sharingEnabled": True}, {"browsingEnabled": True}, {"peers": [{"nodeId": "source"}]}):
            with self.subTest(change=change), self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeFederationDefaultStateChanged"):
                full.initial_state(dict(defaults(READER_ID), **change))

    def test_directional_grants_must_match_and_cannot_be_reciprocal(self):
        for alteration in ("different-grant", "different-revision", "reader-inbound", "source-outbound", "implicit-mapping", "extra-peer"):
            reader, source = defaults(READER_ID), defaults(SOURCE_ID)
            pair(reader, source)
            if alteration == "different-grant": source["peers"][0]["inboundGrant"]["grantId"] = "another"
            elif alteration == "different-revision": source["peers"][0]["inboundGrant"]["revision"] = 2
            elif alteration == "reader-inbound": reader["peers"][0]["inboundGrant"] = dict(GRANT)
            elif alteration == "source-outbound": source["peers"][0]["outboundGrant"] = dict(GRANT)
            elif alteration == "implicit-mapping": reader["peers"][0]["pathMappings"] = [{"sourceRootId": "one", "localPath": "/synthetic"}]
            else: reader["peers"].append({"nodeId": "unrelated"})
            with self.subTest(alteration=alteration), self.assertRaises(flow.probe.ProbeFailure):
                full.paired_state(reader, source, READER_ID, SOURCE_ID)

    def test_pairing_does_not_implicitly_enable_browsing_or_reader_sharing(self):
        for role, key in (("reader", "browsingEnabled"), ("reader", "sharingEnabled"), ("source", "browsingEnabled")):
            reader, source = defaults(READER_ID), defaults(SOURCE_ID)
            pair(reader, source)
            (reader if role == "reader" else source)[key] = True
            with self.subTest(role=role, key=key), self.assertRaisesRegex(flow.probe.ProbeFailure, "NativePairingChangedIndependentSettings"):
                full.paired_state(reader, source, READER_ID, SOURCE_ID)

    def test_complete_synthetic_workflow_still_does_not_certify_cleanup_or_native_main_flow(self):
        scenario = Scenario()
        with tempfile.TemporaryDirectory() as temporary:
            report = scenario.run(Path(temporary) / "flow")
        self.assertTrue(report["workflowStepsPassed"])
        self.assertFalse(report["mainFlowPassed"])
        self.assertEqual(["seed", "baseline", "stop", "restart", "baseline"], scenario.lifecycle)
        for action in ("Request access", "Approve read-only access", "Check approval", "Enable browsing", "Search"):
            self.assertIn(action, [label for _, _, label in scenario.actions])
        self.assertEqual(2, len([a for a in scenario.actions if a[1] == "open-resource"]))
        self.assertEqual(1, len({driver.deadline for driver in scenario.drivers}))
        self.assertTrue(all(not driver.report["mainFlowPassed"] for driver in scenario.drivers))

    def test_all_fixture_reads_and_restart_probe_share_the_driver_deadline(self):
        scenario = Scenario()
        scenario.reader_app["rid"] = "osx-arm64"
        scenario.restart_result["app"] = scenario.source_app
        with contextlib.ExitStack() as stack:
            temporary = stack.enter_context(tempfile.TemporaryDirectory())
            stack.enter_context(patch.object(full.time, "monotonic", return_value=1000.0))
            operations = {
                name: stack.enter_context(patch.object(scenario.fixture, name, wraps=getattr(scenario.fixture, name)))
                for name in ("status", "seed", "read_only_baseline", "stop", "restart")
            }
            reader_status = stack.enter_context(patch.object(scenario, "status", wraps=scenario.status))
            restarted_probe = stack.enter_context(patch.object(full.flow.probe, "capture",
                return_value={"completeTreePassed": True}))
            report = scenario.run(Path(temporary) / "flow")
        self.assertTrue(report["workflowStepsPassed"])
        self.assertEqual(4, len(scenario.drivers))
        self.assertEqual({1600.0}, {driver.deadline for driver in scenario.drivers})
        for name, operation in {**operations, "reader-status": reader_status, "restart-probe": restarted_probe}.items():
            with self.subTest(operation=name):
                self.assertGreater(operation.call_count, 0)
                self.assertTrue(all(call.kwargs.get("deadline") == 1600.0 for call in operation.call_args_list))
        restarted_probe.assert_called_once()
        self.assertTrue(restarted_probe.call_args.kwargs["require_complete"])

    def test_final_baseline_crossing_total_deadline_cannot_pass(self):
        scenario = Scenario()
        clock, calls = [0.0], [0]
        baseline = scenario.fixture.read_only_baseline
        def finish_baseline(*, deadline):
            value = baseline(deadline=deadline)
            calls[0] += 1
            if calls[0] == 2:
                clock[0] = deadline + 1
            return value
        with tempfile.TemporaryDirectory() as temporary, \
                patch.object(full.time, "monotonic", side_effect=lambda: clock[0]), \
                patch.object(scenario.fixture, "read_only_baseline", side_effect=finish_baseline):
            directory = Path(temporary) / "flow"
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeFlowDeadlineExceeded"):
                scenario.run(directory)
            report = json.loads((directory / "report.json").read_text())
        self.assertEqual(2, calls[0])
        self.assertEqual(601.0, clock[0])
        self.assertFalse(report["workflowStepsPassed"])
        self.assertFalse(report["mainFlowPassed"])
        self.assertEqual("NativeFlowDeadlineExceeded", report["error"]["code"])

    def test_absent_or_invisible_native_detail_body_is_not_replaced_by_api_baseline(self):
        for failure in ("missing-body", "invisible-property", "local-management", "open-folder-enabled"):
            scenario = Scenario()
            def change(_phase, step, view):
                nodes = view["windows"][0]["nodes"]
                if failure == "missing-body" and step == "remote-detail":
                    view["windows"][0]["nodes"] = [node for node in nodes if node.get("text") != full.READ_ONLY]
                elif failure == "invisible-property" and step == "source-property":
                    for node in nodes: node["visible"] = False
                elif failure == "local-management" and step == "remote-detail-read-only":
                    bad = frame(READER_PROCESS, controls=[("link", "Manage in the local library")])["windows"][0]["nodes"][1]
                    bad["visible"] = False
                    nodes.append(bad)
                elif failure == "open-folder-enabled" and step == "remote-detail-read-only":
                    for node in nodes: node["enabled"] = True
                return view
            scenario.frame_change = change
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary) / "flow"
                with self.assertRaises(flow.probe.ProbeFailure): scenario.run(directory)
                report = json.loads((directory / "report.json").read_text())
                self.assertFalse(report["workflowStepsPassed"])
                self.assertFalse(report["mainFlowPassed"])

    def test_api_readonly_failure_cannot_be_recorded_as_success(self):
        for result in ({"passed": False, "noPlayedAtWrite": True}, {"passed": True, "noPlayedAtWrite": False}):
            scenario = Scenario()
            scenario.baseline_result = result
            with self.subTest(result=result), tempfile.TemporaryDirectory() as temporary:
                with self.assertRaises(flow.probe.ProbeFailure): scenario.run(Path(temporary) / "flow")

    def test_offline_must_show_partial_coverage_not_complete_empty_or_missing_body(self):
        for texts in (["No matches on the responding devices"],
                      ["0 resources · 2/2 devices searched", "No matching resources"],
                      ["0 resources found · partial coverage (1/2 devices)", "No matching resources"]):
            scenario = Scenario()
            scenario.frame_change = lambda _phase, step, value: frame(READER_PROCESS, texts=texts) if step == "source-offline-partial" else value
            with self.subTest(texts=texts), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary) / "flow"
                with self.assertRaises(flow.probe.ProbeFailure): scenario.run(directory)
                self.assertIn("stop", scenario.lifecycle)
                self.assertNotIn("restart", scenario.lifecycle)
                self.assertFalse(json.loads((directory / "report.json").read_text())["workflowStepsPassed"])

    def test_incomplete_native_tree_cannot_establish_results(self):
        scenario = Scenario()
        def change(_phase, step, value):
            if step == "both-libraries": value["truncated"] = True
            return value
        scenario.frame_change = change
        with tempfile.TemporaryDirectory() as temporary, self.assertRaisesRegex(flow.probe.ProbeFailure, "IncompleteNativeTree"):
            scenario.run(Path(temporary) / "flow")

    def test_recovery_requires_verified_stop_new_source_pid_port_and_grants(self):
        for failure in ("not-stopped", "remaining-child", "same-pid", "changed-port", "lost-grants"):
            scenario = Scenario()
            if failure == "not-stopped": scenario.stop_result["passed"] = False
            elif failure == "remaining-child": scenario.stop_result["remainingProcesses"] = [81]
            elif failure == "same-pid": scenario.restart_result["pid"] = 81
            elif failure == "changed-port": scenario.restart_result["port"] = 9999
            else: scenario.restart_result["identityAndGrantsRetained"] = False
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary) / "flow"
                with self.assertRaises(flow.probe.ProbeFailure): scenario.run(directory)
                self.assertFalse(json.loads((directory / "report.json").read_text())["workflowStepsPassed"])

    def test_reader_cannot_restart_between_phases_or_during_recovery(self):
        for failure_step in ("reader-unchanged", "reader-before-offline", "source-recovered"):
            scenario = Scenario()
            def change(_phase, step, value):
                if step == failure_step: value["process"]["started"] = "replacement-reader"
                return value
            scenario.frame_change = change
            with self.subTest(step=failure_step), tempfile.TemporaryDirectory() as temporary:
                with self.assertRaises(flow.probe.ProbeFailure): scenario.run(Path(temporary) / "flow")


class Aggregation(unittest.TestCase):
    def test_main_flow_requires_both_source_cleanup_and_all_steps(self):
        spec = importlib.util.spec_from_file_location("native_gui_aggregate_pure", HERE / "run-probe.py")
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        for cleanup, steps in ((False, True), (True, False), (True, True)):
            with self.subTest(cleanup=cleanup, steps=steps), tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary) / "results"
                def execute(_args, _results, report, *, exercise, before_remove):
                    self.assertIs(before_remove, runner.renderer_cleanup)
                    report["nativeSource"] = {"cleanup": {"passed": cleanup}}
                    report["nativeFederatedFlow"] = {"workflowStepsPassed": steps}
                argv = ["run-probe.py", "--unified-packages", temporary, "--client-packages", temporary,
                        "--rid", "win-x64", "--version", "test", "--provenance", str(Path(temporary) / "unused.json"),
                        "--results-directory", str(output), "--flow", "federated", "--source-publish", temporary]
                with patch.object(sys, "argv", argv), patch.object(runner.lifecycle.base, "require_hosted_runner"), \
                        patch.object(runner, "provenance", return_value={}), patch.object(runner.lifecycle, "execute", side_effect=execute), \
                        patch.object(runner.subprocess, "check_output", return_value="f" * 40), contextlib.redirect_stdout(io.StringIO()):
                    code = runner.main()
                report = json.loads((output / "report.json").read_text())
                self.assertEqual(cleanup and steps, report["mainFlowPassed"])
                self.assertEqual(cleanup and steps, report["passed"])
                self.assertEqual(0 if cleanup and steps else 1, code)


if __name__ == "__main__":
    unittest.main()
