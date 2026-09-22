#!/usr/bin/env python3
"""Non-OS native control fixtures: no real UI, accounts, system logs or products."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_flow_test", HERE / "flow.py")
flow = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(flow)


def view(texts=(), buttons=(), menus=(), scopes=()):
    values = [("AXStaticText", text) for text in texts] + [("AXButton", text) for text in buttons] + [("AXMenuItem", text) for text in menus] + [("AXCheckBox", text) for text in scopes]
    return {"backend": "macos-system-events-ax", "enabled": True, "readOnly": True, "truncated": False,
            "process": {"pid": 42, "executable": "/fixture/Bakabase", "started": "owned-start"},
            "windows": [{"index": 0, "visible": True, "nodes": [{"path": [0, 0, index], "role": role,
                "name": name, "text": name if role == "AXStaticText" else "", "identifier": "", "enabled": True,
                "insideWebContent": True, "actions": [] if role == "AXStaticText" else ["AXPress"]}
                for index, (role, name) in enumerate(values)]}]}


class Selectors(unittest.TestCase):
    def test_webview2_scope_uses_observed_toggle_pattern_without_changing_ordinary_press(self):
        snapshot = view(buttons=["This device"])
        snapshot["backend"] = "windows-uia"
        node = snapshot["windows"][0]["nodes"][0]
        node.update(role="ControlType.Button", visible=True, runtimeId=[42, 7, 19],
                    actions=["TogglePatternIdentifiers.Pattern"])
        selector = flow.one(snapshot, "This device", "scope")
        self.assertEqual("toggle", selector["operation"])
        self.assertNotIn("operation", flow.one(snapshot, "This device", "button"))
        node["actions"] = ["InvokePatternIdentifiers.Pattern"]
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeScopeToggleUnavailable"):
            flow.one(snapshot, "This device", "scope")

    def test_driver_records_the_actual_native_scope_toggle_dispatch(self):
        snapshot = view(buttons=["This device"])
        snapshot["backend"] = "windows-uia"
        snapshot["windows"][0]["nodes"][0].update(role="ControlType.Button", visible=True,
            runtimeId=[42, 7, 19], actions=["TogglePatternIdentifiers.Pattern"])
        with tempfile.TemporaryDirectory() as temporary, patch.object(flow.probe, "hosted"), \
                patch.object(flow, "perform") as action:
            driver = flow.Driver({"rid": "win-x64"}, 42, Path(temporary) / "flow")
            driver.current = snapshot
            driver.press("This device", "scope")
            self.assertEqual("toggle", action.call_args.args[3])
            self.assertEqual([42, 7, 19], action.call_args.args[2]["runtimeId"])
            self.assertEqual({"operation": "toggle", "label": "This device", "kind": "scope", "submitted": True},
                             driver.report["actions"][0])

    def test_toggle_cannot_be_used_without_an_observed_windows_scope_selector(self):
        snapshot = view(buttons=["Close"])
        with patch.object(flow.probe, "hosted"), patch.object(flow.probe, "bounded_command") as action:
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "UnsupportedNativeToggle"):
                flow.perform({"rid": "win-x64"}, snapshot, flow.one(snapshot, "Close", "button"), "toggle")
        action.assert_not_called()

    def test_wkwebview_pressed_scope_is_selected_as_checkbox_not_an_unrelated_button(self):
        snapshot = view(buttons=["Search"], scopes=["This device", "All enabled devices", "Choose devices"])
        self.assertEqual("AXCheckBox", flow.one(snapshot, "This device", "scope")["role"])
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "MissingOrAmbiguousNativeControl"):
            flow.one(snapshot, "This device", "button")

    def test_semantic_kind_disambiguates_menu_from_header_link(self):
        snapshot = view(menus=["Devices and sharing"])
        duplicate = dict(snapshot["windows"][0]["nodes"][0], role="AXLink", path=[0, 0, 1])
        snapshot["windows"][0]["nodes"].append(duplicate)
        self.assertEqual("AXMenuItem", flow.one(snapshot, "Devices and sharing", "menu")["role"])

    def test_missing_ambiguous_disabled_or_outside_web_control_is_not_clicked(self):
        for change in ("missing", "ambiguous", "disabled", "outside-web"):
            snapshot = view(buttons=["Close"])
            node = snapshot["windows"][0]["nodes"][0]
            if change == "missing": node["name"] = "Other"
            if change == "ambiguous": snapshot["windows"][0]["nodes"].append(dict(node))
            if change == "disabled": node["enabled"] = False
            if change == "outside-web": node["insideWebContent"] = False
            with self.subTest(change=change), self.assertRaises(flow.probe.ProbeFailure):
                flow.one(snapshot, "Close", "button")

    def test_offscreen_or_unknown_uia_controls_and_text_are_not_visible(self):
        for visibility in (False, None):
            snapshot = view(texts=["No matching resources"], buttons=["Search"])
            snapshot["backend"] = "windows-uia"
            for node in snapshot["windows"][0]["nodes"]:
                node["role"] = "ControlType.Button" if node["role"] == "AXButton" else "ControlType.Text"
                node["visible"] = visibility
                node["runtimeId"] = [42, *node["path"]]
            with self.subTest(visibility=visibility):
                self.assertFalse(flow.has_text(snapshot, "No matching resources"))
                with self.assertRaisesRegex(flow.probe.ProbeFailure, "MissingOrAmbiguousNativeControl"):
                    flow.one(snapshot, "Search", "button")
                for node in snapshot["windows"][0]["nodes"]:
                    node["visible"] = True
                self.assertTrue(flow.has_text(snapshot, "No matching resources"))
                self.assertEqual("Search", flow.one(snapshot, "Search", "button")["name"])

    def test_uia_aliases_require_identical_runtime_id_and_semantics(self):
        snapshot = view(buttons=["Close"])
        snapshot["backend"] = "windows-uia"
        node = snapshot["windows"][0]["nodes"][0]
        node.update(role="ControlType.Button", visible=True, runtimeId=[42, 7, 19])
        alias = dict(node, path=[0, 1, 3, 1, 0])
        snapshot["windows"][0]["nodes"].append(alias)
        selected = flow.one(snapshot, "Close", "button")
        self.assertEqual([42, 7, 19], selected["runtimeId"])
        self.assertEqual(node["path"], selected["path"])
        alias["runtimeId"] = [42, 7, 20]
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "MissingOrAmbiguousNativeControl"):
            flow.one(snapshot, "Close", "button")
        alias["runtimeId"] = node["runtimeId"]
        alias["identifier"] = "different-control"
        with self.assertRaisesRegex(flow.probe.ProbeFailure, "InconsistentRuntimeIdObservation"):
            flow.one(snapshot, "Close", "button")

    def test_uia_missing_or_invalid_runtime_id_never_establishes_uniqueness(self):
        for identity in (None, [], [True], [2**31], [0]*65):
            snapshot = view(buttons=["Close"])
            snapshot["backend"] = "windows-uia"
            snapshot["windows"][0]["nodes"][0].update(role="ControlType.Button", visible=True, runtimeId=identity)
            with self.subTest(identity=identity), self.assertRaisesRegex(flow.probe.ProbeFailure, "InvalidObservedRuntimeId"):
                flow.one(snapshot, "Close", "button")

    def test_partial_tree_proves_neither_unique_control_nor_presence_or_absence(self):
        snapshot = view(texts=["No matching resources"], buttons=["Search"])
        snapshot["truncated"] = True
        for check in (lambda: flow.one(snapshot, "Search", "button"),
                      lambda: flow.has_text(snapshot, "No matching resources"),
                      lambda: not flow.matches(snapshot, "Close", "button")):
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "IncompleteNativeTree"):
                check()

    def test_partial_tree_cannot_submit_an_already_resolved_action(self):
        snapshot = view(buttons=["Close"])
        selector = flow.one(snapshot, "Close", "button")
        snapshot["truncated"] = True
        with patch.object(flow.probe, "hosted"), patch.object(flow.probe, "bounded_command") as action:
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "IncompleteNativeTree"):
                flow.perform({"rid": "win-x64"}, snapshot, selector)
        action.assert_not_called()

    def test_pid_change_prevents_native_action(self):
        snapshot = view(buttons=["Close"])
        app = {"rid": "osx-arm64", "exe": Path("/fixture/Bakabase")}
        with patch.object(flow.probe, "hosted"), patch.object(flow.probe, "mac_identity", return_value={"pid": 42, "started": "reused"}), \
                patch.object(flow.probe, "bounded_command") as action:
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "ProductProcessChangedBeforeAction"):
                flow.perform(app, snapshot, flow.one(snapshot, "Close", "button"))
        action.assert_not_called()

    def test_failed_native_action_cannot_be_reported_submitted(self):
        snapshot = view(buttons=["Close"])
        app = {"rid": "osx-arm64", "exe": Path("/fixture/Bakabase")}
        with patch.object(flow.probe, "hosted"), patch.object(flow.probe, "mac_identity", return_value=snapshot["process"]), \
                patch.object(flow.probe, "bounded_command", return_value={"performed": False, "errorStage": "private payload"}):
            with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeActionFailed:unknown"):
                flow.perform(app, snapshot, flow.one(snapshot, "Close", "button"))


class Readiness(unittest.TestCase):
    def test_wait_uses_existing_phase_deadline_and_does_not_reset_it(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(flow.probe, "hosted"), \
                patch.object(flow.time, "monotonic", return_value=0), patch.object(flow.time, "sleep"):
            driver = flow.Driver({}, 42, Path(temporary) / "flow")
            with patch.object(driver, "read", side_effect=[view(), view(buttons=["Search"])]) as read:
                driver.wait("query", lambda snapshot: bool(flow.matches(snapshot, "Search", "button")))
            self.assertEqual([90, 90], [call.args[1] for call in read.call_args_list])

    def test_partial_state_is_reobserved_before_evaluating_absence_without_extending_deadline(self):
        partial = view(buttons=["Search"])
        partial["truncated"] = True
        with tempfile.TemporaryDirectory() as temporary, patch.object(flow.probe, "hosted"), \
                patch.object(flow.time, "monotonic", return_value=0), patch.object(flow.time, "sleep"):
            driver = flow.Driver({}, 42, Path(temporary) / "flow")
            condition_calls = []
            def condition(snapshot):
                condition_calls.append(snapshot)
                return not flow.matches(snapshot, "Close", "button")
            complete = view(buttons=["Search"])
            with patch.object(driver, "read", side_effect=[partial, complete]) as read:
                driver.wait("dialog-gone", condition)
            self.assertEqual([complete], condition_calls)
            self.assertEqual([90, 90], [call.args[1] for call in read.call_args_list])

    def test_permanently_partial_tree_never_passes_a_checkpoint(self):
        partial = view(buttons=["Search"])
        partial["truncated"] = True
        with tempfile.TemporaryDirectory() as temporary, patch.object(flow.probe, "hosted"), \
                patch.object(flow.time, "monotonic", return_value=0), patch.object(flow.time, "sleep"):
            driver = flow.Driver({}, 42, Path(temporary) / "flow")
            with patch.object(driver, "read", return_value=partial) as read:
                with self.assertRaisesRegex(flow.probe.ProbeFailure, "NativeExpectedStateUnavailable"):
                    driver.wait("query", lambda _snapshot: self.fail("Partial tree used as evidence"))
            self.assertEqual(8, read.call_count)
            self.assertEqual([], driver.report["checkpoints"])


class FlowSequence(unittest.TestCase):
    def fake_driver(self):
        menus = ["Multi-device library", "Devices and sharing"]
        frames = {
            "initial-native-ui": view(texts=["Help Center"], buttons=["Close"]),
            "help-dismissed": view(buttons=["Help Center"], menus=menus), "local-menu": view(menus=menus),
            "browsing-default-off": view(texts=["Browsing is off"], buttons=["Enable browsing"], menus=menus),
            "device-settings-off": view(texts=["Browse libraries on this device", "Browsing is off"], buttons=["Enable browsing"], menus=menus),
            "device-settings-enabled": view(texts=["Browsing enabled"], buttons=["Turn browsing off"], menus=menus),
            "search-scopes-visible": view(scopes=["This device", "All enabled devices", "Choose devices"], buttons=["Search"], menus=menus),
            "local-source-selected": view(buttons=["Search"], menus=menus),
            "empty-library-results": view(texts=["No matching resources", "0 resources · 1/1 devices searched", "Read-only view"], menus=menus),
            "saved-browsing-state": view(texts=["Browsing enabled"], buttons=["Turn browsing off"], menus=menus),
        }
        class FakeDriver:
            app = {"port": 40001}
            current = None
            report = {"mainFlowPassed": False}
            def __init__(self): self.actions = []; self.checkpoints = []
            def wait(self, step, condition):
                self.current = frames[step]
                if not condition(self.current): raise AssertionError("Native state missing: " + step)
                self.checkpoints.append(step)
            def press(self, label, kind="button"):
                flow.one(self.current, label, kind)
                self.actions.append((label, kind))
            def save(self): pass
        return FakeDriver(), frames

    def test_browsing_is_changed_only_by_native_control_and_all_visible_results_required(self):
        driver, _ = self.fake_driver()
        states = [{"browsingEnabled": False, "sharingEnabled": False, "peers": []},
                  {"browsingEnabled": True, "sharingEnabled": False, "peers": []}]
        with patch.object(flow, "status", side_effect=AssertionError("No hidden API mutation")):
            report = flow.empty_library(driver, status_reader=lambda _app: states.pop(0))
        self.assertTrue(report["emptyLibraryFlowPassed"])
        self.assertFalse(report["mainFlowPassed"])
        self.assertIn(("Enable browsing", "button"), driver.actions)
        self.assertIn(("Search", "button"), driver.actions)
        self.assertIn("empty-library-results", driver.checkpoints)
        self.assertEqual(["native-pairing", "remote-query-and-detail", "offline-recovery"], report["remaining"])

    def test_http_state_without_native_empty_result_cannot_pass(self):
        driver, frames = self.fake_driver()
        frames["empty-library-results"] = view(texts=["Loading…"])
        states = iter([{"browsingEnabled": False, "sharingEnabled": False, "peers": []},
                       {"browsingEnabled": True, "sharingEnabled": False, "peers": []}])
        with self.assertRaisesRegex(AssertionError, "Native state missing"):
            flow.empty_library(driver, status_reader=lambda _app: next(states))
        self.assertFalse(driver.report.get("emptyLibraryFlowPassed", False))


class ScriptFixtures(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt", "PowerShell pure identity fixture runs on the Windows runner")
    def test_uia_final_runtime_identity_guard_rejects_replaced_control(self):
        fixture = r'''
Add-Type -AssemblyName UIAutomationClient
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseInput([Console]::In.ReadToEnd(),[ref]$tokens,[ref]$errors)
if($errors.Count){exit 1}
$guards=@($ast.FindAll({param($node)
  $node -is [System.Management.Automation.Language.IfStatementAst] -and
  $node.Extent.Text.StartsWith('if(![System.Windows.Automation.Automation]::Compare(')
},$true))
if($guards.Count -ne 1){exit 2}
$guard=[scriptblock]::Create($guards[0].Extent.Text)
[int[]]$expectedRuntimeId=@(42,7,19)
foreach($changed in @($false,$true)) {
  $element=[pscustomobject]@{RuntimeId=@(42,7,$(if($changed){20}else{19}))}
  $element | Add-Member ScriptMethod GetRuntimeId {return [int[]]$this.RuntimeId}
  $blocked=$false
  try { & $guard } catch {$blocked=$true}
  if($blocked -ne $changed){exit 3}
}
'''
        subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", fixture],
                       input=(HERE / "windows-action.ps1").read_text(), text=True,
                       capture_output=True, timeout=10, check=True)

    @unittest.skipUnless(os.name == "nt", "PowerShell pure guard fixture runs on the Windows runner")
    def test_uia_action_guard_rechecks_current_visibility_immediately_before_action(self):
        # Execute just the actual final visibility guard with plain objects. No
        # UIA assembly, OS window, product process or Invoke pattern is loaded.
        fixture = r'''
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseInput([Console]::In.ReadToEnd(),[ref]$tokens,[ref]$errors)
if($errors.Count){exit 1}
$guards=@($ast.FindAll({param($node)
  $node -is [System.Management.Automation.Language.IfStatementAst] -and
  $node.Extent.Text.StartsWith('if($element.Current.IsOffscreen)')
},$true))
if($guards.Count -ne 1){exit 2}
$guard=[scriptblock]::Create($guards[0].Extent.Text)
foreach($offscreen in @($false,$true)) {
  $element=[pscustomobject]@{Current=[pscustomobject]@{IsOffscreen=$offscreen}}
  $blocked=$false
  try { & $guard } catch {$blocked=$true}
  if($blocked -ne $offscreen){exit 3}
}
'''
        subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", fixture],
                       input=(HERE / "windows-action.ps1").read_text(), text=True,
                       capture_output=True, timeout=10, check=True)

    @unittest.skipUnless(shutil.which("node"), "Node is required for the non-OS AX action fixture")
    def test_macos_action_rechecks_target_and_never_presses_a_changed_control(self):
        fixture = r'''
const fs=require('fs'),vm=require('vm');const code=fs.readFileSync(process.argv[1],'utf8');
function run(changed,outside) {
 let presses=0;
 const actions=()=>[{name:()=> 'AXPress'}];actions.byName=()=>({perform:()=>{presses++}});
 const leaf={attributes:{byName:k=>({value:()=>({AXRole:'AXButton',AXTitle:changed?'Unrelated':'Close',AXEnabled:true,AXIdentifier:''}[k]??null)})},actions};
 const web={attributes:{byName:k=>({value:()=>k==='AXRole'?(outside?'AXGroup':'AXWebArea'):null})},uiElements:()=>[leaf]};
 const win={attributes:{byName:k=>({value:()=>false})},uiElements:()=>[web]};
 const se={uiElementsEnabled:()=>true,processes:{whose:q=>{if(q.unixId!==42)throw Error();return()=>[{visible:()=>true,unixId:()=>42,windows:()=>[win]}]}}};
 const input={pid:42,operation:'press',selector:{path:[0,0,0],role:'AXButton',name:'Close',identifier:''}};
 const result=JSON.parse(vm.runInNewContext('const input='+JSON.stringify(input)+';\n'+code,{Application:()=>se}));
 if(result.performed!==(!changed&&!outside) || presses!==(!changed&&!outside?1:0))throw Error('unexpected press');
}
run(false,false);run(true,false);run(false,true);
'''
        subprocess.run([shutil.which("node"), "-e", fixture, str(HERE / "macos-action.js")],
                       capture_output=True, text=True, timeout=5, check=True)


if __name__ == "__main__":
    unittest.main()
