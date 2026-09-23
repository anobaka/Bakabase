#!/usr/bin/env python3
"""Pure boundary/fixture tests. Never read a real native UI or install products."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, HERE / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


probe = load("probe_test_module", "probe.py")
runner = load("probe_test_runner", "run-probe.py")


def snapshot(backend="macos-system-events-ax"):
    return {"backend": backend, "readOnly": True, "enabled": True, "truncated": False,
            "process": {"pid": 42, "started": "fixture-start", "executable": "/fixture/Bakabase"},
            "windows": [{"index": 0, "visible": True, "name": "Bakabase", "nodes": [
                {"path": [0], "role": "AXWindow", "insideWebContent": False},
                {"path": [0, 0], "role": "AXWebArea", "insideWebContent": True, "visible": True},
                {"path": [0, 0, 0], "role": "AXButton", "insideWebContent": True,
                 "visible": True, "enabled": True, "name": "Enable browsing", "actions": ["AXPress"]}]}]}


class Capability(unittest.TestCase):
    def test_actual_web_controls_prove_only_capability_never_main_flow(self):
        result = probe.summarize(snapshot())
        self.assertTrue(result["capabilityPassed"])
        self.assertFalse(result["mainFlowPassed"])
        self.assertEqual(1, result["interactiveWebControls"])

    def test_title_only_or_native_titlebar_controls_cannot_pass(self):
        view = snapshot()
        view["windows"][0]["nodes"] = [{"path": [0], "role": "AXButton", "name": "Bakabase",
            "insideWebContent": False, "enabled": True, "actions": ["AXPress"]}]
        self.assertFalse(probe.summarize(view)["capabilityPassed"])

    def test_invisible_webview_disabled_ax_and_noninteractive_text_do_not_pass(self):
        for change in ("invisible", "disabled", "static", "unnamed"):
            view = snapshot()
            if change == "invisible":
                view["windows"][0]["visible"] = False
            elif change == "disabled":
                view["enabled"] = False
            elif change == "static":
                view["windows"][0]["nodes"][-1]["actions"] = []
            else:
                view["windows"][0]["nodes"][-1]["name"] = ""
            with self.subTest(change=change):
                self.assertFalse(probe.summarize(view)["capabilityPassed"])

    def test_truncation_is_explicit_not_complete_tree_claim(self):
        view = snapshot()
        view["truncated"] = True
        result = probe.summarize(view)
        self.assertTrue(result["treeTruncated"])
        self.assertFalse(result["mainFlowPassed"])

    def test_uia_text_pattern_alone_is_not_an_interactive_control(self):
        view = snapshot("windows-uia")
        view["windows"][0]["nodes"][-1]["actions"] = ["TextPatternIdentifiers.Pattern"]
        self.assertFalse(probe.summarize(view)["capabilityPassed"])
        view["windows"][0]["nodes"][-1]["actions"] = ["InvokePatternIdentifiers.Pattern"]
        self.assertTrue(probe.summarize(view)["capabilityPassed"])

    def test_uia_hidden_or_unknown_node_visibility_is_not_capability(self):
        for visibility in (False, None):
            view = snapshot("windows-uia")
            view["windows"][0]["nodes"][-1]["visible"] = visibility
            with self.subTest(visibility=visibility):
                self.assertFalse(probe.summarize(view)["capabilityPassed"])
                self.assertIs(probe.sanitize(view)["windows"][0]["nodes"][-1]["visible"], visibility)

    def test_node_budget_and_invalid_backend_are_rejected(self):
        view = snapshot()
        view["windows"][0]["nodes"] *= 400
        with self.assertRaisesRegex(probe.ProbeFailure, "InvalidNodeCount"):
            probe.summarize(view)
        view = snapshot("http-response")
        with self.assertRaisesRegex(probe.ProbeFailure, "InvalidNativeBackend"):
            probe.summarize(view)

    def test_saved_schema_omits_values_paths_and_raw_diagnostics_and_redacts_before_truncation(self):
        view = snapshot()
        secret = "fixture-sensitive-text"
        view["stderr"] = secret
        view["windows"][0]["nodes"][-1].update(value=secret, name=secret+"x"*500, raw=secret)
        result = probe.sanitize(view, (secret,))
        encoded = json.dumps(result)
        self.assertNotIn(secret, encoded)
        self.assertNotIn("executable", encoded)
        self.assertNotIn('"value"', encoded)
        self.assertTrue(result["windows"][0]["nodes"][-1]["name"].startswith("[redacted]"))


class Guards(unittest.TestCase):
    def test_local_host_rejected_before_any_native_read(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(probe, "mac_identity") as identity, \
                patch.object(probe, "bounded_command") as command:
            with self.assertRaises(AssertionError):
                probe.native_snapshot({"role": "unified", "rid": "osx-arm64", "exe": Path("/fixture/Bakabase")}, 42)
        identity.assert_not_called()
        command.assert_not_called()

    def test_wrong_architecture_rejected_before_native_read(self):
        environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "fixture"}
        with patch.dict(os.environ, environment, clear=True), patch.object(probe.platform, "system", return_value="Darwin"), \
                patch.object(probe.platform, "machine", return_value="arm64"), patch.object(probe, "bounded_command") as command:
            with self.assertRaises(AssertionError):
                probe.native_snapshot({"role": "unified", "rid": "osx-x64", "exe": Path("/fixture/Bakabase")}, 42)
        command.assert_not_called()

    def test_pid_reuse_after_snapshot_cannot_pass(self):
        app = {"role": "unified", "rid": "osx-arm64", "exe": Path("/fixture/Bakabase")}
        first = {"pid": 42, "executable": str(app["exe"]), "started": "before"}
        with patch.object(probe, "hosted"), patch.object(probe, "mac_identity", side_effect=[first, dict(first, started="after")]), \
                patch.object(probe, "bounded_command", return_value=snapshot()):
            with self.assertRaisesRegex(probe.ProbeFailure, "ProductProcessChangedDuringProbe"):
                probe.native_snapshot(app, 42)

    def test_wrong_process_cannot_read_ax(self):
        app = {"role": "unified", "rid": "osx-arm64", "exe": Path("/fixture/Bakabase")}
        with patch.object(probe, "hosted"), patch.object(probe, "mac_identity", return_value={"pid": 42, "executable": "/other", "started": "x"}), \
                patch.object(probe, "bounded_command") as command:
            with self.assertRaisesRegex(probe.ProbeFailure, "ProductExecutableMismatch"):
                probe.native_snapshot(app, 42)
        command.assert_not_called()

    def test_failure_records_no_raw_native_exception(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", side_effect=OSError("sensitive private command")):
            result = probe.capture({"role": "unified", "rid": "osx-arm64"}, 42, Path(temporary) / "results")
            self.assertFalse(result["capabilityPassed"])
            self.assertNotIn("sensitive", json.dumps(result))

    def test_readiness_reobserves_empty_native_shell_without_changing_identity(self):
        empty = snapshot()
        empty["windows"] = []
        with tempfile.TemporaryDirectory() as temporary, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", side_effect=[empty, snapshot()]) as read, patch.object(probe.time, "sleep"):
            destination = Path(temporary) / "results"
            result = probe.capture({"role": "unified", "rid": "osx-x64"}, 42, destination)
            self.assertTrue(result["capabilityPassed"])
            self.assertFalse(result["mainFlowPassed"])
            self.assertEqual(2, len(result["attempts"]))
            self.assertTrue((destination / "tree-01.json").exists())
            self.assertTrue((destination / "tree-02.json").exists())
            self.assertEqual(2, read.call_count)

    def test_readiness_pid_reuse_does_not_pass(self):
        empty = snapshot()
        empty["windows"] = []
        replacement = snapshot()
        replacement["process"]["started"] = "reused"
        with tempfile.TemporaryDirectory() as temporary, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", side_effect=[empty, replacement]), patch.object(probe.time, "sleep"):
            result = probe.capture({"role": "unified", "rid": "osx-x64"}, 42, Path(temporary) / "results")
            self.assertFalse(result["capabilityPassed"])
            self.assertEqual("ProductProcessChangedDuringReadiness", result["error"]["code"])

    def test_disabled_accessibility_is_not_retried(self):
        view = snapshot()
        view["enabled"] = False
        with tempfile.TemporaryDirectory() as temporary, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", return_value=view) as read:
            result = probe.capture({"role": "unified", "rid": "osx-x64"}, 42, Path(temporary) / "results")
            self.assertFalse(result["capabilityPassed"])
            self.assertEqual(1, read.call_count)


class OwnedCommands(unittest.TestCase):
    def test_pure_helper_stdout_is_parsed(self):
        result = probe.bounded_command([sys.executable, "-c", "import json,sys; json.dump({'ok':True},sys.stdout)"], "", 2)
        self.assertEqual({"ok": True}, result)

    def test_stderr_cannot_count_as_a_successful_tree(self):
        with self.assertRaisesRegex(probe.ProbeFailure, "NativeProbeInvalidJson"):
            probe.bounded_command([sys.executable, "-c", "import sys;sys.stderr.write('{\"ok\":true}')"], "", 2)

    def test_failure_and_timeout_cannot_pass_and_do_not_leak_stderr(self):
        cases = [("import sys;sys.stderr.write('private');sys.exit(3)", "NativeProbeCommandFailed", 2),
                 ("import time;time.sleep(10)", "NativeProbeTimedOut", 0.1)]
        for script, code, timeout in cases:
            with self.subTest(code=code), self.assertRaisesRegex(probe.ProbeFailure, code):
                probe.bounded_command([sys.executable, "-c", script], "", timeout)

    def test_output_budget_terminates_only_its_helper(self):
        with patch.object(probe, "MAX_OUTPUT", 10), self.assertRaisesRegex(probe.ProbeFailure, "ProbeOutputBudgetExceeded"):
            probe.bounded_command([sys.executable, "-c", "print('x'*20000)"], "", 2)


class NativeScriptFixtures(unittest.TestCase):
    @unittest.skipUnless(shutil.which("node"), "Node is required for the non-OS JavaScript fixture")
    def test_macos_missing_children_are_partial_not_a_complete_absence(self):
        fixture = r'''
const fs=require('fs'),vm=require('vm');
const source=fs.readFileSync(process.argv[1],'utf8');
const root={attributes:{byName:key=>({value:()=>({AXRole:'AXWindow',AXTitle:'Bakabase'}[key]??null)})},
  uiElements:()=>{throw Error('Unavailable child tree')}};
const owned={unixId:()=>42,visible:()=>true,windows:()=>[root]};
const system={uiElementsEnabled:()=>true,processes:{whose:()=>()=>[owned]}};
const output=JSON.parse(vm.runInNewContext('const input={pid:42,maxNodes:1000,maxDepth:40};\n'+source,{Application:()=>system}));
if(output.truncated!==true || output.enabled!==true)throw Error('Missing subtree claimed complete');
'''
        subprocess.run([shutil.which("node"), "-e", fixture, str(HERE / "macos-snapshot.js")],
                       capture_output=True, text=True, timeout=5, check=True)

    @unittest.skipUnless(shutil.which("node"), "Node is required for the non-OS JavaScript fixture")
    def test_macos_script_reads_exact_pid_and_omits_editable_values(self):
        fixture = r'''
const fs=require('fs'),vm=require('vm');
const source=fs.readFileSync(process.argv[1],'utf8');
let queried, valuesRead=[], batches=[];
function e(role,title,children=[],extra={}) {
  const props=Object.assign({AXRole:role,AXTitle:title,AXEnabled:true,AXValue:'PRIVATE_INPUT'},extra);
  const uiElements=()=>children;uiElements.role=()=>children.map(child=>child.fixtureRole);
  const actions={name:()=>['AXPress']};
  return {fixtureRole:role, attributes:{byName:key=>({value:()=>{valuesRead.push([role,key]);return props[key] ?? null}}),
    whose:query=>{
      const names=query._or.map(term=>term.name);
      if(names.some(name=>!['AXTitle','AXDescription','AXIdentifier','AXEnabled','AXSubrole'].includes(name)))throw Error('Unsafe native filter');
      batches.push(names);
      return {name:()=>names,value:()=>names.map(key=>{valuesRead.push([role,key]);return props[key]??null})};
    }}, uiElements, actions};
}
const root=e('AXWindow','Bakabase',[e('AXWebArea','',[e('AXTextField','Search'),e('AXButton','Browse')])]);
const owned={unixId:()=>42,visible:()=>true,windows:()=>[root]};
const system={uiElementsEnabled:()=>true,processes:{whose:q=>{queried=q;return()=>[owned]}}};
const output=JSON.parse(vm.runInNewContext('const input={pid:42,maxNodes:1000,maxDepth:40};\n'+source,{Application:()=>system}));
if(queried.unixId!==42 || output.windows[0].nodes.length!==4) throw Error('wrong process or tree');
if(JSON.stringify(output).includes('PRIVATE_INPUT') || valuesRead.some(([role,key])=>role==='AXTextField'&&key==='AXValue')) throw Error('input value read');
if(output.truncated || batches.length!==2 || valuesRead.filter(([role,key])=>key==='AXRole').length!==1)throw Error('Safe batching was not used');
console.log(JSON.stringify(output));
'''
        completed = subprocess.run([shutil.which("node"), "-e", fixture, str(HERE / "macos-snapshot.js")],
                                   capture_output=True, text=True, timeout=5, check=True)
        self.assertTrue(probe.summarize(json.loads(completed.stdout))["capabilityPassed"])

    @unittest.skipUnless(os.name == "nt", "Static PowerShell parser is available on the Windows runner")
    def test_windows_script_parses_without_executing_or_reading_ui(self):
        script = "$tokens=$null; $errors=$null; [void][System.Management.Automation.Language.Parser]::ParseInput([Console]::In.ReadToEnd(),[ref]$tokens,[ref]$errors); if($errors.Count){exit 1}"
        for name in ("windows-snapshot.ps1", "windows-action.ps1", "windows-tree.ps1"):
            with self.subTest(name=name):
                subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script],
                               input=(HERE / name).read_text(), text=True, check=True, capture_output=True, timeout=10)


class Integration(unittest.TestCase):
    def test_both_real_installs_are_probed_even_if_client_capability_is_missing(self):
        with tempfile.TemporaryDirectory() as temporary:
            apps = {role: {"role": role, "results": Path(temporary) / role} for role in ("client", "unified")}
            observed = {"processIds": [42]}
            with patch.object(runner.lifecycle, "install_app", return_value={"passed": True}) as install, \
                    patch.object(runner.lifecycle, "observe_app", return_value=observed), \
                    patch.object(runner.probe, "capture", side_effect=[{"capabilityPassed": False}, {"capabilityPassed": True}]) as capture:
                report = {}
                with self.assertRaisesRegex(AssertionError, "Native accessibility capability"):
                    runner.exercise(apps, report)
            self.assertEqual(["client", "unified"], [call.args[0]["role"] for call in install.call_args_list])
            self.assertEqual(2, capture.call_count)
            self.assertFalse(report["nativeProbes"]["client"]["capabilityPassed"])

    def test_ambiguous_installed_pids_are_rejected_before_ui(self):
        with patch.object(runner.lifecycle, "install_app"), patch.object(runner.lifecycle, "observe_app", return_value={"processIds": [42, 43]}), \
                patch.object(runner.probe, "capture") as capture:
            with self.assertRaisesRegex(AssertionError, "one exact product process"):
                runner.exercise({"client": {"role": "client"}}, {})
        capture.assert_not_called()

    def test_provenance_version_architecture_and_success_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "provenance.json"
            value = {"passed": True, "rid": "osx-arm64", "version": "fixture", "packageSourceSHA": "d"*40}
            path.write_text(json.dumps(value))
            self.assertEqual("d"*40, runner.provenance(path, "osx-arm64", "fixture")["packageSourceSHA"])
            for changed in ({"passed": False}, {"rid": "win-x64"}, {"version": "other"}, {"packageSourceSHA": "invalid"}):
                path.write_text(json.dumps(dict(value, **changed)))
                with self.subTest(changed=changed), self.assertRaises(AssertionError):
                    runner.provenance(path, "osx-arm64", "fixture")


if __name__ == "__main__":
    unittest.main()
