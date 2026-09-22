#!/usr/bin/env python3
"""Pure direct-AX fixtures. Never invoke native UI or inspect local permission."""
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
SPEC = importlib.util.spec_from_file_location("direct_ax_test", HERE / "direct_ax.py")
diagnostic = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(diagnostic)
APP = {"role": "unified", "rid": "osx-x64", "exe": Path("/fixture/Bakabase")}
IDENTITY = {"pid": 42, "started": "owned-start", "executable": "/fixture/Bakabase"}


def reply(trusted=True, count=1):
    return {"readOnly": True, "trusted": trusted, "available": trusted and count > 0,
            "ownedWindowCount": count, "stage": "complete" if trusted else "check-existing-trust",
            "code": None if trusted and count > 0 else "DirectAXNoOwnedWindow" if trusted else "DirectAXNotTrusted",
            "diagnostics": {"windowCountObserved": trusted, "pidReadError": 0, "messagingTimeoutError": 0,
                            "roleReadError": 0, "windowsReadError": 0, "roleMatches": True,
                            "roleTypeId": 7, "expectedRoleTypeId": 7,
                            "windowsTypeId": 19, "expectedWindowsTypeId": 19}}


class DiagnosticBoundary(unittest.TestCase):
    def test_local_host_is_rejected_before_trust_check_or_identity_read(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(diagnostic.probe, "mac_identity") as identity, \
                patch.object(diagnostic.probe, "bounded_command") as command:
            with self.assertRaises(AssertionError):
                diagnostic.capture(APP, 42)
        identity.assert_not_called()
        command.assert_not_called()

    def test_unowned_pid_cannot_reach_direct_ax(self):
        with patch.object(diagnostic.probe, "hosted"), \
                patch.object(diagnostic.probe, "mac_identity", return_value=dict(IDENTITY, executable="/other/Bakabase")), \
                patch.object(diagnostic.probe, "bounded_command") as command:
            result = diagnostic.capture(APP, 42)
        self.assertEqual("DirectAXProcessMismatch", result["code"])
        self.assertFalse(result["available"])
        command.assert_not_called()

    def test_untrusted_is_recorded_without_failing_or_passing_the_workflow(self):
        with patch.object(diagnostic.probe, "hosted"), patch.object(diagnostic.probe, "mac_identity", return_value=IDENTITY), \
                patch.object(diagnostic.probe, "bounded_command", return_value=reply(False)) as command:
            result = diagnostic.capture(APP, 42)
        self.assertEqual("DirectAXNotTrusted", result["code"])
        self.assertFalse(result["trusted"])
        self.assertFalse(result["available"])
        self.assertFalse(result["mainFlowPassed"])
        self.assertEqual(5, command.call_args.args[2])

    def test_trusted_owned_windows_prove_only_api_availability(self):
        with patch.object(diagnostic.probe, "hosted"), patch.object(diagnostic.probe, "mac_identity", return_value=IDENTITY), \
                patch.object(diagnostic.probe, "bounded_command", return_value=dict(reply(), raw="private", title="private")):
            result = diagnostic.capture(APP, 42)
        self.assertTrue(result["available"])
        self.assertFalse(result["mainFlowPassed"])
        self.assertEqual(1, result["ownedWindowCount"])
        self.assertNotIn("private", json.dumps(result))

    def test_pid_reuse_after_diagnostic_invalidates_availability(self):
        with patch.object(diagnostic.probe, "hosted"), \
                patch.object(diagnostic.probe, "mac_identity", side_effect=[IDENTITY, dict(IDENTITY, started="new")]), \
                patch.object(diagnostic.probe, "bounded_command", return_value=reply()):
            result = diagnostic.capture(APP, 42)
        self.assertFalse(result["available"])
        self.assertEqual("ProductProcessChangedDuringProbe", result["code"])

    def test_timeout_unknown_errors_and_malformed_success_never_pass_or_leak(self):
        cases = [diagnostic.probe.ProbeFailure("NativeProbeTimedOut"), OSError("private"),
                 dict(reply(), code="private"), dict(reply(), ownedWindowCount=99), dict(reply(), trusted=1)]
        for case in cases:
            kwargs = {"side_effect": case} if isinstance(case, Exception) else {"return_value": case}
            with self.subTest(case=type(case).__name__), patch.object(diagnostic.probe, "hosted"), \
                    patch.object(diagnostic.probe, "mac_identity", return_value=IDENTITY), \
                    patch.object(diagnostic.probe, "bounded_command", **kwargs):
                result = diagnostic.capture(APP, 42)
                self.assertFalse(result["available"])
                self.assertNotIn("private", json.dumps(result))

    def test_fixed_numeric_diagnostics_survive_without_native_values_or_unknown_fields(self):
        raw = dict(reply(), available=False, code="DirectAXRoleReadFailed", stage="read-owned-application-role",
                   diagnostics={"windowCountObserved": False, "roleReadError": -25204,
                                "roleRawTypeId": 18, "roleTypeId": 7, "roleValueWasRef": True,
                                "nativeValue": "private", "private": "private"})
        with patch.object(diagnostic.probe, "hosted"), patch.object(diagnostic.probe, "mac_identity", return_value=IDENTITY), \
                patch.object(diagnostic.probe, "bounded_command", return_value=raw):
            result = diagnostic.capture(APP, 42)
        self.assertEqual(-25204, result["diagnostics"]["roleReadError"])
        self.assertEqual(18, result["diagnostics"]["roleRawTypeId"])
        self.assertTrue(result["diagnostics"]["roleValueWasRef"])
        self.assertFalse(result["diagnostics"]["windowCountObserved"])
        self.assertFalse(result["available"])
        self.assertNotIn("private", json.dumps(result))

    def test_invalid_diagnostics_or_unobserved_window_count_cannot_be_success(self):
        for key, value in [("roleReadError", "private"), ("roleReadError", True), ("roleReadError", -1),
                           ("roleTypeId", -1), ("roleTypeId", 2**32), ("roleMatches", False),
                           ("windowCountObserved", False), ("roleValueWasRef", "private")]:
            raw = reply()
            raw["diagnostics"][key] = value
            with self.subTest(key=key, value=value), patch.object(diagnostic.probe, "hosted"), \
                    patch.object(diagnostic.probe, "mac_identity", return_value=IDENTITY), \
                    patch.object(diagnostic.probe, "bounded_command", return_value=raw):
                result = diagnostic.capture(APP, 42)
            self.assertFalse(result["available"])
            self.assertEqual("DirectAXDiagnosticUnavailable", result["code"])
            self.assertNotIn("private", json.dumps(result))


class NativeApiFixture(unittest.TestCase):
    @unittest.skipUnless(shutil.which("node"), "Node is required for a fake native API fixture")
    def test_nonprompting_api_reads_only_owned_application_metadata_after_trust(self):
        fixture = r'''
const fs=require('fs'),vm=require('vm'),source=fs.readFileSync(process.argv[1],'utf8')+'\n'+fs.readFileSync(process.argv[2],'utf8');
function run(options={}) {
 function Ref(value){if(!(this instanceof Ref))return new Ref(value);this.target=value}
 const calls=[];const native=name=>name;
 Object.assign(native,{
  AXIsProcessTrusted:()=>{calls.push('trust');return options.trusted!==false},
  AXUIElementCreateApplication:pid=>{if(pid!==42)throw Error('other pid');calls.push('create');return {pid}},
  AXUIElementGetPid:(app,ref)=>{ref[0]=options.wrongPid?99:app.pid;return 0},
  AXUIElementSetMessagingTimeout:(app,seconds)=>{if(app.pid!==42||seconds!==0.5)throw Error('bad bound');return 0},
  AXUIElementCopyAttributeValue:(app,name,ref)=>{
   if(app.pid!==42||!['AXRole','AXWindows'].includes(name))throw Error('Forbidden attribute');calls.push(name);
   if(name==='AXRole'&&options.roleError)return options.roleError;
   if(name==='AXWindows'&&options.windowsError)return options.windowsError;
   const value=name==='AXRole'?{kind:options.roleType??1,value:options.role??'AXApplication'}:
    {kind:options.windowsType??2,count:options.count??1};
   ref[0]=options.boxed===false?value:new Ref(value);return 0;
  },CFGetTypeID:value=>value instanceof Ref?18:value.kind,CFStringGetTypeID:()=>1,CFArrayGetTypeID:()=>2,
  CFArrayGetCount:value=>{if(value instanceof Ref)throw Error('Must unwrap array pointer');return value.count}
 });
 const output=JSON.parse(vm.runInNewContext('const input={pid:42};\n'+source,{
  $:native,Ref,ObjC:{import:()=>{},unwrap:value=>value instanceof Ref?value:value.value,
   castRefToObject:value=>{if(!(value instanceof Ref))throw Error('Not a Ref');return value.target}}
 }));
 return {output,calls};
}
let r=run({trusted:false});if(r.output.trusted!==false||r.output.available||r.calls.join()!=='trust')throw Error('Read before trust');
r=run({wrongPid:true});if(r.output.available||r.calls.some(x=>x.startsWith('AX')))throw Error('Read foreign process');
r=run();if(!r.output.available||r.output.ownedWindowCount!==1||r.calls.join()!=='trust,create,AXRole,AXWindows')throw Error('Missing bounded capability');
if(r.output.diagnostics.roleRawTypeId!==18||r.output.diagnostics.roleTypeId!==1||!r.output.diagnostics.roleValueWasRef||
 !r.output.diagnostics.windowCountObserved)throw Error('Missing bridge evidence');
r=run({boxed:false});if(!r.output.available||r.output.diagnostics.roleValueWasRef)throw Error('Wrapped object rejected');
r=run({roleError:-25204});if(r.output.available||r.output.code!=='DirectAXRoleReadFailed'||r.output.diagnostics.roleReadError!==-25204||r.calls.includes('AXWindows'))throw Error('AX error hidden');
r=run({roleType:2});if(r.output.available||r.output.code!=='DirectAXRoleTypeMismatch'||r.calls.includes('AXWindows'))throw Error('Wrong role type accepted');
r=run({role:'AXWindow'});if(r.output.available||r.output.code!=='DirectAXRoleMismatch'||r.output.diagnostics.roleMatches!==false)throw Error('Wrong role accepted');
r=run({windowsError:-25205});if(r.output.available||r.output.code!=='DirectAXWindowsReadFailed'||r.output.diagnostics.windowCountObserved)throw Error('Window error hidden');
r=run({windowsType:1});if(r.output.available||r.output.code!=='DirectAXWindowsTypeMismatch'||r.output.diagnostics.windowCountObserved)throw Error('Wrong window type accepted');
r=run({count:0});if(r.output.available||r.output.code!=='DirectAXNoOwnedWindow')throw Error('Empty window passed');
r=run({count:9});if(r.output.available||r.output.code!=='DirectAXWindowBudgetExceeded')throw Error('Window bound ignored');
'''
        subprocess.run([shutil.which("node"), "-e", fixture, str(HERE / "macos-cf-values.js"), str(HERE / "macos-direct-ax.js")],
                       capture_output=True, text=True, timeout=5, check=True)

    @unittest.skipUnless(sys.platform == "darwin", "Pure CoreFoundation bridge is macOS only")
    def test_real_corefoundation_values_require_reference_conversion_without_any_ui_api(self):
        # Real JXA ABI regression using only objects allocated in this process.
        # No ApplicationServices, applications, accessibility or permission read.
        source = "ObjC.import('CoreFoundation');\n" + (HERE / "macos-cf-values.js").read_text() + r'''
const rawString=$.CFStringCreateWithCString(null,'AXApplication',$.kCFStringEncodingUTF8);
const string=cfValue(rawString), ordinary=cfValue($('AXApplication'));
const array=cfValue($.CFArrayCreate(null,null,0,null));
JSON.stringify({stringWasRef:string.wasRef,stringTypeId:string.typeId,expectedStringTypeId:Number($.CFStringGetTypeID()),
 stringMatches:ObjC.unwrap(string.object)==='AXApplication',ordinaryWasRef:ordinary.wasRef,
 ordinaryMatches:ObjC.unwrap(ordinary.object)==='AXApplication',arrayWasRef:array.wasRef,
 arrayTypeId:array.typeId,expectedArrayTypeId:Number($.CFArrayGetTypeID()),arrayCount:Number($.CFArrayGetCount(array.object))});
'''
        process = subprocess.run(["/usr/bin/osascript", "-l", "JavaScript", "-"], input=source,
                                 capture_output=True, text=True, timeout=5, check=True)
        evidence = json.loads(process.stdout)
        self.assertTrue(evidence["stringWasRef"])
        self.assertEqual(evidence["expectedStringTypeId"], evidence["stringTypeId"])
        self.assertTrue(evidence["stringMatches"])
        self.assertFalse(evidence["ordinaryWasRef"])
        self.assertTrue(evidence["ordinaryMatches"])
        self.assertTrue(evidence["arrayWasRef"])
        self.assertEqual(evidence["expectedArrayTypeId"], evidence["arrayTypeId"])
        self.assertEqual(0, evidence["arrayCount"])


class ReportIntegration(unittest.TestCase):
    def test_direct_ax_is_retained_as_diagnostic_and_cannot_override_native_flow_gate(self):
        spec = importlib.util.spec_from_file_location("direct_ax_integration_runner", HERE / "run-probe.py")
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        with tempfile.TemporaryDirectory() as temporary:
            apps = {role: {"role": role, "rid": "osx-x64", "results": Path(temporary) / role}
                    for role in ("client", "unified")}
            for app in apps.values():
                app["results"].mkdir()
            report = {"mainFlowPassed": False}
            with patch.object(runner.lifecycle, "install_app", return_value={"passed": True}), \
                    patch.object(runner.lifecycle, "observe_app", return_value={"processIds": [42]}), \
                    patch.object(runner.lifecycle, "require_same_process"), \
                    patch.object(runner.probe, "capture", side_effect=[{"capabilityPassed": True}, {"capabilityPassed": False}]), \
                    patch.object(runner.direct_ax, "capture", return_value=reply()):
                with self.assertRaisesRegex(AssertionError, "Native accessibility capability"):
                    runner.exercise(apps, report)
            self.assertFalse(report["mainFlowPassed"])
            self.assertFalse(report.get("capabilityPassed", False))
            self.assertTrue(report["directAXCapabilities"]["unified"]["available"])
            self.assertTrue((apps["unified"]["results"] / "direct-ax.json").is_file())


if __name__ == "__main__":
    unittest.main()
