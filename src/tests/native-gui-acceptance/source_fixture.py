#!/usr/bin/env python3
"""Owned native source for GUI CI; never a second installed product.

The source uses the production Shell/Service with a private test entry point.
Only resource fixture preparation uses writes to the production API. Sharing,
pairing, browsing, searching and detail opening belong to the native UI driver.
No raw product logs, API credentials, command lines or federation state are
copied into results. All native operations require a disposable hosted runner.
"""
import ctypes
import hashlib
import http.client
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time
import uuid

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("source_fixture_package_guard", HERE.parent / "upgrade-tests/run-package-acceptance.py")
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)
NAME = "Bakabase.NativeGui.SourceHost"
BUNDLE = "com.anobaka.bakabase.native-gui-source-fixture"
SCOPE = "native-gui-source-fixture"
DATA_ENV = "BAKABASE_NATIVE_GUI_SOURCE_DATA_DIR"
MAX_RESPONSE = 2 * 1024 * 1024
MAX_FILES = 8192
MAX_TREE_BYTES = 1024 * 1024 * 1024
TITLES = ("Native source alpha.txt", "Native source beta", "Native source gamma")
MEDIA = b"Owned native GUI source fixture; no user data.\n"
INTRODUCTION = "Native GUI source detail sentinel"
SHELL_DIAGNOSTIC_SCOPE = "native-gui-shell-failure-diagnostic"
SHELL_DIAGNOSTIC_TYPES = {
    "Other", "System.ArgumentException", "System.ArgumentNullException", "System.ArgumentOutOfRangeException",
    "System.BadImageFormatException", "System.DllNotFoundException", "System.EntryPointNotFoundException",
    "System.InvalidOperationException", "System.NotSupportedException", "System.NullReferenceException",
    "System.TypeInitializationException", "System.TypeLoadException", "System.MissingMethodException",
    "System.MissingFieldException", "System.IO.FileNotFoundException", "System.IO.DirectoryNotFoundException",
    "System.IO.IOException", "System.UnauthorizedAccessException", "System.Reflection.TargetInvocationException",
    "System.Runtime.InteropServices.COMException", "System.ComponentModel.Win32Exception",
    "Avalonia.Markup.Xaml.XamlLoadException", "Avalonia.Markup.Xaml.XamlParseException"}
SHELL_DIAGNOSTIC_METHODS = {"main-window-constructor", "main-window-xaml", "native-control-create",
                            "windows-control-create", "navigate", "windows-navigate", "show-main-window"}


class FixtureFailure(AssertionError):
    pass


def require(condition, code):
    if not condition:
        raise FixtureFailure(code)


def remaining(deadline, maximum):
    if deadline is None:
        return maximum
    require(type(deadline) in (int, float) and math.isfinite(deadline), "SourceDeadlineInvalid")
    value = min(maximum, deadline - time.monotonic())
    require(value > 0, "SourceOperationDeadlineExceeded")
    return value


def limited_deadline(deadline, maximum):
    duration = remaining(deadline, maximum)
    now = time.monotonic()
    return now + duration if deadline is None else min(deadline, now + duration)


def hosted(rid):
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), rid)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()


def digest(path):
    return base.sha256(Path(path))


def unlinked(path, *, exists=True):
    """Absolute lexical paths only; resolving first would conceal a symlink."""
    path = Path(path)
    require(path.is_absolute(), "SourcePathNotAbsolute")
    require(".." not in path.parts, "SourceParentTraversal")
    for item in (path, *path.parents):
        require(not item.is_symlink(), "SourcePathTraversesLink")
        if item.exists():
            require(not bool(getattr(item.stat(), "st_file_attributes", 0) & 0x400), "SourcePathTraversesReparsePoint")
    if exists:
        require(path.exists(), "SourcePathMissing")
    return path.resolve()


def beneath(path, root, *, exists=True):
    root = unlinked(root)
    path = unlinked(path, exists=exists)
    require(path != root and path.is_relative_to(root), "SourcePathEscapesOwner")
    return path


def owned_json(path, root, limit):
    path = beneath(path, root)
    require(path.is_file() and path.stat().st_size <= limit, "SourceJsonBudgetOrTypeInvalid")
    value = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(value, dict), "SourceJsonObjectRequired")
    return value


def shell_failure_diagnostics(app, stopped):
    """Only inspect the stopped fixture's fixed, bounded metadata file."""
    validate_probe_app(app)  # Hosted and exact fixture ownership precede file reads.
    process = stopped.get("process", {})
    pid = process.get("pid")
    require(stopped.get("ownedProcessesStopped") is True and stopped.get("remainingProcesses") == [] and
            type(pid) is int and 0 < pid < 2**31 and process.get("executable") == str(app["exe"]) and
            isinstance(process.get("started"), str) and bool(process["started"]), "SourceDiagnosticOwnerNotStopped")
    path = beneath(app["data"] / f"native-shell-diagnostics-{pid}.json", app["fixtureRoot"], exists=False)
    if not path.exists():
        return {"available": False, "code": "SourceShellDiagnosticMissing"}
    require(path.is_file() and 0 < path.stat().st_size <= 4096, "SourceShellDiagnosticBudgetOrTypeInvalid")
    try:
        def unique_pairs(pairs):
            result = {}
            for key, item in pairs:
                if key in result:
                    raise ValueError("Duplicate diagnostic field")
                result[key] = item
            return result
        with path.open("rb") as stream:
            raw = stream.read(4097)
        require(len(raw) <= 4096, "SourceShellDiagnosticBudgetOrTypeInvalid")
        value = json.loads(raw, object_pairs_hook=unique_pairs)
        require(isinstance(value, dict) and set(value) == {"schemaVersion", "scope", "pid", "observations"} and
                type(value["schemaVersion"]) is int and value["schemaVersion"] == 1 and
                value["scope"] == SHELL_DIAGNOSTIC_SCOPE and type(value["pid"]) is int and value["pid"] == pid and
                isinstance(value["observations"], list) and len(value["observations"]) <= 8,
                "SourceShellDiagnosticInvalid")
        for item in value["observations"]:
            require(isinstance(item, dict) and set(item) == {"exceptionType", "hresult", "method"} and
                    item["exceptionType"] in SHELL_DIAGNOSTIC_TYPES and item["method"] in SHELL_DIAGNOSTIC_METHODS and
                    type(item["hresult"]) is int and -(2**31) <= item["hresult"] < 2**31,
                    "SourceShellDiagnosticInvalid")
    except (ValueError, TypeError, KeyError):
        raise FixtureFailure("SourceShellDiagnosticInvalid") from None
    return {"available": True, **value}


def inventory(directory):
    directory = unlinked(directory)
    require(directory.is_dir(), "SourceInventoryNotDirectory")
    result, size = {}, 0
    for current, directories, files in os.walk(directory, followlinks=False):
        for name in directories + files:
            path = unlinked(Path(current) / name)
            require(path.is_relative_to(directory), "SourceInventoryEscapesRoot")
        for name in sorted(files):
            path = Path(current) / name
            require(path.is_file(), "SourceInventorySpecialFile")
            size += path.stat().st_size
            require(len(result) < MAX_FILES and size <= MAX_TREE_BYTES, "SourceInventoryBudgetExceeded")
            result[path.relative_to(directory).as_posix()] = {"sizeBytes": path.stat().st_size, "sha256": digest(path)}
    require(result, "SourceInventoryEmpty")
    return dict(sorted(result.items()))


def verify_candidate(candidate, rid):
    require(isinstance(candidate, dict) and candidate.get("passed") is True and
            candidate.get("unchangedProductSources") is True and candidate.get("rid") == rid,
            "SourceCandidateNotVerified")
    for field in ("packageSourceSHA", "testSourceSHA"):
        require(isinstance(candidate.get(field), str) and re.fullmatch(r"[0-9a-f]{40}", candidate[field]), "SourceCandidateSHAInvalid")
    require(type(candidate.get("runID")) is int and candidate["runID"] > 0 and
            isinstance(candidate.get("version"), str) and candidate["version"] and
            candidate.get("repository") == os.environ.get("GITHUB_REPOSITORY"), "SourceCandidateRunInvalid")
    require(candidate["testSourceSHA"] == os.environ.get("GITHUB_SHA"), "SourceBuildHeadMismatch")


def macos_bundle_paths(home):
    """Only the new test bundle's own known domains, never shared parents."""
    home = unlinked(home)
    return [(home / "Library" / parent / name, kind) for parent, name, kind in (
        ("Caches", BUNDLE, "directory"),
        ("WebKit", BUNDLE, "directory"),
        ("Preferences", BUNDLE, "directory"),
        ("Preferences", BUNDLE + ".plist", "file"),
        ("Saved Application State", BUNDLE + ".savedState", "directory"),
        ("HTTPStorages", BUNDLE, "directory"),
        ("HTTPStorages", BUNDLE + ".binarycookies", "file"),
        ("Cookies", BUNDLE + ".binarycookies", "file"),
        ("Application Support", BUNDLE, "directory"))]


def external_preflight(rid):
    hosted(rid)
    if not rid.startswith("osx-"):
        return {"passed": True, "applicable": False, "paths": []}
    home = unlinked(Path.home())
    paths = []
    for path, kind in macos_bundle_paths(home):
        beneath(path, home, exists=False)
        require(not path.exists() and not path.is_symlink(), "SourceBundleDomainAlreadyExists")
        paths.append({"path": str(path), "kind": kind, "absentBeforeLaunch": True})
    return {"passed": True, "applicable": True, "home": str(home), "bundleIdentifier": BUNDLE, "paths": paths}


def validate_external_tree(path, kind):
    path = unlinked(path)
    require(path.is_file() if kind == "file" else path.is_dir(), "SourceBundleDomainTypeChanged")
    count, size = 0, 0
    if kind == "file":
        require(path.stat().st_size <= 8 * 1024 * 1024, "SourceBundleFileBudgetExceeded")
        return {"fileCount": 1, "sizeBytes": path.stat().st_size}
    for parent, directories, files in os.walk(path, followlinks=False):
        for name in directories + files:
            beneath(Path(parent) / name, path)
            count += 1
            require(count <= 8192, "SourceBundleTreeBudgetExceeded")
        for name in files:
            item = Path(parent) / name
            require(item.is_file(), "SourceBundleSpecialFile")
            size += item.stat().st_size
            require(size <= 256 * 1024 * 1024, "SourceBundleTreeBudgetExceeded")
    return {"entryCount": count, "sizeBytes": size}


def validate_probe_app(app):
    """Explicit source-fixture branch for probe.hosted; never relax product roles."""
    hosted(app["rid"])  # Before filesystem reads or process introspection.
    require(app.get("role") == "source-fixture", "SourceRoleInvalid")
    root = beneath(app["fixtureRoot"], Path(os.environ["RUNNER_TEMP"]))
    proof = owned_json(root / "source-owner.json", root, 4096)
    source = app.get("sourceProvenance")
    require(isinstance(source, dict) and source.get("schemaVersion") == 1 and source.get("scope") == SCOPE and
            source.get("rid") == app["rid"] and proof.get("scope") == SCOPE and
            re.fullmatch(r"[0-9a-f]{32}", proof.get("token", "")) and
            root.name == "native-gui-source-" + proof["token"] and
            source.get("privateInstanceId") == NAME + "." + proof["token"], "SourceOwnershipProofInvalid")
    exe = beneath(app["exe"], root)
    data = beneath(app["data"], root)
    require(exe.name == NAME + (".exe" if app["rid"] == "win-x64" else "") and exe.is_file() and
            str(exe) == source.get("executable") and str(data) == source.get("dataDirectory") and
            source.get("executableSHA256") == digest(exe), "SourceExecutableProofInvalid")
    require(owned_json(root / "source-provenance.json", root, 65536) == source,
            "SourceProvenanceChanged")
    require(type(app.get("port")) is int and 1024 <= app["port"] <= 65535 and
            app["port"] == proof.get("port"), "SourcePortProofInvalid")
    for name, expected in source["criticalBinarySHA256"].items():
        require(name in (NAME + ".dll", "Bakabase.Shell.dll", "Bakabase.Service.dll") and
                digest(beneath(exe.parent / name, root)) == expected, "SourceBinaryChanged")
    require(set(source["criticalBinarySHA256"]) == {NAME + ".dll", "Bakabase.Shell.dll", "Bakabase.Service.dll"},
            "SourceBinaryProofIncomplete")
    return source


def _native_identity(pid, rid, timeout=5):
    hosted(rid)
    require(type(pid) is int and pid > 0, "SourcePidInvalid")
    deadline = limited_deadline(None, timeout)
    if rid.startswith("osx-"):
        lib = ctypes.CDLL("/usr/lib/libproc.dylib")
        lib.proc_pidpath.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
        lib.proc_pidpath.restype = ctypes.c_int
        buffer = ctypes.create_string_buffer(4096)
        if lib.proc_pidpath(pid, buffer, len(buffer)) <= 0:
            # Distinguish absence from inability to establish a live identity.
            result = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "pid="], capture_output=True, text=True, timeout=remaining(deadline, 3))
            require(result.returncode == 1 and not result.stdout.strip(), "SourceLiveIdentityUnavailable")
            return None
        result = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "lstart="], capture_output=True, text=True, timeout=remaining(deadline, 3))
        if result.returncode == 1 and not result.stdout.strip():
            return None
        require(result.returncode == 0 and result.stdout.strip(), "SourceStartIdentityUnavailable")
        return {"pid": pid, "executable": buffer.value.decode(), "started": result.stdout.strip()}
    script = "$ErrorActionPreference='Stop'; $p=Get-CimInstance Win32_Process -Filter ('ProcessId = '+$env:NATIVE_SOURCE_PID) -Property ProcessId,ExecutablePath,CreationDate; if($null -eq $p){'null'}else{[ordered]@{pid=[int]$p.ProcessId;executable=$p.ExecutablePath;started=$p.CreationDate.ToUniversalTime().ToString('o')}|ConvertTo-Json -Compress}"
    result = subprocess.run(["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
                            env=dict(os.environ, NATIVE_SOURCE_PID=str(pid)), capture_output=True, text=True, timeout=remaining(deadline, 5))
    require(result.returncode == 0 and len(result.stdout) <= 16384, "SourceIdentityQueryFailed")
    value = json.loads(result.stdout)
    if value is None:
        return None
    require(value.get("pid") == pid and value.get("executable") and value.get("started"), "SourceIdentityIncomplete")
    return value


def _descendant_ids(pid, rid, timeout=5):
    hosted(rid)
    if rid.startswith("osx-"):
        result = subprocess.run(["/bin/ps", "-axo", "pid=,ppid="], capture_output=True, text=True, timeout=timeout)
        require(result.returncode == 0 and len(result.stdout) <= MAX_RESPONSE, "SourceProcessTreeUnavailable")
        pairs = [tuple(map(int, line.split())) for line in result.stdout.splitlines() if line.strip()]
        selected, pending = [], [pid]
        for _ in range(16):
            next_level = [child for child, parent in pairs if parent in pending and child != pid and child not in selected]
            selected += next_level
            require(len(selected) <= 64, "SourceProcessTreeBudgetExceeded")
            if not next_level:
                return selected
            pending = next_level
        raise FixtureFailure("SourceProcessTreeDepthExceeded")
    script = """$ErrorActionPreference='Stop'; $all=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId); $todo=@([int]$env:NATIVE_SOURCE_PID); $seen=@(); for($depth=0;$depth -lt 16;$depth++){ $next=@($all|Where-Object { $todo -contains [int]$_.ParentProcessId -and $seen -notcontains [int]$_.ProcessId -and [int]$_.ProcessId -ne [int]$env:NATIVE_SOURCE_PID }|ForEach-Object {[int]$_.ProcessId}); $seen+= $next; if($seen.Count -gt 64){throw 'tree-budget'}; if($next.Count -eq 0){ConvertTo-Json -InputObject @($seen) -Compress; exit 0}; $todo=$next }; throw 'tree-depth'"""
    result = subprocess.run(["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
                            env=dict(os.environ, NATIVE_SOURCE_PID=str(pid)), capture_output=True, text=True, timeout=timeout)
    require(result.returncode == 0 and len(result.stdout) <= 16384, "SourceProcessTreeUnavailable")
    values = json.loads(result.stdout)
    require(isinstance(values, list) and len(values) <= 64 and all(type(p) is int and p > 0 and p != pid for p in values),
            "SourceProcessTreeInvalid")
    return values


def pid_module():
    spec = importlib.util.spec_from_file_location("source_embedded_pid_identity", HERE / "macos_pid_identity.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def micro_identity(value):
    # Reparenting is expected after the source exits. It does not create a new
    # process; PID, uid, executable and microsecond start token identify it.
    return {key: value[key] for key in ("pid", "uid", "executable", "startSeconds", "startMicroseconds")}


def verified_embedded_binding(app, parent_pid):
    raw = app.get("embeddedAXBinding")
    if raw is None:
        return None
    fields = {"schemaVersion", "initialRelationsVerified", "rootPath", "parentChildCount", "observedEpochMs",
              "application", "embedded"}
    require(isinstance(raw, dict) and set(raw) == fields and type(raw["schemaVersion"]) is int and
            raw["schemaVersion"] == 1 and raw["initialRelationsVerified"] is True, "SourceEmbeddedBindingInvalid")
    path, observed, count = raw["rootPath"], raw["observedEpochMs"], raw["parentChildCount"]
    require(isinstance(path, list) and 2 <= len(path) <= 42 and type(path[0]) is int and 0 <= path[0] < 8 and
            all(type(index) is int and 0 <= index < 1000 for index in path) and type(count) is int and
            path[-1] < count <= 1000 and type(observed) is int and 0 <= observed <= 2**53-1,
            "SourceEmbeddedBindingInvalid")
    module = pid_module()
    try:
        identities = {}
        for key in ("application", "embedded"):
            value = raw[key]
            require(isinstance(value, dict) and set(value) == {"pid", "ppid", "uid", "executable", "startSeconds", "startMicroseconds"},
                    "SourceEmbeddedBindingInvalid")
            identities[key] = module.identity(value, value["pid"])
    except (AssertionError, KeyError, TypeError):
        raise FixtureFailure("SourceEmbeddedBindingInvalid") from None
    application, embedded = identities["application"], identities["embedded"]
    require(application["pid"] == parent_pid and application["executable"] == str(Path(app["exe"]).resolve()) and
            embedded["pid"] != parent_pid and application["uid"] == embedded["uid"] and
            all(item["startSeconds"] * 1000000 + item["startMicroseconds"] <= observed * 1000 + 999
                for item in (application, embedded)), "SourceEmbeddedBindingIdentityMismatch")
    return dict(raw, **identities)


def observe_embedded_process(rid, pid):
    """Guarded child only: exact-PID read; absence must be independently clear."""
    hosted(rid)
    require(rid.startswith("osx-") and type(pid) is int and 0 < pid <= 2**31-1, "SourceEmbeddedObservationInputInvalid")
    module = pid_module()
    try:
        first = module.identity(module.sample(pid), pid)
        second = module.identity(module.sample(pid), pid)
        require(micro_identity(first) == micro_identity(second), "SourceEmbeddedObservationUncertain")
        return {"code": "ObservedStable", "identity": second}
    except module.IdentityFailure as error:
        require(str(error) == "ProcessUnavailable", "SourceEmbeddedObservationUncertain")
        # libproc uses the same failure for missing and unreadable processes.
        # Only two successful exact-PID absence queries establish exit. Never
        # use signal 0, enumerate by name, or interpret denied reads as absence.
        for _ in range(2):
            result = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "pid="],
                                    capture_output=True, timeout=0.5)
            require(result.returncode == 1 and not result.stdout.strip() and not result.stderr.strip(),
                    "SourceEmbeddedObservationUncertain")
        return {"code": "ProcessAbsent", "identity": None}


def capture_embedded_process(app, pid, timeout):
    hosted(app["rid"])
    require(app["rid"].startswith("osx-") and type(pid) is int and 0 < pid <= 2**31-1 and
            type(timeout) in (int, float) and math.isfinite(timeout) and 0 < timeout <= 2,
            "SourceEmbeddedObservationInputInvalid")
    try:
        child = subprocess.run([sys.executable, "-B", str(Path(__file__).resolve()), "--observe-embedded-exit",
                                app["rid"], str(pid)], capture_output=True, timeout=timeout)
        require(child.returncode == 0 and len(child.stdout) <= 8192 and not child.stderr.strip(),
                "SourceEmbeddedObservationUncertain")
        raw = json.loads(child.stdout)
        require(isinstance(raw, dict) and set(raw) == {"code", "identity"}, "SourceEmbeddedObservationUncertain")
        if raw["code"] == "ProcessAbsent":
            require(raw["identity"] is None, "SourceEmbeddedObservationUncertain")
        else:
            require(raw["code"] == "ObservedStable", "SourceEmbeddedObservationUncertain")
            raw["identity"] = pid_module().identity(raw["identity"], pid)
        return raw
    except (subprocess.TimeoutExpired, ValueError, TypeError, AssertionError, OSError):
        raise FixtureFailure("SourceEmbeddedObservationUncertain") from None


def stable_status(status):
    require(isinstance(status, dict) and isinstance(status.get("identity"), dict) and isinstance(status.get("peers"), list),
            "SourceStatusInvalid")
    identity = status["identity"]
    require(all(isinstance(identity.get(key), str) and identity[key] for key in ("nodeId", "libraryEpoch")), "SourceNodeIdentityMissing")
    require(type(status.get("sharingEnabled")) is bool and type(status.get("browsingEnabled")) is bool and
            len(status["peers"]) <= 10, "SourceStatusInvalid")
    peers = []
    for peer in status["peers"]:
        require(isinstance(peer, dict) and isinstance(peer.get("nodeId"), str) and peer["nodeId"] and
                type(peer.get("enabled")) is bool and isinstance(peer.get("pathMappings"), list), "SourcePeerInvalid")
        for key in ("outboundGrant", "inboundGrant"):
            grant = peer.get(key)
            require(grant is None or (isinstance(grant, dict) and set(grant) == {"grantId", "revision"} and
                isinstance(grant["grantId"], str) and grant["grantId"] and type(grant["revision"]) is int and grant["revision"] > 0),
                "SourceGrantSummaryInvalid")
        peers.append({key: peer.get(key) for key in ("nodeId", "address", "enabled", "outboundGrant", "inboundGrant", "pathMappings")})
    require(len({peer["nodeId"] for peer in peers}) == len(peers), "SourceDuplicatePeers")
    return {"identity": {key: identity[key] for key in ("nodeId", "libraryEpoch")},
            "sharingEnabled": status["sharingEnabled"], "browsingEnabled": status["browsingEnabled"],
            "peers": sorted(peers, key=lambda peer: peer["nodeId"])}


def introduction(resource):
    prop = resource.get("properties", {}).get("2", {}).get("12", {})
    values = prop.get("values", [])
    require(isinstance(values, list), "SourceDescriptionMissing")
    manual = [value.get("bizValue") for value in values if value.get("scope") == 0]
    require(manual == [INTRODUCTION], "SourceDescriptionChanged")
    return INTRODUCTION


def grant_digest(data):
    # This reads our private synthetic state only. Never persist its keys/codes.
    path = beneath(Path(data) / "federation/state.json", data)
    require(path.stat().st_size <= 1024 * 1024, "SourceStateBudgetExceeded")
    state = json.loads(path.read_text(encoding="utf-8"))
    keys = ("nodeId", "libraryEpoch", "sharingEnabled", "browsingEnabled", "peers", "inboundGrants", "outboundGrants")
    require(all(key in state for key in keys), "SourceStateIncomplete")
    return hashlib.sha256(canonical({key: state[key] for key in keys})).hexdigest()


class SourceFixture:
    @classmethod
    def create(cls, parent, rid, published_directory, web_root, candidate_provenance, results, *, expected_web_inventory):
        hosted(rid)
        verify_candidate(candidate_provenance, rid)
        external = external_preflight(rid)  # Includes exact macOS bundle caches, before any fixture writes.
        parent = unlinked(parent)
        temporary = unlinked(Path(os.environ["RUNNER_TEMP"]))
        require(parent == temporary or parent.is_relative_to(temporary), "SourceParentOutsideRunnerTemp")
        published_directory = beneath(published_directory, temporary)
        web_root = unlinked(web_root)
        published = inventory(published_directory)
        web = inventory(web_root)
        require("index.html" in web and web == expected_web_inventory, "SourceWebPayloadNotAudited")
        expected_name = NAME + (".exe" if rid == "win-x64" else "")
        require(all(name in published for name in (expected_name, NAME + ".dll", "Bakabase.Shell.dll", "Bakabase.Service.dll")),
                "SourcePublishedBinaryMissing")
        results = beneath(results, temporary, exists=False)
        require(not results.exists(), "SourceResultsAlreadyExist")
        token = uuid.uuid4().hex
        root = parent / ("native-gui-source-" + token)
        require(not root.exists(), "SourceFixtureAlreadyExists")
        results.mkdir()
        root.mkdir()
        fixture = cls()
        fixture.root, fixture.results, fixture.rid = root, results, rid
        fixture.process, fixture.identity, fixture.last_stopped = None, None, None
        fixture.readers, fixture.log_bytes, fixture.log_errors = [], {}, set()
        fixture.spawn_count, fixture.api_calls = 0, 0
        fixture.renderer_exit_proven = True
        fixture.closed = False
        fixture.external = external
        fixture.report = {"scope": SCOPE, "passed": False, "starts": [], "stops": [], "fixtureApi": [],
                          "cleanup": {"passed": False}, "nativeMainFlowPassed": False, "externalBundlePreflight": external}
        fixture.app = None
        try:
            with socket.socket() as reservation:
                reservation.bind(("127.0.0.1", 0))
                port = reservation.getsockname()[1]
            (root / "source-owner.json").write_bytes(canonical({"scope": SCOPE, "token": token, "port": port}))
            data = root / "data"
            data.mkdir()
            (data / "app.json").write_bytes(canonical({"App": {"language": "en-US", "enableAnonymousDataTracking": False,
                "enablePreReleaseChannel": False, "listeningPorts": [port], "autoListeningPortCount": 0, "maxParallelism": 1}}))
            runtime = root / "runtime"
            if rid.startswith("osx-"):
                bundle = runtime / (NAME + ".app")
                binary = bundle / "Contents/MacOS"
                binary.parent.mkdir(parents=True)
                with (binary.parent / "Info.plist").open("xb") as stream:
                    plistlib.dump({"CFBundleName": "Bakabase native GUI source fixture", "CFBundleDisplayName": "Bakabase native GUI source fixture",
                        "CFBundleIdentifier": BUNDLE, "CFBundleExecutable": NAME, "CFBundlePackageType": "APPL",
                        "CFBundleVersion": "1.0.0", "CFBundleShortVersionString": "1.0.0", "NSHighResolutionCapable": True}, stream)
            else:
                binary = runtime
            shutil.copytree(published_directory, binary)
            require(inventory(binary) == published, "SourceRuntimeCopyChanged")
            exe = binary / expected_name
            source = {"schemaVersion": 1, "scope": SCOPE, "rid": rid, "executable": str(exe), "executableSHA256": digest(exe),
                "sourceHeadSHA": candidate_provenance["testSourceSHA"], "candidatePackageSourceSHA": candidate_provenance["packageSourceSHA"],
                "candidatePackageRunId": candidate_provenance["runID"], "candidatePackageVersion": candidate_provenance["version"],
                "sourceRuntimeFromExecutionHead": True, "sourceIsInstalledCandidate": False,
                "webRoot": str(web_root), "webInventorySHA256": hashlib.sha256(canonical(web)).hexdigest(),
                "publishedInventorySHA256": hashlib.sha256(canonical(published)).hexdigest(),
                "privateInstanceId": NAME + "." + token, "dataDirectory": str(data),
                "criticalBinarySHA256": {name: published[name]["sha256"] for name in (NAME + ".dll", "Bakabase.Shell.dll", "Bakabase.Service.dll")}}
            (root / "source-provenance.json").write_bytes(canonical(source))
            fixture.app = {"role": "source-fixture", "rid": rid, "exe": exe, "data": data, "port": port,
                "results": results, "fixtureRoot": root, "sourceProvenance": source}
            fixture.arguments = [str(exe), "--data-directory", str(data), "--web-root", str(web_root),
                "--port", str(port), "--run-token", token, "--rid", rid]
            fixture.environment = {key: value for key, value in os.environ.items() if key.upper() not in {
                "BAKABASE_DATA_DIR", "BAKABASE_CLIENT_DATA_DIR", "DOTNET_STARTUP_HOOKS", "BAKABASE_UPGRADE_TEST_ROOT", DATA_ENV}}
            fixture.environment.update({DATA_ENV: str(data), "Analytics__Sentry__BackendDsn": "", "Analytics__Sentry__ClientDsn": ""})
            fixture.report["provenance"] = source
            fixture.report["webFiles"] = web
            fixture.save()
            return fixture
        except BaseException as error:
            fixture.report["error"] = {"type": type(error).__name__, "code": "SourceFixturePreparationFailed"}
            fixture.report["cleanup"] = {"passed": False, "retainedOwnedRoot": str(root), "nativeProcessStarted": False}
            fixture.save()
            raise

    def save(self):
        (self.results / "source-report.json").write_bytes(canonical(self.report) + b"\n")

    def _guard(self):
        require(not self.closed, "SourceFixtureAlreadyClosed")
        return validate_probe_app(self.app)

    def _api(self, method, path, payload=None, *, envelope=True, deadline=None):
        deadline = limited_deadline(deadline, 3)
        self._guard()
        require(method in ("GET", "POST", "PUT") and path.startswith("/") and not path.startswith("//") and
                not any(ord(c) < 32 for c in path) and "#" not in path, "SourceApiRequestInvalid")
        # Every fixture write is explicitly allowlisted; no federation mutations.
        if method != "GET":
            require((method == "POST" and path == "/resource/placeholder") or
                    (re.fullmatch(r"/resource/[1-9][0-9]*/materialize", path) and method == "POST") or
                    (re.fullmatch(r"/resource/[1-9][0-9]*/property-value", path) and method == "PUT"), "SourceUiActionCannotUseApi")
        self.api_calls += 1
        require(self.api_calls <= 100, "SourceApiCallBudgetExceeded")
        body = None if payload is None else canonical(payload)
        require(body is None or len(body) <= 65536, "SourceApiBodyBudgetExceeded")
        connection = http.client.HTTPConnection("127.0.0.1", self.app["port"], timeout=remaining(deadline, 3))
        try:
            connection.request(method, path, body=body, headers={"Content-Type": "application/json"})
            active_socket = connection.sock
            require(active_socket is not None, "SourceApiSocketUnavailable")
            active_socket.settimeout(remaining(deadline, 3))
            response = connection.getresponse()
            raw = bytearray()
            while True:
                active_socket.settimeout(remaining(deadline, 3))
                block = response.read1(min(65536, MAX_RESPONSE + 1 - len(raw)))
                if not block:
                    break
                raw.extend(block)
                require(len(raw) <= MAX_RESPONSE, "SourceApiResponseInvalid")
                if response.isclosed():
                    break  # Content-Length/EOF may already have closed the socket.
            remaining(deadline, 3)
            require(response.status == 200 and len(raw) <= MAX_RESPONSE, "SourceApiResponseInvalid")
            value = json.loads(raw)
            if envelope:
                require(isinstance(value, dict) and value.get("code") == 0, "SourceApiEnvelopeFailed")
                value = value.get("data")
            self.report["fixtureApi"].append({"method": method, "path": path.split("?")[0], "status": response.status})
            return value
        finally:
            connection.close()

    def status(self, *, deadline=None):
        return self._api("GET", "/federation/local/peers", envelope=False, deadline=deadline)

    def _drain(self, stream, label):
        count, exceptions, pending = 0, set(), b""
        try:
            while True:
                block = stream.read(4096)
                if not block:
                    break
                count += len(block)
                # Never retain raw lines: they can contain approval request bodies.
                pending = (pending + block)[-8192:]
                if len(exceptions) < 20:
                    exceptions.update(re.findall(rb"\b((?:[A-Za-z_]\w{0,80}\.){1,10}[A-Za-z_]\w{0,80}Exception)(?=[:\s])", pending)[:20-len(exceptions)])
                if count > 16 * 1024 * 1024:
                    self.log_errors.add("SourceOutputBudgetExceeded")
                pending = pending[-256:]
        except (OSError, ValueError):
            self.log_errors.add("SourceOutputReadFailed")
        finally:
            self.log_bytes[label] = count
            self.report.setdefault("exceptionTypes", []).extend(sorted(item.decode("ascii") for item in exceptions)[:20])

    def _verify_live(self, *, check_output=True, deadline=None):
        require(self.process is not None and self.process.poll() is None, "SourceProcessExited")
        if check_output:
            require(not self.log_errors, "SourceOutputObservationFailed")
        actual = _native_identity(self.process.pid, self.rid, timeout=remaining(deadline, 5))
        require(actual == self.identity, "SourceProcessIdentityChanged")

    def start(self, *, deadline=None):
        deadline = limited_deadline(deadline, 90)
        self._guard()
        require(self.process is None and self.spawn_count < 2, "SourceStartBudgetOrStateInvalid")
        require(self.renderer_exit_proven, "SourcePreviousEmbeddedExitNotProven")
        self.app.pop("embeddedAXBinding", None)
        self.app.pop("_embeddedAXCandidate", None)
        self.report["passed"] = False
        self.spawn_count += 1
        ready = self.app["data"] / "native-source-ready.json"
        if ready.exists():
            require(not ready.is_symlink(), "SourceReadyPathChanged")
            ready.unlink()
        self.renderer_exit_proven = False
        remaining(deadline, 90)
        self.process = subprocess.Popen(self.arguments, cwd=self.root, env=self.environment,
            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            start_new_session=self.rid.startswith("osx-"))
        for name, stream in (("stdout", self.process.stdout), ("stderr", self.process.stderr)):
            reader = threading.Thread(target=self._drain, args=(stream, str(self.spawn_count) + "-" + name), daemon=True)
            self.readers.append(reader)
            reader.start()
        try:
            self.identity = _native_identity(self.process.pid, self.rid, timeout=remaining(deadline, 5))
            require(self.identity is not None and Path(self.identity["executable"]).resolve() == self.app["exe"].resolve(),
                    "SourceLaunchIdentityInvalid")
            while time.monotonic() < deadline:
                self._verify_live(deadline=deadline)
                if ready.exists():
                    require(not ready.is_symlink() and ready.stat().st_size <= 16384, "SourceReadyMarkerInvalid")
                    marker = json.loads(ready.read_text(encoding="utf-8"))
                    require(marker.get("pid") == self.process.pid and marker.get("Port") == self.app["port"] and
                            marker.get("dataDirectory") == str(self.app["data"]) and
                            marker.get("privateInstanceId") == self.app["sourceProvenance"]["privateInstanceId"],
                            "SourceReadyIdentityMismatch")
                    info = self._api("GET", "/app/info", deadline=deadline)
                    require(Path(info.get("appDataPath", "")).resolve() == self.app["data"], "SourceDataDirectoryChanged")
                    current = stable_status(self.status(deadline=deadline))
                    require(current["identity"] == {"nodeId": marker.get("NodeId"), "libraryEpoch": marker.get("LibraryEpoch")},
                            "SourceApiIdentityMismatch")
                    if self.last_stopped is None:
                        require(current["sharingEnabled"] is False and current["browsingEnabled"] is False and not current["peers"],
                                "SourceFixtureChangedDefaultSharing")
                    else:
                        require(self.identity["pid"] != self.last_stopped["process"]["pid"] and current == self.last_stopped["status"] and
                                grant_digest(self.app["data"]) == self.last_stopped["grantSHA256"], "SourceRestartLostIdentityOrGrant")
                    result = {"process": dict(self.identity), "port": self.app["port"], "dataDirectory": str(self.app["data"]),
                        "status": current, "coreVersion": info.get("coreVersion"), "restart": self.last_stopped is not None,
                        "identityAndGrantsRetained": self.last_stopped is not None, "passed": True}
                    remaining(deadline, 90)
                    self.report["starts"].append(result)
                    self.report["passed"] = True
                    self.save()
                    return dict(result, app=self.app, pid=self.process.pid)
                time.sleep(remaining(deadline, 0.2))
            raise FixtureFailure("SourceStartupTimedOut")
        except BaseException as error:
            self.report["passed"] = False
            self.report["error"] = {"type": type(error).__name__, "code": str(error) if isinstance(error, FixtureFailure) else "SourceStartupFailed"}
            self.save()
            raise

    def seed(self, *, deadline=None):
        remaining(deadline, 3)
        self._guard()
        self._verify_live(deadline=deadline)
        require(self.spawn_count == 1 and "seed" not in self.report, "SourceSeedCannotRepeat")
        self.report["passed"] = False
        self.report["seed"] = {"passed": False, "scope": "production-api-resource-fixture-only"}
        self.save()
        created = self._api("POST", "/resource/placeholder", {"items": [{"title": title} for title in TITLES], "acquireImmediately": False}, deadline=deadline)
        require(isinstance(created, list) and len(created) == 3 and all(isinstance(item, dict) and
                item.get("created") is True and not item.get("error") and type(item.get("resourceId")) is int and item["resourceId"] > 0
                for item in created), "SourceResourcesNotCreated")
        ids = [item["resourceId"] for item in created]
        require(len(set(ids)) == 3, "SourceResourcesNotUnique")
        remaining(deadline, 3)
        media = self.root / "media"
        media.mkdir()
        path = media / TITLES[0]
        path.write_bytes(MEDIA)
        materialized = self._api("POST", f"/resource/{ids[0]}/materialize", {"path": str(path), "mergeIfOccupied": False}, deadline=deadline)
        require(materialized.get("materialized") is True and materialized.get("merged") is False and
                materialized.get("path") == str(path), "SourceMediaMaterializationFailed")
        self._api("PUT", f"/resource/{ids[0]}/property-value", {"propertyId": 12, "isCustomProperty": False,
                  "value": INTRODUCTION, "isBizValue": False}, deadline=deadline)
        resources = self._api("GET", "/resource/ids?" + "&".join("ids=" + str(i) for i in ids) + "&additionalItems=288", deadline=deadline)
        require(isinstance(resources, list) and len(resources) == 3 and {r.get("id") for r in resources} == set(ids), "SourceSeedReadbackFailed")
        by_id = {r["id"]: r for r in resources}
        require([by_id[i].get("displayName") for i in ids] == list(TITLES) and by_id[ids[0]].get("path") == str(path),
                "SourceResourceMeaningChanged")
        introduction(by_id[ids[0]])
        require(all(not by_id[i].get("playedAt") or by_id[i]["playedAt"].startswith("0001-") for i in ids), "SourceFixtureAlreadyPlayed")
        seed = {"passed": True, "scope": "production-api-resource-fixture-only", "resourceIds": ids, "titles": list(TITLES),
                "detailTitle": TITLES[0], "detailIntroduction": INTRODUCTION, "mediaSHA256": digest(path),
                "mediaSizeBytes": len(MEDIA), "baselineResources": [{key: by_id[i].get(key) for key in
                    ("id", "displayName", "path", "isFile", "playedAt")} for i in ids]}
        remaining(deadline, 3)
        self.report["seed"] = seed
        self.report["passed"] = True
        self.save()
        return seed

    @property
    def resource_names(self):
        return TITLES

    def read_only_baseline(self, *, deadline=None):
        """Cross-check data semantics; never substitutes for a native detail view."""
        remaining(deadline, 3)
        self._guard()
        self._verify_live(deadline=deadline)
        seed = self.report.get("seed", {})
        require(seed.get("passed") is True, "SourceBaselineRequiresSeed")
        ids = seed["resourceIds"]
        resources = self._api("GET", "/resource/ids?" + "&".join("ids=" + str(i) for i in ids) + "&additionalItems=288", deadline=deadline)
        require(isinstance(resources, list) and len(resources) == 3 and {r.get("id") for r in resources} == set(ids),
                "SourceBaselineResourcesChanged")
        by_id = {resource["id"]: resource for resource in resources}
        actual = [{key: by_id[i].get(key) for key in ("id", "displayName", "path", "isFile", "playedAt")} for i in ids]
        require(canonical(actual) == canonical(seed["baselineResources"]), "SourceBaselineSemanticsChanged")
        introduction(by_id[ids[0]])
        path = beneath(self.root / "media" / TITLES[0], self.root)
        require(path.stat().st_size == seed["mediaSizeBytes"] and digest(path) == seed["mediaSHA256"], "SourceMediaChanged")
        remaining(deadline, 3)
        return {"passed": True, "resources": actual, "mediaSHA256": seed["mediaSHA256"], "noPlayedAtWrite": True}

    def stop(self, *, retain_state=True, deadline=None):
        deadline = limited_deadline(deadline, 45)
        self._guard()
        require(self.process is not None, "SourceNotStarted")
        def native(pid):
            require(time.monotonic() < deadline - 6, "SourceStopDeadlineExceeded")
            return _native_identity(pid, self.rid, timeout=remaining(deadline, 5))
        self._verify_live(check_output=False, deadline=deadline)
        self.renderer_exit_proven = False
        binding = verified_embedded_binding(self.app, self.process.pid) if self.rid.startswith("osx-") else None
        embedded_exit = {"passed": not self.rid.startswith("osx-"), "applicable": self.rid.startswith("osx-"), "readOnly": True,
                         "signalSent": False, "observations": 0}
        if self.rid.startswith("osx-") and binding is None:
            embedded_exit.update(beforeStopVerified=False, outcome="NoVerifiedEmbeddedBinding")
        if binding is not None:
            observed = pid_module().capture(self.app, {"origin": "owned-child-edge", "actualPid": binding["embedded"]["pid"],
                "expectedPid": self.process.pid, "observedEpochMs": binding["observedEpochMs"]}, remaining(deadline, 2))
            require(observed.get("code") == "ObservedStable" and observed.get("stable") is True and
                    observed.get("identity") == binding["embedded"] and observed.get("ownedIdentity") == binding["application"],
                    "SourceEmbeddedIdentityChangedBeforeStop")
            embedded_exit.update(application=binding["application"], identity=binding["embedded"], beforeStopVerified=True)
        before = {"process": dict(self.identity)}
        if retain_state:
            before.update(status=stable_status(self.status(deadline=deadline)), grantSHA256=grant_digest(self.app["data"]))
        descendants = []
        for pid in _descendant_ids(self.process.pid, self.rid, timeout=remaining(deadline, 5)):
            if binding is not None and pid == binding["embedded"]["pid"]:
                continue  # AX ownership permits observation, never a signal to WebKit.
            identity = native(pid)
            if identity is not None:
                descendants.append(identity)
        still_owned = set(_descendant_ids(self.process.pid, self.rid, timeout=remaining(deadline, 5)))
        for identity in descendants:
            if identity["pid"] not in still_owned:
                require(native(identity["pid"]) is None, "SourceDescendantOwnershipChanged")
        self._verify_live(check_output=False, deadline=deadline)  # The parent did not change while establishing child ownership.
        stopped = {"process": dict(self.identity), "descendants": descendants, "passed": False,
                   "embeddedProcessExit": embedded_exit}
        self.report["stops"].append(stopped)
        self.save()
        # Terminate only the Popen product and exact descendants observed above.
        require(time.monotonic() < deadline - 18, "SourceStopDeadlineExceeded")
        self.process.terminate()
        try:
            self.process.wait(timeout=remaining(deadline, 8))
        except subprocess.TimeoutExpired:
            require(native(self.process.pid) == self.identity, "SourceChangedBeforeForcedStop")
            self.process.kill()
            self.process.wait(timeout=remaining(deadline, 3))
            stopped["forcedSourceStop"] = True
        for identity in reversed(descendants):
            current = native(identity["pid"])
            if current is None:
                continue
            require(current == identity, "SourceDescendantIdentityChanged")
            remaining(deadline, 1)
            os.kill(identity["pid"], signal.SIGTERM)
        child_deadline = min(deadline - 6, time.monotonic() + 5)
        while True:
            living = []
            for identity in descendants:
                current = native(identity["pid"])
                if current is not None:
                    require(current == identity, "SourceDescendantIdentityChanged")
                    living.append(identity["pid"])
            if not living:
                break
            require(time.monotonic() < child_deadline, "SourceDescendantsDidNotExit")
            time.sleep(remaining(deadline, 0.1))
        require(native(self.process.pid) is None, "SourceProcessStillPresent")
        if binding is not None:
            while True:
                # Retain headroom for log-reader joins, within the original 45s.
                budget = deadline - time.monotonic() - len(self.readers) - 1
                require(budget > 0, "SourceEmbeddedProcessDidNotExit")
                current = capture_embedded_process(self.app, binding["embedded"]["pid"], min(2, budget))
                embedded_exit["observations"] += 1
                if current["code"] == "ProcessAbsent":
                    embedded_exit.update(passed=True, outcome="ProcessAbsent")
                    break
                original = binding["embedded"]
                if any(current["identity"][key] != original[key] for key in ("startSeconds", "startMicroseconds")):
                    embedded_exit.update(passed=True, outcome="PidReused", replacementObserved=current["identity"],
                                         replacementUntouched=True)
                    break
                require(micro_identity(current["identity"]) == micro_identity(original), "SourceEmbeddedIdentityChanged")
                require(time.monotonic() < deadline - len(self.readers) - 1, "SourceEmbeddedProcessDidNotExit")
                time.sleep(min(0.2, max(0, deadline - time.monotonic() - len(self.readers) - 1)))
        for reader in self.readers:
            reader.join(timeout=remaining(deadline, 1))
        require(not any(reader.is_alive() for reader in self.readers), "SourceLogReadersDidNotExit")
        self.process.stdout.close()
        self.process.stderr.close()
        stopped.update(returnCode=self.process.returncode, remainingProcesses=[], ownedProcessesStopped=True)
        self.process, self.identity = None, None
        self.renderer_exit_proven = embedded_exit["passed"] is True
        stopped["shellFailureDiagnostics"] = shell_failure_diagnostics(self.app, stopped)
        self.report["outputBytes"] = self.log_bytes
        self.save()
        require(self.renderer_exit_proven, "SourceEmbeddedExitCannotBeProven")
        if retain_state:
            require(grant_digest(self.app["data"]) == before["grantSHA256"], "SourceGrantChangedDuringStop")
        remaining(deadline, 1)
        if retain_state:
            self.last_stopped = dict(before, embeddedProcessExit=embedded_exit)
        stopped["passed"] = True
        self.save()
        return stopped

    def restart(self, *, deadline=None):
        remaining(deadline, 90)
        self._guard()
        require(self.last_stopped is not None and self.process is None and self.spawn_count == 1, "SourceRestartRequiresVerifiedStop")
        return self.start(deadline=deadline)

    def _remove_external_bundle_data(self):
        hosted(self.rid)
        require(self.process is None and not any(reader.is_alive() for reader in self.readers), "SourceMustStopBeforeBundleCleanup")
        require(self.renderer_exit_proven, "SourcePreviousEmbeddedExitNotProven")
        proof = self.external
        require(proof.get("passed") is True, "SourceBundlePreflightMissing")
        cleanup = {"passed": False, "afterOwnedProcessesStopped": True, "paths": []}
        self.report["externalBundleCleanup"] = cleanup
        if not self.rid.startswith("osx-"):
            require(proof == {"passed": True, "applicable": False, "paths": []}, "SourceBundlePreflightChanged")
            cleanup.update(passed=True, applicable=False)
            return cleanup
        home = unlinked(Path.home())
        expected = [{"path": str(path), "kind": kind, "absentBeforeLaunch": True} for path, kind in macos_bundle_paths(home)]
        require(proof.get("home") == str(home) and proof.get("bundleIdentifier") == BUNDLE and proof.get("paths") == expected,
                "SourceBundlePreflightChanged")
        # Validate every exact domain before removing any. A link/type/ownership
        # discrepancy retains the source root and all not-yet-removed domains.
        for item in expected:
            path = beneath(Path(item["path"]), home, exists=False)
            record = dict(item, existedAtCleanup=path.exists(), removed=False)
            if path.exists():
                require(self.spawn_count > 0 and bool(self.report["stops"]) and all(stop.get("passed") is True for stop in self.report["stops"]),
                        "SourceBundleAppearedWithoutVerifiedLaunchAndStop")
                record.update(validate_external_tree(path, item["kind"]))
            cleanup["paths"].append(record)
        self.save()
        for item in cleanup["paths"]:
            path = Path(item["path"])
            if item["existedAtCleanup"]:
                beneath(path, home)
                if item["kind"] == "directory":
                    shutil.rmtree(path)
                else:
                    path.unlink()
                item["removed"] = True
                self.save()
        require(not any(Path(item["path"]).exists() or Path(item["path"]).is_symlink() for item in expected),
                "SourceBundleDomainStillPresent")
        cleanup.update(passed=True, applicable=True)
        return cleanup

    def close(self):
        self._guard()
        try:
            if self.process is not None:
                self.stop(retain_state=False)
            # Walk first: an unexpected link retains the fixture instead of
            # recursively deleting a path we no longer own.
            inventory(self.root)
            require(self.root.name.startswith("native-gui-source-"), "SourceCleanupRootInvalid")
            external = self._remove_external_bundle_data()
            shutil.rmtree(self.root)
            self.closed = True
            self.report["cleanup"] = {"passed": True, "sourceStopped": True, "ownedRootRemoved": not self.root.exists(),
                                      "logReadersStopped": not any(t.is_alive() for t in self.readers),
                                      "externalBundlePathsRemoved": external["passed"]}
        except BaseException as error:
            self.report["passed"] = False
            self.report["cleanup"] = {"passed": False, "retainedOwnedRoot": str(self.root),
                "error": {"type": type(error).__name__, "code": str(error) if isinstance(error, FixtureFailure) else "SourceCleanupFailed"}}
            raise
        finally:
            self.save()


if __name__ == "__main__":
    try:
        require(len(sys.argv) == 4 and sys.argv[1] == "--observe-embedded-exit", "SourceEmbeddedObservationInputInvalid")
        observed = observe_embedded_process(sys.argv[2], int(sys.argv[3]))
    except Exception:
        observed = {"code": "ObservationUncertain", "identity": None}
    print(json.dumps(observed, separators=(",", ":")))
