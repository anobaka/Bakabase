#!/usr/bin/env python3
"""Synthetic provider controls only. No OS UI, process or trust observations."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_actions_fixture", HERE / "test_direct_ax_tree.py")
fixture = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(fixture)


@unittest.skipUnless(shutil.which("node"), "Node is required for synthetic AX fixtures")
class DirectActions(unittest.TestCase):
    def run_fixture(self, **options):
        result = subprocess.run([shutil.which("node"), "-e", fixture.FIXTURE, str(HERE), json.dumps(options)],
                                capture_output=True, text=True, timeout=5, check=True)
        return json.loads(result.stdout)

    def test_set_submits_once_without_reading_or_persisting_field_value(self):
        result = self.run_fixture(action=True, operation="set", target="input", inputValue="TEST-VALUE-PRIVATE")
        self.assertEqual({"performed": True, "operation": "set"}, result["output"])
        self.assertEqual(1, result["sets"])
        self.assertTrue(result["submittedValueMatched"])
        self.assertEqual(0, result["presses"])
        self.assertNotIn("input:AXValue", result["reads"])
        self.assertNotIn("TEST-VALUE-PRIVATE", json.dumps(result))
        snapshot = self.run_fixture()["output"]
        saved = fixture.probe.sanitize(snapshot)["windows"][0]["nodes"]
        self.assertTrue(next(n for n in saved if n["name"] == "Address")["valueSettable"])
        self.assertIsNone(next(n for n in saved if n["password"])["valueSettable"])

    def test_set_rejects_secure_readonly_hidden_duplicate_partial_or_changed_controls(self):
        for option in ("secureInput", "secureAfterRead", "readOnlyInput", "offscreenInput", "duplicateInput",
                       "missingChildren", "hidden", "pidChanged", "settableError", "setError"):
            with self.subTest(option=option):
                result = self.run_fixture(action=True, operation="set", target="input", inputValue="TEST-VALUE-PRIVATE", **{option: True})
                self.assertFalse(result["output"]["performed"])
                self.assertEqual(0, result["sets"])
                self.assertNotIn("input:AXValue", result["reads"])
                self.assertNotIn("TEST-VALUE-PRIVATE", json.dumps(result))

    def test_set_bound_and_noninput_target_fail_before_any_submission(self):
        for options in ({"inputValue": 42}, {"inputValue": "x"*4097}, {}, {"inputValue": "TEST-VALUE-PRIVATE", "target": "region", "regionGroup": True}):
            with self.subTest(options=list(options)):
                result = self.run_fixture(action=True, operation="set", **{"target": "input", **options})
                self.assertFalse(result["output"]["performed"])
                self.assertEqual(0, result["sets"])

    def test_scroll_uniquely_observed_offscreen_group_uses_only_exposed_native_action(self):
        result = self.run_fixture(action=True, operation="scroll", target="region", regionGroup=True,
                                  offscreenRegion=True, scrollSupported=True)
        self.assertEqual({"performed": True, "operation": "scroll"}, result["output"])
        self.assertEqual(1, result["scrolls"])
        self.assertEqual(0, result["presses"])
        snapshot = self.run_fixture(regionGroup=True, offscreenRegion=True, scrollSupported=True)["output"]
        region = next(n for n in fixture.probe.sanitize(snapshot)["windows"][0]["nodes"] if n["role"] == "AXGroup")
        self.assertEqual("Connection settings", region["name"])
        self.assertFalse(region["visible"])
        self.assertTrue(region["scrollToVisible"])

    def test_scroll_has_no_unsupported_action_or_partial_ambiguous_identity_fallback(self):
        for options in ({"scrollSupported": False}, {"missingChildren": True}, {"duplicate": True},
                        {"replacedControl": True}, {"pidChanged": True}, {"scrollError": True}, {"hidden": True}):
            with self.subTest(options=options):
                result = self.run_fixture(action=True, operation="scroll", offscreen=True,
                                          **{"scrollSupported": True, **options})
                self.assertFalse(result["output"]["performed"])
                self.assertEqual(0, result["scrolls"])
                self.assertEqual(0, result["presses"])

    def test_editable_descendant_labels_and_actions_remain_private(self):
        result = self.run_fixture(editableDescendants=True, scrollSupported=True, regionGroup=True)
        private = [n for n in result["output"]["windows"][0]["nodes"] if n["editableAncestor"]]
        self.assertTrue(private)
        self.assertTrue(all(not n["actions"] and not n["scrollToVisible"] and n["valueSettable"] is None for n in private))
        self.assertNotIn("EDITABLE-", json.dumps(result["output"]))


@unittest.skipUnless(os.name == "nt", "PowerShell/UIA type-only fixtures run on Windows; no UI is accessed")
class WindowsActions(unittest.TestCase):
    def run_ps(self, source, fixture_source):
        subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", fixture_source],
                       input=(HERE/source).read_text(), text=True, capture_output=True, timeout=10, check=True)

    def test_actual_set_and_scroll_branches_use_patterns_without_readback_or_fallback(self):
        self.run_ps("windows-action.ps1", r'''
Add-Type -AssemblyName UIAutomationClient
$tokens=$null;$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseInput([Console]::In.ReadToEnd(),[ref]$tokens,[ref]$errors)
if($errors.Count){exit 1}
$branches=@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and
 $n.Extent.Text.StartsWith("if(`$record.operation -eq 'press')")},$true))
if($branches.Count -ne 1){exit 2}
$invoke=[scriptblock]::Create($branches[0].Extent.Text)
foreach($operation in @('set','scroll')) {foreach($supported in @($true,$false)) {foreach($readOnly in @($true,$false)) {
 $record=[pscustomobject]@{operation=$operation;value='FIXTURE-PRIVATE';selector=@{}}
 $role='ControlType.Edit';$observed=@{valueSettable=$supported;scrollToVisible=$supported}
 $pattern=[pscustomobject]@{Current=[pscustomobject]@{IsReadOnly=$readOnly};writes=0;scrolls=0}
 $pattern | Add-Member ScriptMethod SetValue {param($value) if($value -ne 'FIXTURE-PRIVATE'){throw 'WrongInput'};$this.writes++}
 $pattern | Add-Member ScriptMethod ScrollIntoView {$this.scrolls++}
 $pattern.Current | Add-Member ScriptProperty Value {throw 'ForbiddenReadback'}
 $element=[pscustomobject]@{pattern=$pattern}
 $element | Add-Member ScriptMethod GetCurrentPattern {param($p) return $this.pattern}
 $blocked=$false;try{& $invoke}catch{$blocked=$true}
 $expected=$supported -and ($operation -eq 'scroll' -or !$readOnly)
 if($blocked -eq $expected){exit 3}
 if($pattern.writes -ne [int]($expected -and $operation -eq 'set')){exit 4}
 if($pattern.scrolls -ne [int]($expected -and $operation -eq 'scroll')){exit 5}
}}}
''')

    def test_complete_tree_selector_keeps_distinct_runtime_ids_and_allows_offscreen_only_for_scroll(self):
        self.run_ps("windows-action.ps1", r'''
$source=[Console]::In.ReadToEnd()
$start=$source.IndexOf('if($tree.truncated)');$end=$source.IndexOf('$path=@(',$start)
if($start -lt 0 -or $end -lt $start){exit 1}
$select=[scriptblock]::Create($source.Substring($start,$end-$start))
foreach($scenario in @('single','alias','distinct','inconsistent','private','partial','offscreen-scroll','offscreen-press')) {
 $op=if($scenario -eq 'offscreen-press'){'press'}else{'scroll'}
 $record=@{selector=@{role='ControlType.Group';name='Settings'}}
 $node=@{role='ControlType.Group';name='Settings';identifier='';runtimeId=@(42,9);insideWebContent=$true;
  password=$false;editableAncestor=$false;visible=(!$scenario.StartsWith('offscreen'));enabled=$true;valueSettable=$null;scrollToVisible=$true}
 $nodes=@($node)
 if($scenario -in @('alias','distinct','inconsistent')) {
  $other=$node.Clone()
  if($scenario -eq 'distinct'){$other.runtimeId=@(42,10)}
  if($scenario -eq 'inconsistent'){$other.identifier='changed'}
  $nodes+=@($other)
 }
 if($scenario -eq 'private'){$node.editableAncestor=$true}
 $tree=@{truncated=($scenario -eq 'partial');windows=@(@{visible=$true;nodes=$nodes})}
 $blocked=$false;try{& $select}catch{$blocked=$true}
 $expected=$scenario -in @('single','alias','offscreen-scroll')
 if($blocked -eq $expected){exit 2}
}
''')

    def test_shared_tree_hides_editable_descendants_and_retains_offscreen_scroll_metadata(self):
        self.run_ps("windows-tree.ps1", r'''
Add-Type -AssemblyName UIAutomationClient
Invoke-Expression ([Console]::In.ReadToEnd())
function Node($role,$name,$password=$false,$offscreen=$false) {
 $current=[pscustomobject]@{ControlType=[pscustomobject]@{ProgrammaticName=$role};Name=$name;IsPassword=$password;
  IsOffscreen=$offscreen;IsEnabled=$true;ProcessId=42;AutomationId=''}
 $n=[pscustomobject]@{Current=$current;children=@();next=$null;patterns=@();id=1}
 $n | Add-Member ScriptMethod GetSupportedPatterns {return $this.patterns}
 $n | Add-Member ScriptMethod GetRuntimeId {return [int[]]@(42,$this.id)}
 return $n
}
$window=Node 'ControlType.Window' 'Owned';$web=Node 'ControlType.Document' 'Web'
$inputNode=Node 'ControlType.Edit' 'Address';$private=Node 'ControlType.Text' 'SECRET'
$private.Current.PSObject.Properties.Remove('Name')
$private.Current | Add-Member ScriptProperty Name {throw 'Private descendant label read'}
$group=Node 'ControlType.Group' 'Settings' $false $true
$group.patterns=@([pscustomobject]@{ProgrammaticName='ScrollItemPatternIdentifiers.Pattern'})
$window.children=@($web);$web.children=@($inputNode,$group);$inputNode.next=$group;$inputNode.children=@($private)
$windows=[pscustomobject]@{Count=1;window=$window};$windows | Add-Member ScriptMethod Item {param($i) return $this.window}
$walker=[pscustomobject]@{}
$walker | Add-Member ScriptMethod GetFirstChild {param($n) if($n.children.Count){return $n.children[0]}return $null}
$walker | Add-Member ScriptMethod GetNextSibling {param($n) return $n.next}
$tree=Get-OwnedTree $windows $walker @{pid=42;maxNodes=1000;maxDepth=40;readBudgetMs=24000} ([Diagnostics.Stopwatch]::StartNew())
if($tree.truncated){exit 1}
$nodes=$tree.windows[0].nodes
$secret=@($nodes | Where-Object {$_.editableAncestor})
if($secret.Count -ne 1 -or $secret[0].name -or $secret[0].actions.Count -or $secret[0].scrollToVisible){exit 2}
$target=@($nodes | Where-Object {$_.name -eq 'Settings'})[0]
if($target.visible -or !$target.scrollToVisible){exit 3}
''')


if __name__ == "__main__":
    unittest.main()
