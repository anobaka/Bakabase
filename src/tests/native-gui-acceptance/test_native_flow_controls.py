#!/usr/bin/env python3
"""Pure selector, input privacy and package web-provenance regressions."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import zipfile

from test_flow import flow, view

SPEC = importlib.util.spec_from_file_location("native_runner_web_test", Path(__file__).with_name("run-probe.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class Controls(unittest.TestCase):
    def test_resource_card_requires_exact_title_inside_the_unique_enabled_button(self):
        snapshot = view(buttons=["source label alpha available"])
        button = snapshot["windows"][0]["nodes"][0]
        heading = dict(button, role="AXStaticText", name="alpha", text="alpha", path=button["path"]+[0])
        snapshot["windows"][0]["nodes"].append(heading)
        self.assertEqual(button["path"], flow.resource_card(snapshot, "alpha")["path"])
        # A title elsewhere on the page cannot identify this unrelated button.
        heading["path"] = [0, 5, 7]
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "MissingOrAmbiguous"):
            flow.resource_card(snapshot, "alpha")

    def test_resource_card_never_picks_between_distinct_matching_cards(self):
        snapshot = view(buttons=["source alpha", "another alpha"])
        items = snapshot["windows"][0]["nodes"]
        for button in list(items):
            items.append(dict(button, role="AXStaticText", name="alpha", text="alpha", path=button["path"]+[0]))
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "MissingOrAmbiguous"):
            flow.resource_card(snapshot, "alpha")

    def test_set_submits_once_without_retaining_input_in_reports(self):
        snapshot = view(buttons=["Device address"])
        snapshot["windows"][0]["nodes"][0].update(role="AXTextField", valueSettable=True)
        value = "private-fixture-value"
        with tempfile.TemporaryDirectory() as root, patch.object(flow.probe, "hosted"), patch.object(flow, "perform") as perform:
            driver = flow.Driver({}, 42, Path(root) / "flow")
            driver.current = snapshot
            driver.set("Device address", value)
            self.assertEqual("set", perform.call_args.args[3])
            self.assertEqual(value, perform.call_args.args[4])
            self.assertNotIn(value, json.dumps(driver.report))
            self.assertNotIn(value, (driver.directory / "report.json").read_text())

    def test_disabled_controls_are_assertable_but_not_pressable(self):
        snapshot = view(buttons=["Open containing folder on this device"])
        snapshot["windows"][0]["nodes"][0]["enabled"] = False
        self.assertEqual(1, len(flow.matches(snapshot, "Open containing folder on this device", "button", enabled=False)))
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "MissingOrAmbiguous"):
            flow.one(snapshot, "Open containing folder on this device", "button")

    def test_editable_descendants_cannot_be_selected_even_with_a_matching_label(self):
        snapshot = view(buttons=["Secret child"])
        snapshot["windows"][0]["nodes"][0]["editableAncestor"] = True
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "EditableDescendantNotAllowed"):
            flow.one(snapshot, "Secret child", "button")

    def test_every_phase_inherits_the_earlier_global_deadline(self):
        with tempfile.TemporaryDirectory() as root, patch.object(flow.probe, "hosted"), patch.object(flow.time, "monotonic", return_value=10):
            driver = flow.Driver({}, 42, Path(root) / "flow", deadline=25, scope="native-query")
            self.assertEqual(25, driver.deadline)
            driver.current = view(buttons=["Search"])
            with patch.object(flow, "perform") as perform, self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeFlowDeadlineExceeded"):
                driver.press("Search")
            perform.assert_not_called()

    def test_expired_cross_check_never_opens_a_connection(self):
        with patch.object(flow.time, "monotonic", return_value=20), \
                patch.object(flow.http.client, "HTTPConnection") as connection:
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeFlowDeadlineExceeded"):
                flow.status({"port": 12001}, deadline=19)
            connection.assert_not_called()

    def test_slow_cross_check_cannot_extend_its_absolute_deadline(self):
        now = [10]
        connection = Mock()
        response = connection.getresponse.return_value
        response.status = 200
        def late_chunk(_size):
            now[0] = 14
            return b'{"peers":[]}'
        response.read1.side_effect = late_chunk
        with patch.object(flow.time, "monotonic", side_effect=lambda: now[0]), \
                patch.object(flow.http.client, "HTTPConnection", return_value=connection):
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeFlowDeadlineExceeded"):
                flow.status({"port": 12001}, deadline=12)
        connection.close.assert_called_once()
        self.assertEqual(1, response.read1.call_count)


class RendererCleanupIntegration(unittest.TestCase):
    def test_unknown_binding_blocks_installed_fixture_removal(self):
        app = {"rid": "osx-x64", "installationAttempted": True}
        report = {"macosObserver": "direct-ax"}
        with self.assertRaisesRegex(AssertionError, "ownership was not established"):
            runner.renderer_cleanup(app, report, "unified")
        evidence = report["installedRendererCleanup"]["unified"]
        self.assertFalse(evidence["passed"])
        self.assertTrue(evidence["retainedInstalledFixtures"])

    def test_only_an_unattempted_installation_can_skip_the_renderer_check(self):
        report = {"macosObserver": "direct-ax"}
        runner.renderer_cleanup({"rid": "osx-x64", "installationAttempted": False}, report, "client")
        self.assertEqual("InstallationNotAttempted", report["installedRendererCleanup"]["client"]["reason"])


class WebProvenance(unittest.TestCase):
    def make_archive(self, root, *, link=False):
        path = Path(root) / "fixture-Portable.zip"
        with zipfile.ZipFile(path, "w") as archive:
            info = zipfile.ZipInfo("payload/current/web/index.html")
            if link:
                info.create_system = 3
                info.external_attr = 0o120777 << 16
            archive.writestr(info, b"synthetic web root")
            archive.writestr("payload/current/web/assets/app.js", b"synthetic script")
            archive.writestr("payload/current/Bakabase.exe", b"not executed")
        audit = {"passed": True, "portableContent": "payload/current/", "artifacts": {"portable": {
            "file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "sizeBytes": path.stat().st_size}}}
        return path, {"role": "unified", "packages": Path(root), "packageAudit": audit}

    def test_expected_web_hashes_come_from_verified_archive_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            _, app = self.make_archive(root)
            inventory = runner.audited_web_inventory(app)
            self.assertEqual({"index.html", "assets/app.js"}, set(inventory))
            self.assertEqual(hashlib.sha256(b"synthetic script").hexdigest(), inventory["assets/app.js"]["sha256"])

    def test_changed_archive_or_web_links_are_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            path, app = self.make_archive(root)
            with path.open("ab") as stream:
                stream.write(b"changed")
            with self.assertRaisesRegex(AssertionError, "archive changed"):
                runner.audited_web_inventory(app)
            _, app = self.make_archive(root, link=True)
            with self.assertRaisesRegex(AssertionError, "archive links"):
                runner.audited_web_inventory(app)


if __name__ == "__main__":
    unittest.main()
