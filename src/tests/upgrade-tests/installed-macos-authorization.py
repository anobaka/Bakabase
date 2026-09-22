#!/usr/bin/env python3
"""Simulated user authorization of the ORIGINAL updater on disposable macOS CI.

No bundle ownership, TCC, authorization policy or updater changes. Credentials
exist only in memory and pipes/PTY input. SecurityAgent does not expose its
requester PID: attribution therefore requires an exclusive observed osascript
chain, an initially empty authorization UI, and an unambiguous new system UI.
This proves an INTERACTIVE update, never an unattended/permission-free update.
"""
import contextlib
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import secrets
import select
import signal
import stat
import subprocess
import sys
import time
from types import SimpleNamespace

try:
    import pwd
except ImportError:  # The pure guard suite also imports this module on Windows.
    pwd = SimpleNamespace(getpwall=None, getpwnam=None)

SPEC = importlib.util.spec_from_file_location("mac_authorization_base", Path(__file__).with_name("run-package-acceptance.py"))
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)

SECURITY_AGENTS = {
    "/System/Library/CoreServices/SecurityAgent.app/Contents/MacOS/SecurityAgent",
    "/System/Library/Frameworks/Security.framework/Versions/A/MachServices/SecurityAgent.bundle/Contents/MacOS/SecurityAgent",
}
ATTRIBUTION = "exclusive-observed-osascript-chain; SecurityAgent AX does not expose requester PID"
UI_STAGES = {"initialize", "preflight", "resolve-process", "enumerate-windows", "read-focused-window", "read-dialog-tree",
             "validate-snapshot", "revalidate-focused-reference", "resolve-control", "verify-credential-controls",
             "set-username", "verify-username", "set-password", "press-button"}


class AuthorizationFailure(AssertionError):
    pass


class AuthorizationUIFailure(AuthorizationFailure):
    def __init__(self, diagnostic):
        self.diagnostic = safe_ui_diagnostic(diagnostic)
        super().__init__("Authorization UI failed: " + json.dumps(self.diagnostic, separators=(",", ":")))


class PreparationFailure(AuthorizationFailure):
    def __init__(self, context):
        super().__init__("Native authorization preparation failed; fixture cleanup context is retained")
        self.context = context


def require(value, message):
    if not value:
        raise AuthorizationFailure(message)


def run(arguments, *, input_text=None, timeout=10):
    """Never expose command output/input; JXA supplies its own safe error schema."""
    try:
        result = subprocess.run([str(a) for a in arguments], input=input_text, text=True,
                                capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired as error:
        last_stage = None
        if Path(arguments[0]).name == "osascript":
            stderr = error.stderr or b""
            if isinstance(stderr, bytes):
                stderr = stderr.decode("utf-8", errors="replace")
            markers = re.findall(r"^BAKABASE_AUTH_STAGE:([a-z-]+)\r?$", stderr[-16000:], flags=re.MULTILINE)
            last_stage = next((marker for marker in reversed(markers) if marker in UI_STAGES), None)
        suffix = "; lastUiStage=" + last_stage if last_stage else ""
        raise AuthorizationFailure("Authorization helper command timed out" + suffix) from None
    except OSError:
        raise AuthorizationFailure("Authorization helper command failed or timed out") from None
    failure = f"Authorization helper command {Path(arguments[0]).name} returned exit {result.returncode}"
    require(result.returncode == 0, failure)
    return result.stdout


def process_table():
    output = run(["/bin/ps", "-axo", "pid=,ppid=,uid=,lstart=,comm="])
    result = {}
    for line in output.splitlines():
        match = re.fullmatch(r"\s*(\d+)\s+(\d+)\s+(\d+)\s+(\w{3}\s+\w{3}\s+\d+\s+\d\d:\d\d:\d\d\s+\d{4})\s+(.+)", line)
        if match:
            pid, parent, uid, started, path = match.groups()
            result[int(pid)] = {"pid": int(pid), "parentPid": int(parent), "uid": int(uid),
                                "started": started, "path": path}
    return result


def executable(pid):
    library = ctypes.CDLL("/usr/lib/libproc.dylib")
    buffer = ctypes.create_string_buffer(4096)
    require(library.proc_pidpath(pid, buffer, len(buffer)) > 0, "Cannot identify authorization process executable")
    return buffer.value.decode()


def same_process(first, second):
    return bool(first and second and (first["pid"], first["started"]) == (second["pid"], second["started"]))


def permission_snapshot(root):
    """Observe existing system-install permissions without changing them."""
    runner = {name: getattr(os, name)() if hasattr(os, name) else None
              for name in ("getuid", "geteuid", "getgid", "getegid", "getgroups")}
    result = {"observedEpoch": time.time(), "runner": runner, "paths": []}
    for path in (root, root.parent):
        record = {"path": str(path)}
        try:
            info = path.lstat()
            record.update(uid=info.st_uid, gid=info.st_gid, mode=oct(stat.S_IMODE(info.st_mode)),
                          isSymlink=stat.S_ISLNK(info.st_mode),
                          realUidCanWrite=os.access(path, os.W_OK), realUidCanSearch=os.access(path, os.X_OK))
            if os.access in os.supports_effective_ids:
                record.update(effectiveUidCanWrite=os.access(path, os.W_OK, effective_ids=True),
                              effectiveUidCanSearch=os.access(path, os.X_OK, effective_ids=True))
            record["acl"] = run(["/bin/ls", "-lde", str(path)], timeout=3)[:4096]
        except (OSError, AuthorizationFailure) as error:
            record["observationError"] = type(error).__name__
        result["paths"].append(record)
    return result


def descendants(table, pid):
    found = {pid}
    while True:
        more = {p["pid"] for p in table.values() if p["parentPid"] in found}
        if more <= found:
            return found
        found |= more


# All UI reads and actions target an exact PID. Field values are NEVER included
# in snapshots; in particular secure text is never read back into diagnostics.
JXA = r'''
let se, stage = 'initialize';
function setStage(value) { stage = value; console.log('BAKABASE_AUTH_STAGE:' + value); }
function fail(code, difference) {
  const error = new Error(); error.helperCode = code; error.helperDifference = difference; throw error;
}
function snapshotDifference(expected, actual) {
  const fields = [], keys = ['path','role','subrole','title','description','enabled','text'];
  const add = path => { if (fields.length < 12) fields.push(path); };
  const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  for (const key of ['pid','scope','originalWindowCount','focusedWindowAvailable'])
    if (!equal(expected && expected[key], actual && actual[key])) add(key);
  const before = expected && Array.isArray(expected.windows) ? expected.windows : [];
  const after = actual && Array.isArray(actual.windows) ? actual.windows : [];
  if (before.length !== after.length) add('windows.length');
  for (let i = 0; i < Math.min(before.length, after.length, 4); i++) {
    if (!equal(before[i].index, after[i].index)) add(`windows[${i}].index`);
    const a = before[i].nodes || [], b = after[i].nodes || [];
    if (a.length !== b.length) add(`windows[${i}].nodes.length`);
    for (let n = 0; n < Math.min(a.length, b.length, 500) && fields.length < 12; n++)
      for (const key of keys) if (!equal(a[n][key], b[n][key])) add(`windows[${i}].nodes[${n}].${key}`);
  }
  return {fields, expectedWindowCount:before.length, actualWindowCount:after.length,
    expectedNodeCounts:before.slice(0,4).map(w => (w.nodes || []).length),
    actualNodeCounts:after.slice(0,4).map(w => (w.nodes || []).length)};
}
function attr(e, key) { try { return e.attributes.byName(key).value(); } catch (_) { return ''; } }
function inspect(pid) {
  setStage('resolve-process');
  const focusedOnly = data.scope === 'updater-focused-window';
  const ps = se.processes.whose({unixId: pid})();
  if (ps.length > 1) fail('AmbiguousProcess');
  if (ps.length === 0) return focusedOnly
    ? {pid: pid, scope: data.scope, originalWindowCount: 0, focusedWindowAvailable: false, windows: []}
    : {pid: pid, windows: []};
  setStage('enumerate-windows');
  const process = ps[0], refs = [], windows = process.windows();
  const result = {pid: pid, windows: []};
  let focused;
  if (focusedOnly) {
    if (process.unixId() !== pid) fail('ProcessChanged');
    result.scope = data.scope;
    result.originalWindowCount = windows.length;
    result.focusedWindowAvailable = false;
    // Read the OS-selected window; never choose an array item or deduplicate
    // equal titles/trees. System Events does not expose a native window ID.
    setStage('read-focused-window');
    try { focused = process.attributes.byName('AXFocusedWindow').value(); } catch (_) {}
    if (!focused || String(attr(focused, 'AXRole')) !== 'AXWindow' || attr(focused, 'AXFocused') !== true)
      return {snapshot: result, refs: refs};
    result.focusedWindowAvailable = true;
  }
  function walk(e, path, nodes, depth) {
    if (depth > 15 || refs.length > 500) fail('UiBudgetExceeded');
    const role = String(attr(e, 'AXRole')), subrole = String(attr(e, 'AXSubrole'));
    const node = {path: path, role: role, subrole: subrole,
      title: String(attr(e, 'AXTitle')), description: String(attr(e, 'AXDescription')),
      enabled: attr(e, 'AXEnabled') === true};
    if (role === 'AXStaticText') node.text = String(attr(e, 'AXValue'));
    nodes.push(node); refs.push([path.join('.'), e]);
    let children; try { children = e.uiElements(); } catch (_) { children = []; }
    children.forEach((child, i) => walk(child, path.concat([i]), nodes, depth + 1));
  }
  setStage('read-dialog-tree');
  if (focusedOnly) {
    const nodes = []; walk(focused, ['focused'], nodes, 0);
    if (attr(focused, 'AXFocused') !== true) fail('FocusChangedDuringRead');
    result.windows.push({index: 'focused', nodes: nodes});
  } else {
    windows.forEach((window, i) => { const nodes = []; walk(window, [i], nodes, 0);
      result.windows.push({index: i, nodes: nodes}); });
  }
  return {snapshot: result, refs: refs, focusedElement: focused};
}
function main() {
  setStage('initialize');
  se = Application('System Events');
  if (data.operation === 'preflight') { setStage('preflight'); return {enabled: se.uiElementsEnabled()}; }
  if (data.scope === 'updater-focused-window' && !['snapshot', 'confirm'].includes(data.operation))
    fail('InvalidFocusedOperation');
  let view = inspect(data.pid);
  const snapshot = view.snapshot || view;
  if (data.operation === 'snapshot') return snapshot;
  setStage('validate-snapshot');
  if (JSON.stringify(snapshot) !== JSON.stringify(data.expected)) fail('SnapshotChanged', snapshotDifference(data.expected, snapshot));
  if (data.scope === 'updater-focused-window') {
    // inspect above already re-read the whole focused dialog and compared it
    // with the caller's snapshot. Check that very reference is still focused
    // before using its controls; a second full AX traversal can exceed the
    // original five-second command deadline on Intel hosted runners.
    setStage('revalidate-focused-reference');
    if (!view.focusedElement || attr(view.focusedElement, 'AXFocused') !== true)
      fail('FocusChangedBeforePress');
  }
  function element(path) {
    setStage('resolve-control');
    const found = view.refs.filter(p => p[0] === path.join('.'));
    if (found.length !== 1) fail('AmbiguousControl');
    return found[0][1];
  }
  if (data.operation === 'authorize') {
    const username = element(data.usernamePath), password = element(data.passwordPath);
    setStage('verify-credential-controls');
    if (username.attributes.byName('AXValue').settable() !== true || password.attributes.byName('AXValue').settable() !== true)
      fail('CredentialFieldNotWritable');
    setStage('set-username'); username.value = data.username;
    setStage('verify-username');
    // Read only the ordinary field and compare in memory. Never serialize its
    // value, read back the secure field, or retry a failed credential write.
    if (String(username.attributes.byName('AXValue').value()) !== data.username) fail('UsernameValueNotApplied');
    setStage('set-password'); password.value = data.secret;
  } else if (data.operation !== 'confirm') fail('UnsupportedOperation');
  const button = element(data.buttonPath); setStage('press-button'); button.click();
  return {submitted: true};
}
let output;
try { output = main(); } catch (error) {
  const diagnostic = {stage, code:error.helperCode || 'SystemCallFailed'};
  // Never use message, stack, userInfo, input, or any other exception text.
  for (const key of ['errorNumber', 'number', 'code']) {
    const number = error[key];
    if (Number.isInteger(number) && number >= -2147483648 && number <= 2147483647) {
      diagnostic.systemErrorNumber = number; break;
    }
  }
  if (diagnostic.systemErrorNumber === undefined && typeof error.message === 'string') {
    const suffix = error.message.slice(-80).match(/\(([+-]?\d{1,10})\)\s*$/);
    if (suffix) {
      const number = Number(suffix[1]);
      if (Number.isInteger(number) && number >= -2147483648 && number <= 2147483647)
        diagnostic.systemErrorNumber = number;
    }
  }
  if (error.helperDifference) diagnostic.snapshotDifference = error.helperDifference;
  output = {helperError:diagnostic};
}
JSON.stringify(output);
'''


def safe_ui_diagnostic(value):
    codes = {"SystemCallFailed", "AmbiguousProcess", "ProcessChanged", "UiBudgetExceeded", "FocusChangedDuringRead",
             "InvalidFocusedOperation", "SnapshotChanged", "FocusChangedBeforePress", "AmbiguousControl", "UnsupportedOperation",
             "CredentialFieldNotWritable", "UsernameValueNotApplied"}
    invalid = "Authorization UI returned invalid diagnostic evidence"
    require(isinstance(value, dict) and set(value) <= {"stage", "code", "systemErrorNumber", "snapshotDifference"}, invalid)
    require(isinstance(value.get("stage"), str) and value["stage"] in UI_STAGES and
            isinstance(value.get("code"), str) and value["code"] in codes, invalid)
    if "systemErrorNumber" in value:
        require(type(value["systemErrorNumber"]) is int and -2147483648 <= value["systemErrorNumber"] <= 2147483647, invalid)
    if "snapshotDifference" in value:
        difference = value["snapshotDifference"]
        require(isinstance(difference, dict) and set(difference) == {
            "fields", "expectedWindowCount", "actualWindowCount", "expectedNodeCounts", "actualNodeCounts"}, invalid)
        fields = difference["fields"]
        field_pattern = r"(?:pid|scope|originalWindowCount|focusedWindowAvailable|windows\.length|windows\[[0-3]\]\.(?:index|nodes\.length|nodes\[[0-9]{1,3}\]\.(?:path|role|subrole|title|description|enabled|text)))"
        require(isinstance(fields, list) and len(fields) <= 12 and all(
            isinstance(path, str) and re.fullmatch(field_pattern, path) for path in fields), invalid)
        for key in ("expectedWindowCount", "actualWindowCount"):
            require(type(difference[key]) is int and 0 <= difference[key] <= 1000, invalid)
        for key in ("expectedNodeCounts", "actualNodeCounts"):
            require(isinstance(difference[key], list) and len(difference[key]) <= 4 and all(
                type(count) is int and 0 <= count <= 1000 for count in difference[key]), invalid)
    return value


def ui(payload, timeout=5):
    # The script itself is stdin, not -e or an on-disk script, so neither process
    # arguments nor an artifact can expose the temporary administrator password.
    script = "const data = " + json.dumps(payload) + ";\n" + JXA
    result = run(["/usr/bin/osascript", "-l", "JavaScript", "-"], input_text=script, timeout=timeout)
    try:
        parsed = json.loads(result)
    except ValueError:
        raise AuthorizationFailure("Authorization UI returned invalid evidence") from None
    if isinstance(parsed, dict) and "helperError" in parsed:
        raise AuthorizationUIFailure(parsed["helperError"])
    return parsed


def security_views(table):
    result = []
    for record in table.values():
        if Path(record["path"]).name == "SecurityAgent":
            require(executable(record["pid"]) in SECURITY_AGENTS, "Unexpected SecurityAgent executable")
            view = ui({"operation": "snapshot", "pid": record["pid"]})
            if view["windows"]:
                result.append(view)
    return result


def require_no_competing_script(table, allowed_pid=None):
    scripts = [p for p in table.values() if Path(p["path"]).name == "osascript"]
    require(all(p["pid"] == allowed_pid for p in scripts), "Another osascript could own the authorization dialog")


def text_of(window):
    return "\n".join(str(node.get(key, "")) for node in window["nodes"] for key in ("title", "text", "description"))


def dialog_evidence(view):
    """Bounded AX labels/roles only, no editable-field values or screenshots."""
    return {"pid": view["pid"], **{key: view[key] for key in ("scope", "originalWindowCount", "focusedWindowAvailable") if key in view},
        "windows": [{"index": window["index"], "nodes": [
        {key: value[:500] if isinstance(value, str) else value for key, value in node.items()
         if key in {"path", "role", "subrole", "title", "description", "enabled"} or
         (key == "text" and node["role"] == "AXStaticText")}
        for node in window["nodes"][:80]]} for window in view["windows"][:4]]}


def confirmation(view, title, version):
    require(view.get("scope") == "updater-focused-window", "Updater confirmation was not selected by OS window focus")
    matches = []
    for window in view["windows"]:
        texts = {str(node.get(key, "")) for node in window["nodes"] for key in ("title", "text")}
        buttons = [n for n in window["nodes"] if n["role"] == "AXButton"]
        install = [n for n in buttons if n["title"] == "Install Update" and n.get("enabled")]
        cancel = [n for n in buttons if n["title"] == "Cancel"]
        expected = {title + " Update", "Administrator Permission Required",
                    f"{title} needs administrator permission to install version {version}. Allow this update to continue?"}
        if expected <= texts and len(install) == len(cancel) == 1:
            matches.append(install[0]["path"])
    require(len(matches) <= 1, "Ambiguous updater elevation confirmation")
    return matches[0] if matches else None


def authorization_controls(view):
    require(len(view["windows"]) == 1, "System authorization window is not unique")
    window = view["windows"][0]
    text = text_of(window)
    instructions = ("Enter your password to allow this.",
                    "Enter an administrator’s name and password to allow this.")
    require("osascript wants to make changes." in text and any(instruction in text for instruction in instructions),
            "System dialog does not identify the expected osascript administrator request")
    fields = [n for n in window["nodes"] if n["role"] == "AXTextField"]
    names = [n for n in fields if n["subrole"] != "AXSecureTextField"]
    passwords = [n for n in fields if n["subrole"] == "AXSecureTextField"]
    buttons = [n for n in window["nodes"] if n["role"] == "AXButton" and n["title"] == "OK" and n.get("enabled")]
    require(len(names) == len(passwords) == len(buttons) == 1,
            "System authorization does not expose one username, password and OK control")
    return {"usernamePath": names[0]["path"], "passwordPath": passwords[0]["path"], "buttonPath": buttons[0]["path"]}


def validate_elevation_script(command, root, cache):
    prefix = '/usr/bin/osascript -e do shell script "'
    # ps can report the binary's argv[0] as simply 'osascript'. Both identities
    # are separately checked with proc_pidpath, never accepted by argv alone.
    if command.startswith('osascript -e '):
        command = '/usr/bin/' + command
    require(command.startswith(prefix) and command.endswith('" with administrator privileges'),
            "Updater child is not the original administrator-privileges script")
    shell = command[len(prefix):-len('" with administrator privileges')]
    match = re.fullmatch(r"mv -f '([^']+)' '([^']+)' && mv -f '([^']+)' '([^']+)' && rm -rf '([^']+)'", shell)
    require(match is not None, "Updater elevation script contains unexpected operations")
    old, temp_old, temp_new, destination, removal = match.groups()
    require(old == destination == str(root) and temp_old == removal and temp_new != temp_old,
            "Updater elevation script targets a different installation")
    for value in (temp_old, temp_new):
        path = Path(value)
        require(path.parent == cache / "VelopackTemp" and re.fullmatch(r"tmp_[A-Za-z0-9]{16}", path.name),
                "Updater elevation temporary path is outside the original default cache")
    return shell


class Context:
    def __init__(self, apps, results):
        self.apps, self.results = apps, Path(results)
        self.username = "bbci_" + secrets.token_hex(6)
        self.secret = secrets.token_urlsafe(32)
        self.home = Path("/Users") / self.username
        self.uid = 10000 + secrets.randbelow(40000)
        self.creation_attempted = False
        self.account_removal_verified = False
        self.tracked = {}
        self.shells = set()
        self.evidence = {"scope": "simulated user authorization on disposable macOS CI; not unattended upgrade",
                         "attribution": ATTRIBUTION, "roles": {}, "prepared": False}

    def save(self):
        self.results.mkdir(parents=True, exist_ok=True)
        (self.results / "macos-authorization.json").write_text(json.dumps(self.evidence, indent=2) + "\n")


def create_account(context, timeout=30):
    # A PTY lets sysadminctl's documented '-password -' prompt work without ever
    # putting the password in argv. Its output is discarded, including on error.
    import pty
    import termios
    username, uid, home = context.username, context.uid, context.home
    require(not any(p.pw_name == username or p.pw_uid == uid for p in pwd.getpwall()) and not home.exists(),
            "Temporary administrator identity already exists")
    context.creation_attempted = True
    pid, fd = pty.fork()
    if pid == 0:
        settings = termios.tcgetattr(0)
        settings[3] &= ~termios.ECHO
        termios.tcsetattr(0, termios.TCSANOW, settings)
        os.execv("/usr/bin/sudo", ["sudo", "-n", "/usr/sbin/sysadminctl", "-addUser", username,
                 "-UID", str(uid), "-GID", "20", "-fullName", username, "-home", str(home),
                 "-shell", "/bin/zsh", "-password", "-", "-admin"])
    deadline, buffer, replies, status = time.monotonic() + timeout, b"", 0, None
    try:
        while time.monotonic() < deadline:
            child, status_value = os.waitpid(pid, os.WNOHANG)
            if child:
                status = status_value
                break
            if select.select([fd], [], [], 0.1)[0]:
                try:
                    buffer = (buffer + os.read(fd, 4096))[-8192:]
                except OSError:
                    continue
                if re.search(rb"(?i)(password|retype|repeat)[^\r\n]*:\s*$", buffer):
                    require(replies < 2, "Unexpected temporary administrator credential prompt")
                    os.write(fd, (context.secret + "\n").encode())
                    replies += 1
                    buffer = b""
        require(status is not None and os.waitstatus_to_exitcode(status) == 0, "Temporary administrator creation failed or timed out")
        require(replies > 0, "Temporary administrator creation did not request its password")
    finally:
        if status is None:
            # Remember any privileged children BEFORE stopping the owned command
            # group, so an incomplete stop also blocks later filesystem cleanup.
            with contextlib.suppress(Exception):
                table = process_table()
                for child_pid in descendants(table, pid):
                    if child_pid in table:
                        context.tracked[(child_pid, table[child_pid]["started"])] = table[child_pid]
            # Only the new session we created above; never the caller's group.
            with contextlib.suppress(Exception):
                run(["/usr/bin/sudo", "-n", "/bin/kill", "-TERM", "--", "-" + str(pid)])
            wait_deadline = time.monotonic() + 2
            while time.monotonic() < wait_deadline:
                try:
                    if os.waitpid(pid, os.WNOHANG)[0]:
                        status = 1
                        break
                except ChildProcessError:
                    status = 1
                    break
                time.sleep(0.05)
            if status is None:
                with contextlib.suppress(Exception):
                    run(["/usr/bin/sudo", "-n", "/bin/kill", "-KILL", "--", "-" + str(pid)])
                with contextlib.suppress(ChildProcessError):
                    os.waitpid(pid, os.WNOHANG)
        os.close(fd)
        buffer = b""
    entry = pwd.getpwnam(username)
    require(entry.pw_uid == uid and Path(entry.pw_dir) == home, "Created administrator identity differs")
    # PAM's authorization account check rejects /usr/bin/false even when the
    # separate OpenDirectory password check succeeds. Use a normal shell only
    # for this new disposable fixture, without changing system auth policy.
    require(entry.pw_shell == "/bin/zsh", "Temporary administrator shell is not valid for authorization")
    require("admin" in run(["/usr/bin/id", "-Gn", username]).split(),
            "Temporary authorization user is not an administrator")
    context.evidence["fixturePasswordPromptReplies"] = replies
    context.evidence["fixtureShellVerified"] = "/bin/zsh"


def verify_account_password(context, timeout):
    require(0 < timeout <= 5, "Invalid temporary administrator password verification deadline")
    response = run([sys.executable, Path(__file__).with_name("installed-macos-account.py")],
                   input_text=json.dumps({"username": context.username, "uid": context.uid, "secret": context.secret}),
                   timeout=timeout)
    try:
        result = json.loads(response)
    except ValueError:
        raise AuthorizationFailure("Temporary administrator verifier returned invalid evidence") from None
    valid = isinstance(result, dict) and set(result) <= {"verified", "error", "nativeErrorCode", "phase", "exceptionKind"} and type(result.get("verified")) is bool
    if valid and "error" in result:
        valid = isinstance(result["error"], str) and result["error"] in {
            "HostedRunnerRequired", "NativeMacRequired", "InvalidInput", "InvalidIdentity", "AccountIdentityChanged",
            "AccountHomeChanged", "AccountShellInvalid", "AdministratorRequired", "NativeAllocationFailed", "LocalNodeUnavailable",
            "LocalRecordUnavailable", "PasswordVerificationFailed", "InputTooLarge", "VerificationUnavailable"}
    if valid and "nativeErrorCode" in result:
        valid = type(result["nativeErrorCode"]) is int and -2147483648 <= result["nativeErrorCode"] <= 2147483647
    for key, allowed in (
        ("phase", {"initial", "account-lookup", "home", "admin", "load-frameworks", "local-node", "local-record", "verify-password"}),
        ("exceptionKind", {"AccountFailure", "KeyError", "FileNotFoundError", "PermissionError", "NotADirectoryError",
                           "OSError", "ValueError", "TypeError", "AttributeError", "ImportError", "RuntimeError",
                           "JSONDecodeError", "OverflowError", "MemoryError", "Other"})):
        if valid and key in result:
            valid = isinstance(result[key], str) and result[key] in allowed
    require(valid, "Temporary administrator verifier returned invalid evidence")
    context.evidence["fixturePasswordVerification"] = result
    require(result == {"verified": True}, "Temporary administrator password verification failed")


def prepare(apps, results, *, timeout_seconds=45):
    prepare_deadline = time.monotonic() + timeout_seconds
    require(set(apps) == {"unified", "client"}, "Authorization requires both known products")
    rid = apps["unified"]["rid"]
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), rid)
    require(rid.startswith("osx-") and all(app["rid"] == rid for app in apps.values()), "macOS authorization only")
    require(0 < timeout_seconds <= 60, "Authorization preflight time budget is invalid")
    for role, app in apps.items():
        assembly = base.contract.PRODUCTS[role]["assembly"]
        root = app["installRoot"]
        require(root.parent == Path("/Applications") and root.suffix == ".app" and
                app["exe"] == root / "Contents/MacOS" / assembly and not root.is_symlink(),
                "Authorization application identity is not the original system installation")
    require(run(["/usr/bin/stat", "-f", "%u", "/dev/console"]).strip() == str(os.getuid()),
            "Runner has no active console session for this user")
    require(ui({"operation": "preflight"}) == {"enabled": True}, "System Events GUI scripting is unavailable; TCC is unchanged")
    table = process_table()
    require_no_competing_script(table)
    require(not security_views(table), "A pre-existing system authorization window prevents attribution")
    context = Context(apps, results)
    context.evidence["preflight"] = {"consoleUserMatches": True, "guiScriptingEnabled": True,
                                     "preexistingAuthorizationWindows": 0, "competingOsascriptProcesses": 0}
    try:
        remaining = prepare_deadline - time.monotonic()
        require(remaining > 0, "Authorization preflight exhausted its time budget")
        create_account(context, timeout=min(30, remaining))
        remaining = prepare_deadline - time.monotonic()
        require(remaining > 0, "Administrator creation exhausted the authorization preparation budget")
        verify_account_password(context, timeout=min(5, remaining))
        context.evidence.update(prepared=True, fixtureAccount={"name": context.username, "uid": context.uid, "home": str(context.home)})
        context.save()
        return context
    except (Exception, KeyboardInterrupt) as error:
        context.evidence["preparationFailed"] = True
        context.evidence["preparationFailureType"] = type(error).__name__
        if isinstance(error, AuthorizationFailure):
            context.evidence["preparationFailureMessage"] = str(error)
        try:
            context.evidence["preparationCleanup"] = cleanup(context)
        except (Exception, KeyboardInterrupt) as cleanup_error:
            context.evidence["preparationCleanup"] = {
                "passed": False, "canRemoveOwnedPaths": False,
                "remainingProcesses": [{"verification": "preparation cleanup did not complete"}],
                "error": type(cleanup_error).__name__}
            context.secret = None
        # A failed prepare never assigns the caller's context variable. Preserve
        # it on a fixed-message exception so the lifecycle finally can retry and
        # enforce the same process/account/file cleanup gates as normal prepare.
        raise PreparationFailure(context) from None


def command_for_observed_process(record):
    """Read a command only while the sampled PID/start identity still exists.

    A short-lived shell can exit between the process snapshot and ps. Only a
    fresh identity check can explain a nonzero ps exit as disappearance; a
    timeout, failed recheck, or error for the same live process stays a failure.
    """
    arguments = ["/bin/ps", "-ww", "-p", str(record["pid"]), "-o", "command="]
    try:
        result = subprocess.run(arguments, text=True, capture_output=True, timeout=10)
    except subprocess.TimeoutExpired:
        raise AuthorizationFailure(f"Authorization process command query timed out (stage=track-command, pid={record['pid']})") from None
    except OSError:
        raise AuthorizationFailure(f"Authorization process command query could not start (stage=track-command, pid={record['pid']})") from None
    try:
        latest = process_table()
    except AuthorizationFailure as error:
        # process_table/run expose fixed failures only, never the ps output.
        raise AuthorizationFailure(f"Authorization process identity recheck failed (stage=track-recheck, pid={record['pid']}): {error}") from None
    except Exception as error:
        raise AuthorizationFailure(f"Authorization process identity recheck failed (stage=track-recheck, pid={record['pid']}, errorType={type(error).__name__})") from None
    if not same_process(record, latest.get(record["pid"])):
        return None, latest
    require(result.returncode == 0,
            f"Authorization process command query returned exit {result.returncode} for the same live process (stage=track-command, pid={record['pid']})")
    return result.stdout.strip(), latest


def track(context, table, updater_pid=None):
    if updater_pid is not None and updater_pid in table:
        for pid in descendants(table, updater_pid):
            # A successful updater also launches /usr/bin/open -> the new app.
            # That application/WebKit subtree is NOT an outstanding file writer.
            if pid == updater_pid or Path(table[pid]["path"]).name in {"osascript", "sh", "bash", "mv", "rm"}:
                context.tracked[(pid, table[pid]["started"])] = table[pid]
    # Privileged shells may be reparented to launchd. Identify ONLY the exact
    # validated shell command for our randomly named original Velopack temps.
    for record in table.values():
        if context.shells and record["uid"] == 0 and Path(record["path"]).name in {"sh", "bash"}:
            command, latest = command_for_observed_process(record)
            if any(command in ("/bin/sh -c " + shell, "sh -c " + shell, "/bin/bash -c " + shell) for shell in context.shells):
                for pid in descendants(latest, record["pid"]):
                    context.tracked[(pid, latest[pid]["started"])] = latest[pid]


def observe_submitted_dialog(evidence, table, deadline):
    """One bounded read of the same system process, never a submission retry.

    A returned click only means the UI command completed. Labels after the
    submission help distinguish a still-visible prompt from an exited dialog;
    neither observation alone proves acceptance or successful native apply.
    """
    observation = evidence["postSubmissionObservation"] = {"readOnly": True, "observedEpoch": time.time()}
    original = evidence["systemAuthorizationProcess"]
    current = table.get(original["pid"])
    if current is None:
        observation["processExited"] = True
        return
    if not same_process(original, current):
        observation["error"] = "System authorization process identity changed"
        return
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        observation["error"] = "No remaining authorization observation budget"
        return
    try:
        require(executable(original["pid"]) in SECURITY_AGENTS, "Unexpected system authorization executable")
        view = ui({"operation": "snapshot", "pid": original["pid"]}, timeout=min(5, remaining))
        observation["dialog"] = dialog_evidence(view)
    except Exception as error:
        # Diagnostics never expose arbitrary system exception messages or field
        # values, and cannot turn an incomplete authorization into a pass.
        observation["error"] = type(error).__name__


def capture_authentication_diagnostics(context, evidence):
    """Failure-only diagnostics cannot replace the original authorization result."""
    try:
        spec = importlib.util.spec_from_file_location(
            "installed_auth_diagnostics", Path(__file__).with_name("installed-macos-auth-diagnostics.py"))
        diagnostics = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(diagnostics)
        evidence["systemAuthenticationLog"] = diagnostics.capture(
            evidence["systemAuthorizationProcess"], evidence["authorizationStartedEpoch"], context.secret)
    except Exception:
        evidence["systemAuthenticationLog"] = {"collected": False, "error": "DiagnosticUnavailable"}


def authorize(context, app, updater_pid, *, target_version, deadline):
    require(context.evidence["prepared"] and context.secret, "Authorization context is not prepared")
    require(app is context.apps[app["role"]] and app["role"] not in context.evidence["roles"], "Repeated or unknown product authorization")
    require(type(updater_pid) is int and updater_pid > 1 and 0 < deadline - time.monotonic() <= 125,
            "Invalid updater identity or authorization deadline")
    require(re.fullmatch(r"[0-9A-Za-z.+-]{1,100}", target_version), "Invalid target version")
    root = app["installRoot"]
    assembly = base.contract.PRODUCTS[app["role"]]["assembly"]
    cache = Path.home() / "Library/Caches/velopack" / assembly / "packages"
    title = "Bakabase" if app["role"] == "unified" else "Bakabase Client"
    evidence = context.evidence["roles"][app["role"]] = {"updaterPid": updater_pid, "targetVersion": target_version,
                  "confirmationSubmitted": False, "credentialsSubmitted": False, "required": False, "completed": False,
                  "confirmationSnapshotChanges": 0,
                  "stage": "waiting-for-updater-confirmation", "originalPermissions": permission_snapshot(root),
                  "authorizationStartedEpoch": time.time()}
    initial, observation_at = None, None
    try:
        while time.monotonic() < deadline:
            table = process_table()
            table_observed_epoch = time.time()
            current = table.get(updater_pid)
            track(context, table, updater_pid)
            if current is None:
                evidence["completed"] = True
                evidence["updaterExited"] = True
                evidence["stage"] = "updater-exited; native-apply-and-restart-still-require-independent-verification"
                return evidence  # Caller still MUST prove native apply and automatic restart.
            if initial is None:
                require(executable(updater_pid) == str(app["exe"].parent / "UpdateMac"), "Updater PID is not this product's original updater")
                initial = current
                evidence["updaterProcess"] = initial
            require(same_process(initial, current), "Updater PID was reused during authorization")
            if not evidence["confirmationSubmitted"]:
                require_no_competing_script(table)
                require(not security_views(table), "Authorization window appeared before the owned updater confirmation")
                view = ui({"operation": "snapshot", "scope": "updater-focused-window", "pid": updater_pid})
                evidence["lastUpdaterDialog"] = dialog_evidence(view)
                button = confirmation(view, title, target_version)
                if button:
                    evidence["required"] = True
                    try:
                        submitted = ui({"operation": "confirm", "scope": "updater-focused-window", "pid": updater_pid,
                                        "expected": view, "buttonPath": button})
                    except AuthorizationUIFailure as error:
                        if error.diagnostic["stage"] != "validate-snapshot" or error.diagnostic["code"] != "SnapshotChanged":
                            raise
                        # This exact JXA guard runs before resolving or clicking
                        # the button. Reobserve the whole dialog and recheck all
                        # process/attribution guards at the next loop iteration.
                        # A third change fails; no deadline is extended and no
                        # uncertain action or credential submission is retried.
                        evidence["confirmationSnapshotChanges"] += 1
                        if evidence["confirmationSnapshotChanges"] >= 3:
                            raise
                        evidence["stage"] = "reobserving-updater-confirmation-before-click"
                        continue
                    require(submitted == {"submitted": True},
                            "Updater confirmation was not submitted")
                    evidence["confirmationSubmitted"] = True
                    evidence["stage"] = "waiting-for-original-osascript-system-authorization"
            elif not evidence["credentialsSubmitted"]:
                children = [p for p in table.values() if p["parentPid"] == updater_pid and Path(p["path"]).name == "osascript"]
                require(len(children) <= 1, "Multiple updater authorization script children")
                if children:
                    child = children[0]
                    require_no_competing_script(table, child["pid"])
                    require(executable(child["pid"]) == "/usr/bin/osascript", "Updater child is not the system osascript")
                    command = run(["/bin/ps", "-ww", "-p", str(child["pid"]), "-o", "command="]).strip()
                    shell = validate_elevation_script(command, root, cache)
                    context.shells.add(shell)
                    evidence["originalOsascript"] = child
                    views = security_views(table)
                    require(len(views) <= 1, "Multiple system authorization dialogs prevent attribution")
                    if views:
                        view = views[0]
                        evidence["lastSystemDialog"] = dialog_evidence(view)
                        controls = authorization_controls(view)
                        latest = process_table()
                        require(same_process(initial, latest.get(updater_pid)) and same_process(child, latest.get(child["pid"])) and
                                latest[child["pid"]]["parentPid"] == updater_pid,
                                "Original authorization process chain changed before credential input")
                        require_no_competing_script(latest, child["pid"])
                        system_process = table.get(view["pid"])
                        require(same_process(system_process, latest.get(view["pid"])),
                                "System authorization process changed before credential input")
                        evidence["systemAuthorizationProcess"] = dict(system_process, firstObservedEpoch=table_observed_epoch)
                        # Mark attempted BEFORE acting: any timeout/error prevents
                        # a credential retry or a second dialog submission.
                        evidence["credentialsSubmitted"] = True
                        require(ui({"operation": "authorize", "pid": view["pid"], "expected": view,
                                    "username": context.username, "secret": context.secret, **controls}) == {"submitted": True},
                                "System authorization submission did not complete")
                        evidence["systemAuthorizationPid"] = view["pid"]
                        evidence["credentialFieldsWritable"] = True
                        evidence["usernameValueVerified"] = True
                        evidence["stage"] = "one-credential-submission; waiting-for-original-updater-exit"
                        observation_at = time.monotonic() + 3
                else:
                    require_no_competing_script(table)
                    require(not security_views(table), "Unattributed system authorization dialog")
            elif observation_at is not None and time.monotonic() >= observation_at:
                observation_at = None
                observe_submitted_dialog(evidence, table, deadline)
            time.sleep(0.15)
        raise AuthorizationFailure("Original updater authorization exceeded the existing startup deadline")
    finally:
        if not evidence["completed"] and "systemAuthorizationProcess" in evidence:
            capture_authentication_diagnostics(context, evidence)
        context.save()


def cancel_unsubmitted_requests(context, table, evidence):
    """Cancel only a verified waiting request after its updater has been stopped.

    A successful cancellation is failure cleanup, not successful authorization or
    apply. Never terminate a submitted authorization or a privileged file writer.
    """
    candidates = []
    roles = context.evidence.get("roles", {})
    for role, state in roles.items():
        original = state.get("originalOsascript")
        current = table.get(original["pid"]) if original else None
        if current is None:
            continue
        require(same_process(original, current), "Recorded authorization PID was reused; cancellation is forbidden")
        require(state.get("credentialsSubmitted") is False and state.get("confirmationSubmitted") is True and
                state.get("required") is True, "Authorization was submitted or is not a verified waiting request")
        candidates.append((role, state, original))
    if not candidates:
        return table
    require(all(state.get("credentialsSubmitted") is False for state in roles.values()),
            "Credentials were submitted; outstanding authorization requests must remain blocked")

    def verify_no_writer(values):
        track(context, values)
        for _, _, original in candidates:
            if same_process(original, values.get(original["pid"])):
                track(context, values, original["pid"])
        evidence["remainingProcesses"] = [record for record in context.tracked.values()
                                           if same_process(record, values.get(record["pid"]))]
        require(not any(record.get("uid") == 0 and Path(record["path"]).name in {"sh", "bash", "mv", "rm"}
                        for record in context.tracked.values()),
                "A privileged writer was observed; cancellation is forbidden")
        for _, _, original in candidates:
            require(not any(values[pid].get("uid") == 0 for pid in descendants(values, original["pid"]) if pid in values),
                    "A privileged authorization descendant exists; cancellation is forbidden")

    def verify_request(role, state, original, values):
        current = values.get(original["pid"])
        require(same_process(original, current) and current.get("uid") == original.get("uid") != 0,
                "Original authorization process identity changed; cancellation is forbidden")
        require(original.get("parentPid") == state.get("updaterPid") and state.get("updaterProcess", {}).get("pid") == state.get("updaterPid"),
                "Original authorization parent was not verified")
        require(state["updaterPid"] not in values, "Original updater PID is still present; cancellation is forbidden")
        require(current.get("parentPid") in (1, state["updaterPid"]), "Authorization was reparented unexpectedly")
        require(executable(original["pid"]) == "/usr/bin/osascript", "Original authorization executable changed")
        app = context.apps[role]
        cache = Path.home() / "Library/Caches/velopack" / base.contract.PRODUCTS[role]["assembly"] / "packages"
        command = run(["/bin/ps", "-ww", "-p", str(original["pid"]), "-o", "command="], timeout=3).strip()
        shell = validate_elevation_script(command, app["installRoot"], cache)
        require(shell in context.shells, "Authorization command was not previously verified")

    # Validate every candidate before mutating any process. Unknown or competing
    # writers therefore cannot be hidden by first cancelling a familiar child.
    verify_no_writer(table)
    for role, state, original in candidates:
        verify_request(role, state, original, table)
    cancellations = evidence["cancelledUnsubmittedRequests"] = []
    for role, state, original in candidates:
        table = process_table()
        verify_no_writer(table)
        verify_request(role, state, original, table)
        item = {"role": role, "process": original, "signal": "SIGTERM", "exited": False,
                "scope": "failed-update cleanup only; no credentials submitted"}
        cancellations.append(item)
        os.kill(original["pid"], signal.SIGTERM)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            table = process_table()
            verify_no_writer(table)
            if not same_process(original, table.get(original["pid"])):
                item["exited"] = True
                break
            time.sleep(0.1)
        require(item["exited"], "Original authorization request did not exit after bounded cancellation")
    return table


def cleanup(context):
    evidence = {"accountRemoved": False, "homeRemoved": False, "remainingProcesses": [], "canRemoveOwnedPaths": False, "passed": False,
                "accountRemovalPreviouslyVerified": context.account_removal_verified}
    context.evidence["cleanup"] = evidence
    try:
        table = process_table()
        track(context, table)
        evidence["remainingProcesses"] = [record for record in context.tracked.values()
                                           if same_process(record, table.get(record["pid"]))]
        table = cancel_unsubmitted_requests(context, table, evidence)
        evidence["remainingProcesses"] = [record for record in context.tracked.values()
                                           if same_process(record, table.get(record["pid"]))]
        require(not evidence["remainingProcesses"], "Authorization processes remain; fixture paths must not be removed")
        require(re.fullmatch(r"bbci_[0-9a-f]{12}", context.username) and context.home == Path("/Users") / context.username,
                "Refusing cleanup of an unowned administrator identity")
        # Once removal has been independently verified, retries only recheck
        # absence. Never delete again based on a name lookup that disagrees
        # with the earlier enumeration, or delete a subsequently recreated user.
        # Keep this milestone on the context even if a later recheck fails.
        if not context.account_removal_verified:
            try:
                entry = pwd.getpwnam(context.username)
            except KeyError:
                entry = None
            if entry:
                require(context.creation_attempted and entry.pw_uid == context.uid and Path(entry.pw_dir) == context.home,
                        "Administrator cleanup identity differs from the created fixture")
                require(not context.home.is_symlink(), "Administrator home is an unexpected symlink")
                run(["/usr/bin/sudo", "-n", "/usr/sbin/sysadminctl", "-deleteUser", context.username], timeout=30)
        require(not any(p.pw_name == context.username or p.pw_uid == context.uid for p in pwd.getpwall()),
                "Temporary administrator account remains")
        require(not context.home.exists() and not context.home.is_symlink(), "Temporary administrator home remains")
        context.account_removal_verified = True
        evidence.update(accountRemoved=True, homeRemoved=True, canRemoveOwnedPaths=True, passed=True)
    except Exception as error:
        # Report only our fixed messages, never subprocess stderr or an account
        # tool's raw output. The caller must treat this as a cleanup gate failure.
        evidence["error"] = str(error) if isinstance(error, AuthorizationFailure) else type(error).__name__
    finally:
        context.secret = None
        context.save()
    return evidence
