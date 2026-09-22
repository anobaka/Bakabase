#!/usr/bin/env python3
"""Pure authorization guards. Never create users, drive UI, or execute an updater."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import traceback
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("installed_macos_authorization", Path(__file__).with_name("installed-macos-authorization.py"))
helper = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(helper)


def node(role, title="", *, subrole="", text="", path=None):
    return {"role": role, "title": title, "subrole": subrole, "text": text,
            "description": "", "enabled": True, "path": path or [0, 0]}


def confirm_view(title="Bakabase", version="0.0.2-updater.1.1"):
    return {"pid": 40, "scope": "updater-focused-window", "originalWindowCount": 2, "focusedWindowAvailable": True,
        "windows": [{"index": "focused", "nodes": [
        node("AXWindow", title + " Update"), node("AXStaticText", text="Administrator Permission Required"),
        node("AXStaticText", text=f"{title} needs administrator permission to install version {version}. Allow this update to continue?"),
        node("AXButton", "Install Update", path=["focused", 2]), node("AXButton", "Cancel", path=["focused", 3])]}]}


def system_view():
    return {"pid": 99, "windows": [{"index": 0, "nodes": [
        node("AXStaticText", text="osascript wants to make changes."),
        node("AXStaticText", text="Enter an administrator’s name and password to allow this."),
        node("AXTextField", "Username", path=[0, 2]),
        node("AXTextField", "Password", subrole="AXSecureTextField", path=[0, 3]),
        node("AXButton", "OK", path=[0, 4]), node("AXButton", "Cancel", path=[0, 5])]}]}


def proc(pid, parent=1, path="/usr/bin/osascript", started="test", uid=501):
    return {"pid": pid, "parentPid": parent, "path": path, "started": started, "uid": uid}


def apps():
    return {role: {"role": role, "rid": "osx-arm64", "installRoot": Path("/Applications") / bundle,
                   "exe": Path("/Applications") / bundle / "Contents/MacOS" / executable}
            for role, bundle, executable in (("unified", "Bakabase.app", "Bakabase"),
                                             ("client", "Bakabase Client.app", "Bakabase.Client"))}


class AuthorizationGuards(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.context = helper.Context(apps(), Path(self.temp.name))
        self.context.evidence["prepared"] = True
        self.context.creation_attempted = True
        self.app = self.context.apps["unified"]
        self.updater = proc(40, path=str(self.app["exe"].parent / "UpdateMac"))
        self.child = proc(41, parent=40)
        self.system = proc(99, path=sorted(helper.SECURITY_AGENTS)[0])
        self.version = "0.0.2-updater.1.1"
        permissions = patch.object(helper, "permission_snapshot", return_value={"fixture": True})
        permissions.start()
        self.addCleanup(permissions.stop)
        # Failure diagnostics are tested separately with fake log streams. Pure
        # authorization fixtures must never query real hosted system logs.
        diagnostics = patch.object(helper, "capture_authentication_diagnostics")
        self.auth_diagnostics = diagnostics.start()
        self.addCleanup(diagnostics.stop)

    def test_prepare_rejects_non_hosted_before_any_system_or_ui_action(self):
        with patch.dict(helper.os.environ, {}, clear=True), patch.object(helper, "run") as run, \
                patch.object(helper, "ui") as ui, patch.object(helper, "create_account") as create:
            with self.assertRaisesRegex(AssertionError, "GitHub-hosted"):
                helper.prepare(apps(), self.temp.name)
            run.assert_not_called()
            ui.assert_not_called()
            create.assert_not_called()

    def test_jxa_preflight_uses_real_dictionary_property_with_a_pure_application_fixture(self):
        # Evaluate the exact JXA against a dictionary fixture, never System Events.
        # The installed acceptance workflow already provisions Node 22.
        script = """
const console = {log: () => {}};
const Application = name => {
  if (name !== 'System Events') throw Error('Unknown application');
  return new Proxy({uiElementsEnabled: () => true}, {
    get(target, key) { if (!(key in target)) throw Error('Unknown dictionary property: ' + key); return target[key]; }
  });
};
const data = {operation: 'preflight'};
process.stdout.write(String(eval(SOURCE)));
""".replace("SOURCE", json.dumps(helper.JXA))
        result = subprocess.run(["node", "-e", script], text=True, capture_output=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual({"enabled": True}, json.loads(result.stdout))

    def focused_fixture(self, scenario):
        # Execute the exact JXA against two distinct same-title window references.
        # Focus is supplied by the fixture's AXFocusedWindow, never their order.
        script = r'''
const console = {log: () => {}};
const clicks = [], source = SOURCE, scenario = SCENARIO, fixtureSecret = FIXTURE_SECRET;
let focusFlagReads = 0, loseFocusAfterRead = null, windowTreeReads = 0;
function element(attrs, children = [], tag = '') {
  return {attributes: {byName: key => ({value: () => {
    if (key === 'AXFocused' && attrs.AXRole === 'AXWindow') {
      focusFlagReads++;
      if (loseFocusAfterRead !== null && focusFlagReads > loseFocusAfterRead) return false;
    }
    return attrs[key] ?? '';
  }})},
    uiElements: () => { if (attrs.AXRole === 'AXWindow') windowTreeReads++; return children; }, click: () => {
      if (scenario === 'press-fails' && tag === 'focused:install') throw Error(fixtureSecret + ' (-1719)');
      clicks.push(tag);
    }};
}
function window(tag, body = 'Bakabase needs administrator permission to install version new. Allow this update to continue?') {
  const staticText = text => element({AXRole:'AXStaticText', AXValue:text, AXEnabled:true});
  return element({AXRole:'AXWindow', AXTitle:'Bakabase Update', AXFocused:true}, [
    staticText('Administrator Permission Required'), staticText(body),
    element({AXRole:'AXButton', AXTitle:'Cancel', AXEnabled:true}, [], tag + ':cancel'),
    element({AXRole:'AXButton', AXTitle:'Install Update', AXEnabled:true}, [], tag + ':install')]);
}
const first = window('first'), second = window('focused'), changed = window('changed', 'Different operation');
const windows = [first, second];
let focus = scenario === 'no-focus' ? null : second, focusReads = 0;
const processFixture = {unixId: () => 40, windows: () => windows,
  attributes: {byName: key => ({value: () => {
    if (key !== 'AXFocusedWindow') throw Error('Unknown process attribute');
    focusReads++;
    return focus;
  }})}};
const Application = name => ({processes: {whose: query => () => {
  if (name !== 'System Events' || query.unixId !== 40) throw Error('Wrong PID');
  return scenario === 'process-exited' ? [] : [processFixture];
}}});
let data = {operation:'snapshot', pid:40};
if (scenario !== 'security-all-windows') data.scope = 'updater-focused-window';
const snapshot = JSON.parse(eval(source));
let response = null, error = null;
if (!['no-focus', 'process-exited', 'security-all-windows'].includes(scenario)) {
  if (scenario === 'changed-before-action') focus = changed;
  if (scenario === 'changed-before-click') loseFocusAfterRead = focusFlagReads + 2;
  const button = snapshot.windows[0].nodes.find(node => node.title === 'Install Update');
  data = {operation:'confirm', scope:'updater-focused-window', pid:40, expected:snapshot, buttonPath:button.path, secret:fixtureSecret};
  if (scenario === 'control-missing') data.buttonPath = ['focused', 999];
  try { response = JSON.parse(eval(source));
    if (response.helperError) { error = response.helperError; response = null; }
  } catch (_) { error = {fixtureRuntimeFailure:true}; }
}
process.stdout.write(JSON.stringify({snapshot, response, error, clicks, focusReads, windowTreeReads}));
'''.replace("SOURCE", json.dumps(helper.JXA)).replace("SCENARIO", json.dumps(scenario)).replace("FIXTURE_SECRET", json.dumps(self.context.secret))
        result = subprocess.run(["node", "-e", script], text=True, capture_output=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        return json.loads(result.stdout)

    def test_updater_uses_os_focused_window_with_same_title_other_window_present(self):
        result = self.focused_fixture("same-title")
        self.assertEqual(2, result["snapshot"]["originalWindowCount"])
        self.assertEqual("focused", result["snapshot"]["windows"][0]["index"])
        self.assertEqual(["focused:install"], result["clicks"])
        self.assertEqual(2, result["focusReads"])
        self.assertEqual(2, result["windowTreeReads"], "Confirmation should traverse the current dialog once after the original snapshot")
        self.assertEqual({"submitted": True}, result["response"])

    def credential_fixture(self, scenario):
        script = r'''
const console = {log: () => {}}, source = SOURCE, scenario = SCENARIO, secret = SECRET;
const writes = [], clicks = []; let passwordReads = 0;
function element(attrs, children = [], tag = '') {
  const value = {attributes:{byName:key => ({
    value:() => {
      if (key === 'AXValue' && tag === 'password') { passwordReads++; throw Error(secret); }
      if (key === 'AXValue' && tag === 'username' && scenario === 'username-read-error') throw Error(secret + ' (-25205)');
      return attrs[key] ?? '';
    },
    settable:() => key === 'AXValue' && scenario !== tag + '-not-writable'
  })}, uiElements:() => children, click:() => clicks.push(tag)};
  Object.defineProperty(value, 'value', {set:next => {
    writes.push(tag);
    if (!(tag === 'username' && scenario === 'username-ignored')) attrs.AXValue = next;
  }});
  return value;
}
const username = element({AXRole:'AXTextField',AXValue:'initial-user',AXEnabled:true}, [], 'username');
const password = element({AXRole:'AXTextField',AXSubrole:'AXSecureTextField',AXEnabled:true}, [], 'password');
const button = element({AXRole:'AXButton',AXTitle:'OK',AXEnabled:true}, [], 'ok');
const window = element({AXRole:'AXWindow'}, [username,password,button]);
const Application = name => ({processes:{whose:query => () => {
  if (name !== 'System Events' || query.unixId !== 99) throw Error('Wrong fixture process');
  return [{windows:() => [window]}];
}}});
let data = {operation:'snapshot',pid:99};
const expected = JSON.parse(eval(source));
data = {operation:'authorize',pid:99,expected,username:'bbci_fixture',secret,
  usernamePath:[0,0],passwordPath:[0,1],buttonPath:[0,2]};
const response = JSON.parse(eval(source));
process.stdout.write(JSON.stringify({response,writes,clicks,passwordReads}));
'''.replace("SOURCE", json.dumps(helper.JXA)).replace("SCENARIO", json.dumps(scenario)).replace("SECRET", json.dumps(self.context.secret))
        result = subprocess.run(["node", "-e", script], text=True, capture_output=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn(self.context.secret, result.stdout)
        self.assertNotIn("initial-user", result.stdout)
        self.assertNotIn("bbci_fixture", result.stdout)
        return json.loads(result.stdout)

    def test_credentials_verify_username_and_write_password_once_without_reading_it(self):
        result = self.credential_fixture("success")
        self.assertEqual({"submitted": True}, result["response"])
        self.assertEqual(["username", "password"], result["writes"])
        self.assertEqual(["ok"], result["clicks"])
        self.assertEqual(0, result["passwordReads"])

    def test_unwritable_fields_fail_before_any_credential_write_or_click(self):
        for scenario in ("username-not-writable", "password-not-writable"):
            result = self.credential_fixture(scenario)
            self.assertEqual({"stage": "verify-credential-controls", "code": "CredentialFieldNotWritable"}, result["response"]["helperError"])
            self.assertEqual([], result["writes"])
            self.assertEqual([], result["clicks"])
            self.assertEqual(0, result["passwordReads"])

    def test_username_write_or_read_failure_prevents_password_and_button_action(self):
        for scenario in ("username-ignored", "username-read-error"):
            result = self.credential_fixture(scenario)
            error = result["response"]["helperError"]
            self.assertEqual("verify-username", error["stage"])
            self.assertEqual("UsernameValueNotApplied" if scenario == "username-ignored" else "SystemCallFailed", error["code"])
            helper.safe_ui_diagnostic(error)
            self.assertEqual(["username"], result["writes"])
            self.assertEqual([], result["clicks"])
            self.assertEqual(0, result["passwordReads"])

    def test_missing_focus_never_falls_back_to_an_enumerated_window(self):
        result = self.focused_fixture("no-focus")
        self.assertEqual(2, result["snapshot"]["originalWindowCount"])
        self.assertFalse(result["snapshot"]["focusedWindowAvailable"])
        self.assertEqual([], result["snapshot"]["windows"])
        self.assertEqual([], result["clicks"])
        self.assertIsNone(helper.confirmation(result["snapshot"], "Bakabase", "new"))

    def test_updater_disappearing_between_pid_sample_and_ui_read_is_an_empty_focused_snapshot(self):
        result = self.focused_fixture("process-exited")
        self.assertEqual("updater-focused-window", result["snapshot"]["scope"])
        self.assertEqual(0, result["snapshot"]["originalWindowCount"])
        self.assertFalse(result["snapshot"]["focusedWindowAvailable"])
        self.assertEqual([], result["snapshot"]["windows"])
        self.assertEqual([], result["clicks"])
        self.assertIsNone(helper.confirmation(result["snapshot"], "Bakabase", "new"))

    def test_changed_focus_before_action_or_before_click_never_submits(self):
        for scenario in ("changed-before-action", "changed-before-click"):
            with self.subTest(scenario=scenario):
                result = self.focused_fixture(scenario)
                self.assertEqual([], result["clicks"])
                self.assertIsNotNone(result["error"])
                self.assertIsNone(result["response"])
                stage = "validate-snapshot" if scenario == "changed-before-action" else "revalidate-focused-reference"
                self.assertEqual(stage, result["error"]["stage"])
                if scenario == "changed-before-action":
                    difference = result["error"]["snapshotDifference"]
                    self.assertIn("windows[0].nodes[2].text", difference["fields"])
                else:
                    self.assertEqual("FocusChangedBeforePress", result["error"]["code"])
                self.assertNotIn("Different operation", json.dumps(result["error"]))
                self.assertNotIn(self.context.secret, json.dumps(result["error"]))

    def test_failed_press_returns_only_stage_fixed_code_and_numeric_error_suffix(self):
        result = self.focused_fixture("press-fails")
        self.assertEqual([], result["clicks"])
        self.assertEqual({"stage": "press-button", "code": "SystemCallFailed", "systemErrorNumber": -1719}, result["error"])
        self.assertNotIn(self.context.secret, json.dumps(result))
        with patch.object(helper, "run", return_value=json.dumps({"helperError": result["error"]})):
            with self.assertRaisesRegex(helper.AuthorizationFailure, "press-button") as raised:
                helper.ui({"operation": "confirm", "secret": self.context.secret})
        self.assertNotIn(self.context.secret, str(raised.exception))

    def test_control_lookup_failure_is_distinct_from_press_failure(self):
        result = self.focused_fixture("control-missing")
        self.assertEqual([], result["clicks"])
        self.assertEqual({"stage": "resolve-control", "code": "AmbiguousControl"}, result["error"])
        self.assertNotIn(self.context.secret, json.dumps(result))

    def test_python_rejects_diagnostic_strings_or_fields_outside_the_fixed_schema(self):
        for diagnostic in ({"stage": self.context.secret, "code": "SystemCallFailed"},
                           {"stage": "press-button", "code": "SystemCallFailed", "message": self.context.secret},
                           {"stage": "press-button", "code": "SystemCallFailed", "systemErrorNumber": self.context.secret},
                           {"stage": "validate-snapshot", "code": "SnapshotChanged", "snapshotDifference": {
                               "fields": [self.context.secret], "expectedWindowCount": 1, "actualWindowCount": 1,
                               "expectedNodeCounts": [5], "actualNodeCounts": [5]}}):
            with self.subTest(diagnostic=diagnostic), patch.object(helper, "run", return_value=json.dumps({"helperError": diagnostic})):
                with self.assertRaisesRegex(helper.AuthorizationFailure, "invalid diagnostic") as raised:
                    helper.ui({"operation": "authorize", "secret": self.context.secret})
                self.assertNotIn(self.context.secret, str(raised.exception))

    def test_system_authorization_still_enumerates_and_rejects_multiple_windows(self):
        result = self.focused_fixture("security-all-windows")
        self.assertEqual(2, len(result["snapshot"]["windows"]))
        self.assertNotIn("scope", result["snapshot"])
        with self.assertRaisesRegex(helper.AuthorizationFailure, "not unique"):
            helper.authorization_controls(result["snapshot"])

    def test_prepare_rejects_windows_even_if_base_host_guard_accepts(self):
        values = apps()
        for app in values.values():
            app["rid"] = "win-x64"
        with patch.object(helper.base, "require_hosted_runner"), patch.object(helper, "run") as run:
            with self.assertRaisesRegex(AssertionError, "macOS authorization only"):
                helper.prepare(values, self.temp.name)
            run.assert_not_called()

    def test_failed_prepare_retains_context_and_failed_cleanup_without_disclosing_password(self):
        failure = {"passed": False, "canRemoveOwnedPaths": False, "remainingProcesses": [{"pid": 123}]}
        secret = self.context.secret
        with patch.object(helper.base, "require_hosted_runner"), patch.object(helper, "run", return_value="501"), \
                patch.object(helper.os, "getuid", return_value=501, create=True), patch.object(helper, "ui", return_value={"enabled": True}), \
                patch.object(helper, "process_table", return_value={}), patch.object(helper, "Context", return_value=self.context), \
                patch.object(helper, "create_account", side_effect=ValueError(secret)), \
                patch.object(helper, "cleanup", return_value=failure) as cleanup:
            try:
                helper.prepare(self.context.apps, self.temp.name)
            except helper.PreparationFailure as error:
                self.assertIs(self.context, error.context)
                self.assertEqual(failure, error.context.evidence["preparationCleanup"])
                self.assertNotIn(secret, "".join(traceback.format_exception(type(error), error, error.__traceback__)))
            else:
                self.fail("Preparation failure was incorrectly reported as success")
        cleanup.assert_called_once_with(self.context)

    def test_cleanup_exception_during_prepare_still_preserves_a_blocking_context(self):
        with patch.object(helper.base, "require_hosted_runner"), patch.object(helper, "run", return_value="501"), \
                patch.object(helper.os, "getuid", return_value=501, create=True), patch.object(helper, "ui", return_value={"enabled": True}), \
                patch.object(helper, "process_table", return_value={}), patch.object(helper, "Context", return_value=self.context), \
                patch.object(helper, "create_account", side_effect=KeyboardInterrupt), \
                patch.object(helper, "cleanup", side_effect=OSError("private account-tool error")):
            with self.assertRaises(helper.PreparationFailure) as raised:
                helper.prepare(self.context.apps, self.temp.name)
        context = raised.exception.context
        self.assertIsNone(context.secret)
        self.assertFalse(context.evidence["preparationCleanup"]["canRemoveOwnedPaths"])
        self.assertEqual("OSError", context.evidence["preparationCleanup"]["error"])
        self.assertNotIn("private", json.dumps(context.evidence))

    def test_password_verification_uses_only_stdin_and_records_success(self):
        with patch.object(helper, "run", return_value='{"verified":true}') as run:
            helper.verify_account_password(self.context, 3)
        self.assertEqual(3, run.call_args.kwargs["timeout"])
        self.assertNotIn(self.context.secret, " ".join(str(a) for a in run.call_args.args[0]))
        self.assertEqual({"username": self.context.username, "uid": self.context.uid, "secret": self.context.secret},
                         json.loads(run.call_args.kwargs["input_text"]))
        self.assertEqual({"verified": True}, self.context.evidence["fixturePasswordVerification"])
        self.assertNotIn(self.context.secret, json.dumps(self.context.evidence))

    def test_password_verification_failure_retains_cleanup_context_before_prepared(self):
        self.context.evidence["prepared"] = False
        replies = iter(["501", '{"verified":false,"error":"PasswordVerificationFailed","nativeErrorCode":5200,"phase":"verify-password","exceptionKind":"AccountFailure"}'])
        with patch.object(helper.base, "require_hosted_runner"), patch.object(helper, "run", side_effect=lambda *_a, **_k: next(replies)), \
                patch.object(helper.os, "getuid", return_value=501, create=True), patch.object(helper, "ui", return_value={"enabled": True}), \
                patch.object(helper, "process_table", return_value={}), patch.object(helper, "Context", return_value=self.context), \
                patch.object(helper, "create_account"), patch.object(helper, "cleanup", return_value={"passed": True}) as cleanup:
            with self.assertRaises(helper.PreparationFailure) as raised:
                helper.prepare(self.context.apps, self.temp.name)
        self.assertIs(self.context, raised.exception.context)
        self.assertFalse(self.context.evidence["prepared"])
        self.assertFalse(self.context.evidence["fixturePasswordVerification"]["verified"])
        self.assertEqual("verify-password", self.context.evidence["fixturePasswordVerification"]["phase"])
        cleanup.assert_called_once_with(self.context)

    def test_password_verification_rejects_unknown_evidence_without_recording_secret(self):
        for response in ({"verified": False, "error": self.context.secret},
                         {"verified": False, "message": self.context.secret},
                         {"verified": False, "phase": self.context.secret},
                         {"verified": False, "exceptionKind": self.context.secret},
                         {"verified": False, "error": "PasswordVerificationFailed", "nativeErrorCode": self.context.secret}):
            with self.subTest(response=response), patch.object(helper, "run", return_value=json.dumps(response)):
                with self.assertRaisesRegex(helper.AuthorizationFailure, "invalid evidence") as raised:
                    helper.verify_account_password(self.context, 5)
            self.assertNotIn(self.context.secret, str(raised.exception))
            self.assertNotIn(self.context.secret, json.dumps(self.context.evidence))

    def test_confirmation_requires_exact_product_version_header_and_named_enabled_button(self):
        view = confirm_view()
        self.assertEqual(["focused", 2], helper.confirmation(view, "Bakabase", self.version))
        self.assertIsNone(helper.confirmation(view, "Bakabase Client", self.version))
        self.assertIsNone(helper.confirmation(view, "Bakabase", "0.0.2-updater.1"))
        bad = copy.deepcopy(view)
        bad["windows"][0]["nodes"][3]["enabled"] = False
        self.assertIsNone(helper.confirmation(bad, "Bakabase", self.version))
        duplicate = copy.deepcopy(view)
        duplicate["windows"].append(copy.deepcopy(view["windows"][0]))
        with self.assertRaisesRegex(helper.AuthorizationFailure, "Ambiguous"):
            helper.confirmation(duplicate, "Bakabase", self.version)

    def test_system_dialog_requires_requester_admin_fields_and_one_enabled_ok(self):
        view = system_view()
        self.assertEqual([0, 3], helper.authorization_controls(view)["passwordPath"])
        current_macos = copy.deepcopy(view)
        current_macos["windows"][0]["nodes"][1]["text"] = "Enter your password to allow this."
        self.assertEqual([0, 3], helper.authorization_controls(current_macos)["passwordPath"])
        unrelated = copy.deepcopy(current_macos)
        unrelated["windows"][0]["nodes"][1]["text"] = "Enter your password to unlock the keychain."
        with self.assertRaises(helper.AuthorizationFailure):
            helper.authorization_controls(unrelated)
        for mutation in ("requester", "password", "duplicate", "disabled", "secondWindow"):
            bad = copy.deepcopy(view)
            nodes = bad["windows"][0]["nodes"]
            if mutation == "requester": nodes[0]["text"] = "Other app wants to make changes."
            elif mutation == "password": nodes[3]["subrole"] = ""
            elif mutation == "duplicate": nodes.append(copy.deepcopy(nodes[4]))
            elif mutation == "disabled": nodes[4]["enabled"] = False
            else: bad["windows"].append(copy.deepcopy(bad["windows"][0]))
            with self.subTest(mutation=mutation), self.assertRaises(helper.AuthorizationFailure):
                helper.authorization_controls(bad)

    def command(self, root=None, cache=None):
        root = root or self.app["installRoot"]
        cache = cache or Path.home() / "Library/Caches/velopack/Bakabase/packages"
        old, new = cache / "VelopackTemp/tmp_0123456789abcdef", cache / "VelopackTemp/tmp_fedcba9876543210"
        shell = f"mv -f '{root}' '{old}' && mv -f '{new}' '{root}' && rm -rf '{old}'"
        return '/usr/bin/osascript -e do shell script "' + shell + '" with administrator privileges'

    def test_only_exact_vendor_operations_in_owned_root_and_default_cache_are_accepted(self):
        root, cache = self.app["installRoot"], Path.home() / "Library/Caches/velopack/Bakabase/packages"
        command = self.command()
        self.assertTrue(helper.validate_elevation_script(command, root, cache).startswith("mv -f "))
        for wrong in (command.replace(str(root), str(root.parent / "Other.app")),
                      command.replace(str(cache), str(cache.parent.parent / "Bakabase.Client/packages")),
                      command.replace("tmp_0123456789abcdef", "../elsewhere"),
                      command.replace("mv -f", "sudo mv -f", 1), command + "; evil", command.replace(" && rm", " && chmod -R 777 / && rm")):
            with self.subTest(command=wrong), self.assertRaises(helper.AuthorizationFailure):
                helper.validate_elevation_script(wrong, root, cache)

    def test_competing_osascript_is_rejected_not_ignored(self):
        helper.require_no_competing_script({41: self.child}, 41)
        with self.assertRaisesRegex(helper.AuthorizationFailure, "Another osascript"):
            helper.require_no_competing_script({41: self.child, 77: proc(77)}, 41)

    def test_credentials_are_only_in_stdin_and_never_in_report_or_argv(self):
        with patch.object(helper, "run", return_value='{"submitted":true}') as run:
            helper.ui({"operation": "authorize", "secret": self.context.secret})
        arguments = run.call_args.args[0]
        self.assertNotIn(self.context.secret, " ".join(arguments))
        self.assertIn(self.context.secret, run.call_args.kwargs["input_text"])
        self.context.save()
        self.assertNotIn(self.context.secret, (Path(self.temp.name) / "macos-authorization.json").read_text())
        self.assertNotIn("keystroke", helper.JXA)

    def test_dialog_evidence_never_includes_username_or_password_field_values(self):
        view = system_view()
        for node in view["windows"][0]["nodes"]:
            if node["role"] == "AXTextField":
                node.update(value=self.context.secret, text=self.context.secret)
        self.assertNotIn(self.context.secret, json.dumps(helper.dialog_evidence(view)))

    def test_cleanup_tracking_excludes_automatic_app_and_webkit_but_keeps_file_writers(self):
        table = {40: self.updater, 41: self.child,
                 42: proc(42, parent=41, path="/bin/sh", uid=0),
                 43: proc(43, parent=42, path="/bin/mv", uid=0),
                 50: proc(50, parent=40, path="/usr/bin/open"),
                 51: proc(51, parent=50, path=str(self.app["exe"])),
                 52: proc(52, parent=51, path="WebKit")}
        helper.track(self.context, table, 40)
        self.assertEqual({40, 41, 42, 43}, {key[0] for key in self.context.tracked})

    def tracked_shell_fixture(self):
        cache = Path.home() / "Library/Caches/velopack/Bakabase/packages"
        shell = helper.validate_elevation_script(self.command(), self.app["installRoot"], cache)
        self.context.shells = {shell}
        record = proc(42, path="/bin/sh", uid=0)
        return record, "/bin/sh -c " + shell

    def test_shell_command_nonzero_is_ignored_only_after_fresh_exit_or_pid_reuse_confirmation(self):
        record, command = self.tracked_shell_fixture()
        for latest in ({}, {42: dict(record, started="different-start")}):
            with self.subTest(latest=latest), \
                    patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=1, stdout=command, stderr=self.context.secret)) as query, \
                    patch.object(helper, "process_table", return_value=latest) as sample:
                helper.track(self.context, {42: record})
            self.assertEqual({}, self.context.tracked)
            sample.assert_called_once_with()
            query.assert_called_once_with(["/bin/ps", "-ww", "-p", "42", "-o", "command="],
                                          text=True, capture_output=True, timeout=10)
            self.assertNotIn("completed", self.context.evidence)

    def test_shell_command_success_cannot_track_a_disappeared_or_reused_pid(self):
        record, command = self.tracked_shell_fixture()
        for latest in ({}, {42: dict(record, started="different-start"), 43: proc(43, parent=42, path="/bin/mv", uid=0)}):
            with self.subTest(latest=latest), \
                    patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout=command, stderr="")), \
                    patch.object(helper, "process_table", return_value=latest):
                helper.track(self.context, {42: record})
            self.assertEqual({}, self.context.tracked)

    def test_shell_command_nonzero_for_same_live_identity_remains_failure_without_exposing_output(self):
        record, _ = self.tracked_shell_fixture()
        with patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=1, stdout=self.context.secret, stderr=self.context.secret)), \
                patch.object(helper, "process_table", return_value={42: record}):
            with self.assertRaisesRegex(helper.AuthorizationFailure, "same live process") as raised:
                helper.track(self.context, {42: record})
        self.assertNotIn(self.context.secret, str(raised.exception))
        self.assertIn("stage=track-command, pid=42", str(raised.exception))
        self.assertIn("exit 1", str(raised.exception))
        self.assertEqual({}, self.context.tracked)

    def test_shell_command_timeout_or_launch_error_is_not_reinterpreted_as_process_exit(self):
        record, _ = self.tracked_shell_fixture()
        for failure in (subprocess.TimeoutExpired(["/bin/ps"], 10, output=self.context.secret, stderr=self.context.secret),
                        OSError(self.context.secret)):
            with self.subTest(failure=type(failure).__name__), patch.object(helper.subprocess, "run", side_effect=failure), \
                    patch.object(helper, "process_table", return_value={}) as sample:
                with self.assertRaises(helper.AuthorizationFailure) as raised:
                    helper.track(self.context, {42: record})
                self.assertNotIn(self.context.secret, str(raised.exception))
                sample.assert_not_called()

    def test_shell_command_cannot_treat_failed_identity_recheck_as_disappearance(self):
        record, command = self.tracked_shell_fixture()
        for returncode in (0, 1):
            with self.subTest(returncode=returncode), \
                    patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=returncode, stdout=command, stderr="")), \
                    patch.object(helper, "process_table", side_effect=helper.AuthorizationFailure("Fixture identity recheck failed")):
                with self.assertRaisesRegex(helper.AuthorizationFailure, "identity recheck failed") as raised:
                    helper.track(self.context, {42: record})
                self.assertIn("stage=track-recheck, pid=42", str(raised.exception))
            self.assertEqual({}, self.context.tracked)

    def test_owned_shell_tracking_uses_current_descendants_after_successful_identity_recheck(self):
        record, command = self.tracked_shell_fixture()
        stale = {42: record, 43: proc(43, parent=42, path="/bin/mv", uid=0)}
        latest = {42: record, 43: proc(43, path="/bin/mv", uid=0, started="reused"),
                  44: proc(44, parent=42, path="/bin/mv", uid=0)}
        with patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout=command, stderr="")), \
                patch.object(helper, "process_table", return_value=latest):
            helper.track(self.context, stale)
        self.assertEqual({(42, "test"), (44, "test")}, set(self.context.tracked))
        self.assertEqual(latest[44], self.context.tracked[(44, "test")])

    def test_disappeared_shell_does_not_forget_previously_owned_writer_or_allow_cleanup(self):
        record, command = self.tracked_shell_fixture()
        writer = proc(43, parent=42, path="/bin/mv", uid=0)
        self.context.tracked = {(42, "test"): record, (43, "test"): writer}
        self.context.evidence["roles"] = {"unified": {"credentialsSubmitted": True, "completed": False}}
        latest = {43: dict(writer, parentPid=1)}
        with patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=1, stdout=command, stderr="")), \
                patch.object(helper, "process_table", return_value=latest):
            helper.track(self.context, {42: record, 43: writer})
            with patch.object(helper.pwd, "getpwnam") as account:
                evidence = helper.cleanup(self.context)
        self.assertEqual({(42, "test"), (43, "test")}, set(self.context.tracked))
        self.assertEqual([writer], evidence["remainingProcesses"])
        self.assertFalse(evidence["passed"] or evidence["canRemoveOwnedPaths"])
        self.assertFalse(self.context.evidence["roles"]["unified"]["completed"])
        account.assert_not_called()

    def test_ui_errors_never_echo_secret_script_or_command_output(self):
        with patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=1, stdout="", stderr=self.context.secret)):
            with self.assertRaises(helper.AuthorizationFailure) as error:
                helper.ui({"secret": self.context.secret})
        self.assertNotIn(self.context.secret, str(error.exception))

    def test_no_ui_operation_exposes_raw_stderr_even_during_preflight(self):
        diagnostic = "x" * 2000 + "preflight capability failure " + self.context.secret
        with patch.object(helper.subprocess, "run", return_value=SimpleNamespace(returncode=1, stdout="", stderr=diagnostic)):
            for payload in ({"operation": "preflight"}, {"operation": "preflight", "secret": self.context.secret},
                            {"operation": "authorize", "secret": self.context.secret},
                            {"operation": "snapshot", "pid": 99}):
                with self.subTest(payload=payload["operation"]), self.assertRaises(helper.AuthorizationFailure) as private_error:
                    helper.ui(payload)
                self.assertIn("osascript returned exit 1", str(private_error.exception))
                self.assertNotIn("preflight capability failure", str(private_error.exception))
                self.assertNotIn(self.context.secret, str(private_error.exception))

    def test_timeout_exposes_only_the_last_allowlisted_stage_marker(self):
        stderr = ("raw exception " + self.context.secret + "\nBAKABASE_AUTH_STAGE:resolve-process\n"
                  "BAKABASE_AUTH_STAGE:read-dialog-tree\nBAKABASE_AUTH_STAGE:" + self.context.secret + "\n").encode()
        failure = subprocess.TimeoutExpired(["osascript"], 5, stderr=stderr)
        with patch.object(helper.subprocess, "run", side_effect=failure):
            with self.assertRaisesRegex(helper.AuthorizationFailure, "lastUiStage=read-dialog-tree") as raised:
                helper.ui({"operation": "confirm", "secret": self.context.secret})
        self.assertNotIn(self.context.secret, str(raised.exception))
        self.assertNotIn("raw exception", str(raised.exception))

    def test_already_exited_updater_requires_no_ui_or_credentials_and_is_not_update_success(self):
        with patch.object(helper, "process_table", return_value={}), patch.object(helper, "ui") as ui:
            evidence = helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
        self.assertFalse(evidence["required"])
        self.assertTrue(evidence["updaterExited"])
        self.assertNotIn("passed", evidence)
        ui.assert_not_called()

    def test_ui_failure_exposes_only_validated_structured_diagnostic(self):
        diagnostic = {"stage": "validate-snapshot", "code": "SnapshotChanged"}
        with patch.object(helper, "run", return_value=json.dumps({"helperError": diagnostic})):
            with self.assertRaises(helper.AuthorizationUIFailure) as raised:
                helper.ui({"operation": "confirm"})
        self.assertEqual(diagnostic, raised.exception.diagnostic)
        with patch.object(helper, "run", return_value=json.dumps({"helperError": {**diagnostic, "message": self.context.secret}})):
            with self.assertRaises(helper.AuthorizationFailure) as invalid:
                helper.ui({"operation": "confirm"})
        self.assertNotIsInstance(invalid.exception, helper.AuthorizationUIFailure)
        self.assertNotIn(self.context.secret, str(invalid.exception))

    def test_confirmation_snapshot_changes_reobserve_before_one_confirmation_and_credential_submission(self):
        for changes in (1, 2):
            with self.subTest(changes=changes):
                self.context.evidence["roles"] = {}
                actions, rejected = [], [0]

                def fake_ui(payload, **_):
                    actions.append(payload["operation"])
                    if payload["operation"] == "snapshot":
                        return confirm_view()
                    if payload["operation"] == "confirm" and rejected[0] < changes:
                        rejected[0] += 1
                        raise helper.AuthorizationUIFailure({"stage": "validate-snapshot", "code": "SnapshotChanged"})
                    return {"submitted": True}

                table = {40: self.updater, 41: self.child, 99: self.system}
                with patch.object(helper, "process_table", side_effect=[{40: self.updater}] * (changes + 1) + [table, table, {}]), \
                        patch.object(helper, "executable", side_effect=lambda pid: self.updater["path"] if pid == 40 else "/usr/bin/osascript"), \
                        patch.object(helper, "security_views", side_effect=[[]] * (changes + 1) + [[system_view()]]) as views, \
                        patch.object(helper, "run", return_value=self.command()), patch.object(helper, "ui", side_effect=fake_ui), \
                        patch.object(helper.time, "sleep"):
                    evidence = helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
                self.assertEqual(["snapshot", "confirm"] * (changes + 1) + ["authorize"], actions)
                self.assertEqual(changes, evidence["confirmationSnapshotChanges"])
                self.assertEqual(changes + 2, views.call_count)
                self.assertTrue(evidence["confirmationSubmitted"] and evidence["credentialsSubmitted"] and evidence["completed"])

    def test_third_confirmation_snapshot_change_fails_without_click_or_credentials(self):
        actions = []

        def fake_ui(payload, **_):
            actions.append(payload["operation"])
            if payload["operation"] == "snapshot":
                return confirm_view()
            raise helper.AuthorizationUIFailure({"stage": "validate-snapshot", "code": "SnapshotChanged"})

        with patch.object(helper, "process_table", return_value={40: self.updater}), \
                patch.object(helper, "executable", return_value=self.updater["path"]), \
                patch.object(helper, "security_views", return_value=[]), patch.object(helper, "ui", side_effect=fake_ui):
            with self.assertRaises(helper.AuthorizationUIFailure):
                helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
        evidence = self.context.evidence["roles"]["unified"]
        self.assertEqual(["snapshot", "confirm"] * 3, actions)
        self.assertEqual(3, evidence["confirmationSnapshotChanges"])
        self.assertFalse(evidence["confirmationSubmitted"] or evidence["credentialsSubmitted"] or evidence["completed"])

    def test_other_confirm_errors_and_unstructured_error_text_never_reobserve(self):
        errors = [helper.AuthorizationUIFailure({"stage": stage, "code": code}) for stage, code in (
            ("press-button", "SystemCallFailed"), ("press-button", "SnapshotChanged"),
            ("revalidate-focused-reference", "FocusChangedBeforePress"), ("validate-snapshot", "SystemCallFailed"))]
        errors += [helper.AuthorizationFailure('Authorization UI failed: {"stage":"validate-snapshot","code":"SnapshotChanged"}'),
                   helper.AuthorizationFailure("Authorization helper command timed out; lastUiStage=validate-snapshot")]
        for error in errors:
            with self.subTest(error=str(error)):
                self.context.evidence["roles"] = {}
                with patch.object(helper, "process_table", return_value={40: self.updater}), \
                        patch.object(helper, "executable", return_value=self.updater["path"]), \
                        patch.object(helper, "security_views", return_value=[]), \
                        patch.object(helper, "ui", side_effect=[confirm_view(), error]) as ui:
                    with self.assertRaises(helper.AuthorizationFailure) as raised:
                        helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
                self.assertIs(error, raised.exception)
                self.assertEqual(2, ui.call_count)
                self.assertEqual(0, self.context.evidence["roles"]["unified"]["confirmationSnapshotChanges"])

    def test_credential_snapshot_change_is_not_retried(self):
        actions = []

        def fake_ui(payload, **_):
            actions.append(payload["operation"])
            if payload["operation"] == "snapshot":
                return confirm_view()
            if payload["operation"] == "authorize":
                raise helper.AuthorizationUIFailure({"stage": "validate-snapshot", "code": "SnapshotChanged"})
            return {"submitted": True}

        table = {40: self.updater, 41: self.child, 99: self.system}
        with patch.object(helper, "process_table", side_effect=[{40: self.updater}, table, table]), \
                patch.object(helper, "executable", side_effect=lambda pid: self.updater["path"] if pid == 40 else "/usr/bin/osascript"), \
                patch.object(helper, "security_views", side_effect=[[], [system_view()]]), \
                patch.object(helper, "run", return_value=self.command()), patch.object(helper, "ui", side_effect=fake_ui), \
                patch.object(helper.time, "sleep"):
            with self.assertRaises(helper.AuthorizationUIFailure):
                helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
        evidence = self.context.evidence["roles"]["unified"]
        self.assertEqual(["snapshot", "confirm", "authorize"], actions)
        self.assertTrue(evidence["credentialsSubmitted"])
        self.assertEqual(0, evidence["confirmationSnapshotChanges"])

    def test_confirmation_reobservation_preserves_original_deadline(self):
        clock = [0]

        def fake_ui(payload, **_):
            if payload["operation"] == "snapshot":
                return confirm_view()
            clock[0] = 11
            raise helper.AuthorizationUIFailure({"stage": "validate-snapshot", "code": "SnapshotChanged"})

        with patch.object(helper.time, "monotonic", side_effect=lambda: clock[0]), \
                patch.object(helper, "process_table", return_value={40: self.updater}) as table, \
                patch.object(helper, "executable", return_value=self.updater["path"]), \
                patch.object(helper, "security_views", return_value=[]), patch.object(helper, "ui", side_effect=fake_ui) as ui:
            with self.assertRaisesRegex(helper.AuthorizationFailure, "existing startup deadline"):
                helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=10)
        self.assertEqual(1, table.call_count)
        self.assertEqual(2, ui.call_count)
        self.assertEqual(1, self.context.evidence["roles"]["unified"]["confirmationSnapshotChanges"])

    def test_confirmation_reobservation_rechecks_process_identity_and_attribution(self):
        for change in ("pid-reused", "another-script", "system-window"):
            with self.subTest(change=change):
                self.context.evidence["roles"] = {}
                second_table = {40: dict(self.updater, started="changed") if change == "pid-reused" else self.updater}
                if change == "another-script":
                    second_table[41] = self.child
                with patch.object(helper, "process_table", side_effect=[{40: self.updater}, second_table]), \
                        patch.object(helper, "executable", return_value=self.updater["path"]), \
                        patch.object(helper, "security_views", side_effect=[[], [system_view()]]) as views, \
                        patch.object(helper, "ui", side_effect=[confirm_view(), helper.AuthorizationUIFailure(
                            {"stage": "validate-snapshot", "code": "SnapshotChanged"})]) as ui:
                    with self.assertRaises(helper.AuthorizationFailure):
                        helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
                self.assertEqual(2, ui.call_count)
                self.assertEqual(2 if change == "system-window" else 1, views.call_count)
                evidence = self.context.evidence["roles"]["unified"]
                self.assertEqual(1, evidence["confirmationSnapshotChanges"])
                self.assertFalse(evidence["confirmationSubmitted"] or evidence["credentialsSubmitted"])

    def test_exact_chain_confirms_and_authorizes_once_before_exit(self):
        actions = []
        def fake_ui(payload, **_):
            actions.append(payload)
            return confirm_view() if payload["operation"] == "snapshot" else {"submitted": True}
        with patch.object(helper, "process_table", side_effect=[{40: self.updater}, {40: self.updater, 41: self.child, 99: self.system},
                {40: self.updater, 41: self.child, 99: self.system}, {}]), \
                patch.object(helper, "executable", side_effect=lambda pid: self.updater["path"] if pid == 40 else "/usr/bin/osascript"), \
                patch.object(helper, "security_views", side_effect=[[], [system_view()]]), \
                patch.object(helper, "run", return_value=self.command()), patch.object(helper, "ui", side_effect=fake_ui), \
                patch.object(helper.time, "sleep"):
            evidence = helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
        self.assertTrue(evidence["required"] and evidence["credentialsSubmitted"] and evidence["completed"])
        self.auth_diagnostics.assert_not_called()
        self.assertEqual(["snapshot", "confirm", "authorize"], [p["operation"] for p in actions])
        with self.assertRaisesRegex(helper.AuthorizationFailure, "Repeated"):
            helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)

    def test_changed_process_chain_prevents_any_credential_submission(self):
        actions = []
        def fake_ui(payload, **_):
            actions.append(payload["operation"])
            return confirm_view() if payload["operation"] == "snapshot" else {"submitted": True}
        with patch.object(helper, "process_table", side_effect=[{40: self.updater}, {40: self.updater, 41: self.child},
                {40: self.updater, 41: proc(41, parent=88)}]), \
                patch.object(helper, "executable", side_effect=lambda pid: self.updater["path"] if pid == 40 else "/usr/bin/osascript"), \
                patch.object(helper, "security_views", side_effect=[[], [system_view()]]), \
                patch.object(helper, "run", return_value=self.command()), patch.object(helper, "ui", side_effect=fake_ui), \
                patch.object(helper.time, "sleep"):
            with self.assertRaisesRegex(helper.AuthorizationFailure, "chain changed"):
                helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
        self.assertNotIn("authorize", actions)

    def test_submitted_but_stalled_request_is_observed_once_without_retry_or_success(self):
        actions, clock = [], [0]
        table = {40: self.updater, 41: self.child, 99: self.system}
        def fake_ui(payload, **_):
            actions.append(payload)
            if payload["operation"] != "snapshot":
                return {"submitted": True}
            if payload["pid"] == 40:
                return confirm_view()
            view = system_view()
            view["windows"][0]["nodes"].append(node("AXStaticText", text="The supplied password was not accepted."))
            for item in view["windows"][0]["nodes"]:
                if item["role"] == "AXTextField":
                    item.update(value=self.context.secret, text=self.context.secret)
            return view
        def advance(_):
            clock[0] += 4
        with patch.object(helper.time, "monotonic", side_effect=lambda: clock[0]), \
                patch.object(helper.time, "sleep", side_effect=advance), \
                patch.object(helper, "process_table", side_effect=[{40: self.updater}, table, table, table]), \
                patch.object(helper, "executable", side_effect=lambda pid: {40: self.updater, 41: self.child, 99: self.system}[pid]["path"]), \
                patch.object(helper, "security_views", side_effect=[[], [system_view()]]), \
                patch.object(helper, "run", return_value=self.command()), patch.object(helper, "ui", side_effect=fake_ui):
            with self.assertRaisesRegex(helper.AuthorizationFailure, "exceeded"):
                helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=12)
        self.assertEqual([("snapshot", 40), ("confirm", 40), ("authorize", 99), ("snapshot", 99)],
                         [(p["operation"], p["pid"]) for p in actions])
        evidence = self.context.evidence["roles"]["unified"]
        self.auth_diagnostics.assert_called_once_with(self.context, evidence)
        self.assertFalse(evidence["completed"])
        self.assertTrue(evidence["postSubmissionObservation"]["readOnly"])
        self.assertIn("not accepted", json.dumps(evidence["postSubmissionObservation"]))
        self.assertNotIn(self.context.secret, json.dumps(evidence))

    def test_post_submission_diagnostic_refuses_reused_pid_or_exhausted_budget_and_hides_exceptions(self):
        for situation in ("exited", "reused", "budget", "error"):
            evidence = {"systemAuthorizationProcess": self.system}
            table = {} if situation == "exited" else {99: dict(self.system, started="changed" if situation == "reused" else self.system["started"])}
            with self.subTest(situation=situation), patch.object(helper.time, "monotonic", return_value=10), \
                    patch.object(helper, "executable", return_value=self.system["path"]) as executable, \
                    patch.object(helper, "ui", side_effect=RuntimeError(self.context.secret)) as ui:
                helper.observe_submitted_dialog(evidence, table, 10 if situation == "budget" else 12)
                if situation != "error":
                    ui.assert_not_called()
                    executable.assert_not_called()
                else:
                    self.assertEqual(2, ui.call_args.kwargs["timeout"])
                    self.assertEqual("RuntimeError", evidence["postSubmissionObservation"]["error"])
            self.assertNotIn(self.context.secret, json.dumps(evidence))

    def test_timeout_is_failure_and_not_an_authorization_pass(self):
        with patch.object(helper.time, "monotonic", side_effect=[0, 11]), self.assertRaisesRegex(helper.AuthorizationFailure, "exceeded"):
            helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=10)
        self.assertFalse(self.context.evidence["roles"]["unified"]["completed"])

    def test_cleanup_leaves_unowned_or_reused_accounts_untouched(self):
        foreign = SimpleNamespace(pw_uid=self.context.uid + 1, pw_dir=str(self.context.home))
        with patch.object(helper, "process_table", return_value={}), patch.object(helper.pwd, "getpwnam", return_value=foreign), \
                patch.object(helper, "run") as run:
            evidence = helper.cleanup(self.context)
        self.assertFalse(evidence["canRemoveOwnedPaths"])
        run.assert_not_called()

    def test_verified_removal_rechecks_absence_without_deleting_again_even_after_later_failure(self):
        owned = SimpleNamespace(pw_name=self.context.username, pw_uid=self.context.uid, pw_dir=str(self.context.home))
        with patch.object(helper, "process_table", return_value={}), patch.object(helper.pwd, "getpwnam", return_value=owned) as lookup, \
                patch.object(helper.pwd, "getpwall", return_value=[]) as enumerate_accounts, patch.object(helper, "run") as run:
            self.assertTrue(helper.cleanup(self.context)["passed"])
            self.assertTrue(self.context.account_removal_verified)
            lookup.reset_mock()
            run.reset_mock()
            repeated = helper.cleanup(self.context)
            self.assertTrue(repeated["passed"] and repeated["accountRemovalPreviouslyVerified"])
            lookup.assert_not_called()
            run.assert_not_called()
            # Even a later failure cannot erase the previous removal proof and
            # authorize deletion of a newly created same-name/same-UID account.
            enumerate_accounts.return_value = [owned]
            for _ in range(2):
                recreated = helper.cleanup(self.context)
                self.assertFalse(recreated["passed"] or recreated["canRemoveOwnedPaths"])
                self.assertTrue(recreated["accountRemovalPreviouslyVerified"])
            lookup.assert_not_called()
            run.assert_not_called()

    def test_live_authorization_child_blocks_user_and_bundle_cleanup(self):
        self.context.tracked[(41, self.child["started"])] = self.child
        with patch.object(helper, "process_table", return_value={41: self.child}), patch.object(helper, "run") as run, \
                patch.object(helper.pwd, "getpwnam") as user:
            evidence = helper.cleanup(self.context)
        self.assertEqual([self.child], evidence["remainingProcesses"])
        self.assertFalse(evidence["canRemoveOwnedPaths"])
        run.assert_not_called()
        user.assert_not_called()

    def test_cleanup_deletes_only_owned_account_and_verifies_home_and_uid_absent(self):
        owned = SimpleNamespace(pw_uid=self.context.uid, pw_dir=str(self.context.home))
        with patch.object(helper, "process_table", return_value={}), patch.object(helper.pwd, "getpwnam", return_value=owned), \
                patch.object(helper.pwd, "getpwall", return_value=[]), patch.object(helper.Path, "exists", return_value=False), \
                patch.object(helper.Path, "is_symlink", return_value=False), patch.object(helper, "run", return_value="") as run:
            evidence = helper.cleanup(self.context)
        self.assertTrue(evidence["canRemoveOwnedPaths"])
        self.assertIsNone(self.context.secret)
        self.assertEqual(["/usr/bin/sudo", "-n", "/usr/sbin/sysadminctl", "-deleteUser", self.context.username], run.call_args.args[0])

    def waiting_cleanup_state(self):
        state = {"updaterPid": 40, "updaterProcess": self.updater, "originalOsascript": self.child,
                 "required": True, "confirmationSubmitted": True, "credentialsSubmitted": False, "completed": False}
        self.context.evidence["roles"] = {"unified": state}
        self.context.tracked = {(41, self.child["started"]): self.child}
        cache = Path.home() / "Library/Caches/velopack/Bakabase/packages"
        self.context.shells = {helper.validate_elevation_script(self.command(), self.app["installRoot"], cache)}
        return state, {41: dict(self.child, parentPid=1)}

    def test_failed_authorize_can_cancel_only_its_verified_unsubmitted_request_then_remove_owned_account(self):
        # Exercise the real authorize -> failure -> cleanup flow. No UI, signals,
        # accounts or native processes are used; the original child proof is
        # produced by authorize itself, not injected into its evidence.
        actions = []
        def fake_ui(payload, **_):
            actions.append(payload["operation"])
            return confirm_view() if payload["operation"] == "snapshot" else {"submitted": True}
        with patch.object(helper, "process_table", side_effect=[{40: self.updater}, {40: self.updater, 41: self.child}]), \
                patch.object(helper, "executable", side_effect=lambda pid: self.updater["path"] if pid == 40 else "/usr/bin/osascript"), \
                patch.object(helper, "security_views", side_effect=[[], [system_view()]]), \
                patch.object(helper, "run", return_value=self.command()), patch.object(helper, "ui", side_effect=fake_ui), \
                patch.object(helper, "authorization_controls", side_effect=helper.AuthorizationFailure("Fixture rejects authorization dialog")), \
                patch.object(helper.time, "sleep"):
            with self.assertRaisesRegex(helper.AuthorizationFailure, "Fixture rejects"):
                helper.authorize(self.context, self.app, 40, target_version=self.version, deadline=helper.time.monotonic() + 10)
        state = self.context.evidence["roles"]["unified"]
        self.assertEqual(self.child, state["originalOsascript"])
        self.assertEqual(["snapshot", "confirm"], actions)
        owned = SimpleNamespace(pw_uid=self.context.uid, pw_dir=str(self.context.home))
        waiting = {41: dict(self.child, parentPid=1)}
        events = []
        def fake_run(arguments, **_):
            if arguments[0] == "/bin/ps":
                return self.command()
            self.assertEqual(["/usr/bin/sudo", "-n", "/usr/sbin/sysadminctl", "-deleteUser", self.context.username], arguments)
            events.append("delete-account")
            return ""
        with patch.object(helper, "process_table", side_effect=[waiting, waiting, {}]), \
                patch.object(helper, "executable", return_value="/usr/bin/osascript"), \
                patch.object(helper.os, "kill", side_effect=lambda *_: events.append("cancel-request")) as kill, \
                patch.object(helper, "run", side_effect=fake_run), patch.object(helper.pwd, "getpwnam", return_value=owned), \
                patch.object(helper.pwd, "getpwall", return_value=[]), patch.object(helper.Path, "exists", return_value=False), \
                patch.object(helper.Path, "is_symlink", return_value=False):
            evidence = helper.cleanup(self.context)
        kill.assert_called_once_with(41, helper.signal.SIGTERM)
        self.assertEqual(["cancel-request", "delete-account"], events)
        self.assertTrue(evidence["passed"] and evidence["canRemoveOwnedPaths"])
        self.assertEqual([], evidence["remainingProcesses"])
        self.assertTrue(evidence["cancelledUnsubmittedRequests"][0]["exited"])
        self.assertIn("failed-update cleanup only", evidence["cancelledUnsubmittedRequests"][0]["scope"])
        self.assertFalse(state["credentialsSubmitted"] or state["completed"])
        self.assertIsNone(self.context.secret)

    def test_cancellation_rejects_reused_pid_unknown_script_credentials_and_privileged_writers_without_signalling(self):
        for mutation in ("reused-pid", "unknown-script", "unrecorded-script", "changed-executable", "submitted",
                         "other-role-submitted", "updater-present", "unverified-parent", "privileged-descendant", "past-privileged-writer"):
            with self.subTest(mutation=mutation):
                state, table = self.waiting_cleanup_state()
                command, executable = self.command(), "/usr/bin/osascript"
                if mutation == "reused-pid": table[41]["started"] = "different-start"
                elif mutation == "unknown-script": command = command.replace("mv -f", "unexpected -f", 1)
                elif mutation == "unrecorded-script": self.context.shells.clear()
                elif mutation == "changed-executable": executable = "/unowned/osascript"
                elif mutation == "submitted": state["credentialsSubmitted"] = True
                elif mutation == "other-role-submitted": self.context.evidence["roles"]["client"] = {"credentialsSubmitted": True}
                elif mutation == "updater-present": table[40] = self.updater
                elif mutation == "unverified-parent": state["updaterProcess"] = proc(88)
                elif mutation == "privileged-descendant": table[42] = proc(42, parent=41, path="/bin/mv", uid=0)
                elif mutation == "past-privileged-writer": self.context.tracked[(42, "test")] = proc(42, path="/bin/mv", uid=0)
                with patch.object(helper, "process_table", return_value=table), patch.object(helper, "run", return_value=command) as run, \
                        patch.object(helper, "executable", return_value=executable), patch.object(helper.os, "kill") as kill, \
                        patch.object(helper.pwd, "getpwnam") as account:
                    evidence = helper.cleanup(self.context)
                kill.assert_not_called()
                account.assert_not_called()
                self.assertTrue(all(call.args[0][0] == "/bin/ps" for call in run.call_args_list))
                self.assertFalse(evidence["passed"] or evidence["canRemoveOwnedPaths"])
                self.assertIn("error", evidence)

    def test_cancellation_rechecks_pid_and_script_immediately_before_signalling(self):
        for mutation in ("pid-reused", "script-changed", "writer-started"):
            with self.subTest(mutation=mutation):
                _, first = self.waiting_cleanup_state()
                latest = copy.deepcopy(first)
                command = self.command()
                if mutation == "pid-reused": latest[41]["started"] = "new-start"
                elif mutation == "writer-started": latest[42] = proc(42, parent=41, path="/bin/mv", uid=0)
                commands = [command, command + "; unexpected"] if mutation == "script-changed" else [command]
                with patch.object(helper, "process_table", side_effect=[first, latest]), \
                        patch.object(helper, "run", side_effect=commands), patch.object(helper, "executable", return_value="/usr/bin/osascript"), \
                        patch.object(helper.os, "kill") as kill, patch.object(helper.pwd, "getpwnam") as account:
                    evidence = helper.cleanup(self.context)
                kill.assert_not_called()
                account.assert_not_called()
                self.assertFalse(evidence["passed"] or evidence["canRemoveOwnedPaths"])

    def test_cancellation_timeout_never_escalates_or_removes_account(self):
        _, table = self.waiting_cleanup_state()
        with patch.object(helper, "process_table", return_value=table), patch.object(helper, "run", return_value=self.command()), \
                patch.object(helper, "executable", return_value="/usr/bin/osascript"), patch.object(helper.os, "kill") as kill, \
                patch.object(helper.time, "monotonic", side_effect=[0, 0, 6]), patch.object(helper.time, "sleep"), \
                patch.object(helper.pwd, "getpwnam") as account:
            evidence = helper.cleanup(self.context)
        kill.assert_called_once_with(41, helper.signal.SIGTERM)
        account.assert_not_called()
        self.assertFalse(evidence["passed"] or evidence["canRemoveOwnedPaths"])
        self.assertFalse(evidence["cancelledUnsubmittedRequests"][0]["exited"])
        self.assertEqual([table[41]], evidence["remainingProcesses"])
        self.assertIn("bounded cancellation", evidence["error"])

    def test_cancellation_signal_or_exit_verification_failure_remains_a_cleanup_failure(self):
        for failure in ("signal", "exit-verification"):
            with self.subTest(failure=failure):
                _, table = self.waiting_cleanup_state()
                tables = [table, table] if failure == "signal" else [table, table, helper.AuthorizationFailure("Fixture exit verification failed")]
                with patch.object(helper, "process_table", side_effect=tables), patch.object(helper, "run", return_value=self.command()), \
                        patch.object(helper, "executable", return_value="/usr/bin/osascript"), \
                        patch.object(helper.os, "kill", side_effect=PermissionError("private detail") if failure == "signal" else None) as kill, \
                        patch.object(helper.pwd, "getpwnam") as account:
                    evidence = helper.cleanup(self.context)
                kill.assert_called_once_with(41, helper.signal.SIGTERM)
                account.assert_not_called()
                self.assertFalse(evidence["passed"] or evidence["canRemoveOwnedPaths"])
                self.assertFalse(evidence["cancelledUnsubmittedRequests"][0]["exited"])
                self.assertNotIn("private detail", json.dumps(evidence))


if __name__ == "__main__":
    unittest.main()
