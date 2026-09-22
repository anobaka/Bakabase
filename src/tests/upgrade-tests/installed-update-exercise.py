#!/usr/bin/env python3
"""Real installed updater checks, downloads and automatic restarts.

Called only by the hosted-runner lifecycle gate. Never directly invokes an
updater, supplies a startup hook, or manually starts an updated application.
"""
import contextlib
import base64
import datetime as dt
import hashlib
import http.client
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request

SPEC = importlib.util.spec_from_file_location("update_acceptance_base", Path(__file__).with_name("run-package-acceptance.py"))
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)
require = base.require


def default_log_paths(rid, home, environment):
    names = ["velopack_Bakabase.log", "velopack_Bakabase.Client.log"]
    if rid.startswith("osx-"):
        # Both possible native/managed destinations are checked before installing.
        parents = [home / "Library/Logs", Path(tempfile.gettempdir())]
    else:
        parents = [Path(environment["LOCALAPPDATA"]) / "velopack"]
    return list(dict.fromkeys(parent / name for parent in parents for name in names))


def update_paths(app):
    assembly = base.contract.PRODUCTS[app["role"]]["assembly"]
    mac = app["rid"].startswith("osx-")
    return {"cache": Path.home() / "Library/Caches/velopack" / assembly / "packages" if mac else app["installRoot"] / "packages",
            "updater": app["exe"].parent / "UpdateMac" if mac else app["installRoot"] / "Update.exe",
            "logs": [path for path in default_log_paths(app["rid"], Path.home(), app["environment"])
                     if path.name == f"velopack_{assembly}.log"]}


def api(app, operation, payload=None, timeout=10):
    prefix = "/client/updater" if app["role"] == "client" else "/updater/app"
    request = urllib.request.Request(f"http://127.0.0.1:{app['port']}{prefix}/{operation}",
        data=None if payload is None else json.dumps(payload).encode(), headers={"Content-Type": "application/json"})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(request, timeout=timeout) as response:
        body = response.read(1024 * 1024)
    result = json.loads(body)
    require(result.get("code") == 0, f"{app['role']} updater {operation} returned an error")
    return result


def validate_check(info, installed, running, channel, available):
    require(isinstance(info, dict), "Updater check did not return version data")
    require(info.get("installedVersion") == installed and info.get("runningVersion") == running,
            "Updater compared the wrong installed or running version")
    require(info.get("channel") == channel and info.get("updateCheckUnavailable") is False,
            "Updater channel is incorrect or installation is unavailable")
    require(info.get("version") == available, "Updater reported an unexpected available version")
    return info


def inventory(directory):
    result = {}
    for path in sorted(directory.rglob("*")):
        name = path.relative_to(directory).as_posix()
        if path.is_symlink():
            target = os.readlink(path)
            result[name] = {"kind": "symlink", "target": target,
                            "sha256": hashlib.sha256(target.encode()).hexdigest()}
        elif path.is_file():
            result[name] = {"kind": "file", "sizeBytes": path.stat().st_size, "sha256": base.sha256(path)}
    return result


def validate_payload(app, prepared, updated):
    manifest = base.read_manifest((app["exe"].parent / "sq.version").read_bytes())
    expected = prepared["newManifest" if updated else "oldManifest"]
    for key in ("id", "mainExe", "rid", "version", "channel"):
        require(manifest.get(key) == expected.get(key), "Installed manifest differs: " + key)
    allowed = set(prepared["vendorGenerated"]["allowedNames"])
    assembly = base.contract.PRODUCTS[app["role"]]["assembly"]
    permitted = {"sq.version", "UpdateMac"} if app["rid"].startswith("osx-") else {
        "sq.version", "Update.exe", "Squirrel.exe", assembly + "_ExecutionStub.exe"}
    require(allowed == permitted, "Generated-file exceptions are not the exact platform metadata allowlist")
    expected_hashes = prepared["newPayloadHashes" if updated else "oldPayloadHashes"]
    actual_hashes = inventory(app["exe"].parent)
    product = lambda values: {name: value for name, value in values.items() if name not in allowed}
    require(product(actual_hashes) == product(expected_hashes), "Installed product payload differs from the prepared package")
    marker_path = app["exe"].parent / prepared.get("markerPath", "updater-acceptance.json")
    if updated:
        require(json.loads(marker_path.read_text()) == prepared["marker"], "Installed update marker differs")
    else:
        require(not marker_path.exists(), "Original install unexpectedly contains the update marker")
    return {"manifest": manifest, "productFileCount": len(product(actual_hashes)),
            "productHashesVerified": True, "marker": prepared["marker"] if updated else None, "passed": True}


def validate_download(app, prepared, deliveries):
    package = prepared["packageChecksums"]["newFull"]
    cache = update_paths(app)["cache"] / package["fileName"]
    require(cache.is_file() and cache.stat().st_size == package["sizeBytes"] and
            base.sha256(cache) == package["sha256"], "Default-cache package does not match the served update")
    matching = [item for item in deliveries if item.get("role") == app["role"] and item.get("kind") == "new" and
                item.get("file") == package["fileName"] and item.get("completed") is True and item.get("bytes") == package["sizeBytes"]]
    require(matching, "No completed full-package HTTP delivery was recorded for this product")
    return {"path": str(cache), "sizeBytes": package["sizeBytes"], "sha256": package["sha256"],
            "completedHttpDeliveries": len(matching), "passed": True}


def read_state(app):
    if app["role"] == "client":
        state = api(app, "state").get("data")
    else:
        helper = base.ROOT / "src/tests/installed-lifecycle-tools/updater-state.mjs"
        result = subprocess.run(["node", str(helper), "--port", str(app["port"])], capture_output=True,
                                text=True, timeout=20, env=app["environment"])
        require(result.returncode == 0, "SignalR updater state helper failed: " + result.stderr[-1000:])
        state = json.loads(result.stdout)
    require(isinstance(state, dict) and type(state.get("status")) is int, "Invalid real updater state")
    return state


def wait_pending(app, history=None):
    deadline = time.monotonic() + 180
    if history is None:
        history = []
    while time.monotonic() < deadline:
        state = read_state(app)
        history.append({"at": time.time(), "state": state})
        require(state["status"] not in (5, 6) and not state.get("error"),
                "Updater failed or became unavailable: " + json.dumps(state)[:2000])
        if state["status"] == 3:
            require(state.get("failedFileCount") == 0 and state.get("downloadedFileCount") == 1 and state.get("totalFileCount") == 1,
                    "PendingRestart did not represent one successful package download")
            return history
        time.sleep(0.5)
    raise TimeoutError("Real updater did not reach PendingRestart (3)")


def identity(record):
    return record["pid"], record["startedUtc"]


class SurvivorWatch:
    """Independent sampling; API errors are recorded immediately, never retried away."""
    def __init__(self, app, observer, expected):
        self.app, self.observer, self.expected = app, observer, expected
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.errors, self.samples, self.maximum_gap, self.previous = [], 0, 0, None

    def _run(self):
        endpoint = "/client/app/info" if self.app["role"] == "client" else "/app/info"
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        while not self.stop.is_set():
            stamp = time.monotonic()
            if self.previous is not None:
                self.maximum_gap = max(self.maximum_gap, stamp - self.previous)
            self.previous = stamp
            try:
                require(not self.observer.errors, "Native process observer failed")
                current = self.observer.current(self.app["exe"])
                require({identity(p) for p in current} == {identity(self.expected)}, "Other product process identity changed")
                with opener.open(f"http://127.0.0.1:{self.app['port']}{endpoint}", timeout=2) as response:
                    payload = json.loads(response.read(65536))
                require(payload.get("code") == 0, "Other product API returned an error")
                key = "dataDirectory" if self.app["role"] == "client" else "appDataPath"
                require(Path(payload["data"][key]).resolve() == self.app["data"].resolve(), "Other product changed data directory")
            except Exception as error:
                self.errors.append({"at": time.time(), "error": f"{type(error).__name__}: {error}"})
            self.samples += 1
            self.stop.wait(0.2)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.stop.set()
        self.thread.join(timeout=5)
        if self.thread.is_alive():
            self.errors.append({"error": "Survivor observer did not stop"})

    def evidence(self):
        return {"process": self.expected, "samples": self.samples, "maximumSampleGapSeconds": self.maximum_gap,
                "outagesOrIdentityChanges": self.errors, "passed": self.samples > 1 and not self.errors}


def log_offsets(app):
    return {str(path): path.stat().st_size if path.exists() else 0 for path in update_paths(app)["logs"]}


def read_update_logs(app, offsets):
    contents = []
    for index, path in enumerate(update_paths(app)["logs"]):
        if not path.exists():
            continue
        offset = offsets.get(str(path), 0)
        require(path.stat().st_size >= offset, "Default updater log was truncated during the operation")
        with path.open("rb") as stream:
            stream.seek(offset)
            data = stream.read(4 * 1024 * 1024 + 1)
        require(len(data) <= 4 * 1024 * 1024, "Updater log evidence exceeds its budget")
        (app["results"] / f"automatic-update-native-{index}.log").write_bytes(data)
        contents.append(data.decode("utf-8", errors="replace"))
    require(contents, "No default native updater log was produced")
    return "\n".join(contents)


def verify_native_chain(log, prepared, old_pid, cache_path, native_records):
    records = []
    for record in native_records:
        pid = record["pid"]
        lines = [line for line in log.splitlines() if re.search(r"\[update:" + str(pid) + r"\]", line)]
        text = "\n".join(lines)
        normalized = text.replace("\\\\", "\\")
        if ("Command: Apply" in text and "Restart: true" in text and f"WaitPid({old_pid})" in text and
                str(cache_path) in normalized and f"Package version {prepared['newManifest']['version']} applied successfully." in text):
            require("--norestart" not in text, "Native updater was instructed to suppress automatic restart")
            records.append({"process": record, "nativeLines": lines})
    require(len(records) == 1, "No uniquely observed native updater proves apply, waitPid and automatic restart")
    return records[0]


def trigger_restart(app):
    try:
        result = api(app, "restart", {}, timeout=15)
        return {"outcome": "http-success", "response": result}
    except urllib.error.HTTPError:
        raise
    except (http.client.RemoteDisconnected, ConnectionResetError, BrokenPipeError, urllib.error.URLError, TimeoutError) as error:
        # Environment.Exit may close the response; all native/file/process checks
        # below are still mandatory. A dropped request by itself is never success.
        return {"outcome": "connection-ended", "type": type(error).__name__, "detail": str(error)[:500]}


def one_process(observer, executable):
    require(not observer.errors, "Native process observer failed: " + "; ".join(observer.errors))
    records = observer.current(executable)
    require(len(records) == 1, f"Expected exactly one installed application process for {executable}; found {len(records)}")
    return records[0]


def perform(app, other, prepared, other_prepared, feed, lifecycle, observer, report, client_baseline, resource_id):
    old = one_process(observer, app["exe"])
    survivor = one_process(observer, other["exe"])
    info = lifecycle.observe_app(app)["appInfo"]
    running = info["version" if app["role"] == "client" else "coreVersion"]
    report.update(role=app["role"], oldProcess=old, originalRunningVersion=running)
    report["beforePayload"] = validate_payload(app, prepared, False)
    other_updated = other["version"] == other_prepared["newManifest"]["version"]
    other_payload = validate_payload(other, other_prepared, other_updated)
    offsets = log_offsets(app)
    with SurvivorWatch(other, observer, survivor) as watch:
        try:
            feed.activate(app["role"])
            report["check"] = validate_check(api(app, "new-version").get("data"), app["version"], running,
                                             prepared["channel"], prepared["newManifest"]["version"])
            report["downloadTrigger"] = api(app, "update", {})
            report["stateHistory"] = []
            wait_pending(app, report["stateHistory"])
            report["defaultCache"] = validate_download(app, prepared, feed.deliveries)
            cache = Path(report["defaultCache"]["path"])
            require(not (update_paths(other)["cache"] / cache.name).exists(), "Target update leaked into the other product's cache")
            trigger = time.time()
            report["triggerEpoch"] = trigger
            report["restartRequest"] = trigger_restart(app)
            deadline = time.monotonic() + 20
            while any(identity(record) == identity(old) for record in observer.current(app["exe"])) and time.monotonic() < deadline:
                time.sleep(0.05)
            require(not any(identity(record) == identity(old) for record in observer.current(app["exe"])),
                    "Old application did not exit before the native updater force-stop window")
            deadline = time.monotonic() + 120
            replacement = None
            while time.monotonic() < deadline:
                matches = [p for p in observer.current(app["exe"]) if identity(p) != identity(old) and
                           p["startedEpoch"] >= math.floor(trigger) and p["firstSeenEpoch"] >= trigger]
                if len(matches) == 1:
                    manifest_path = app["exe"].parent / "sq.version"
                    with contextlib.suppress(OSError, ValueError):
                        manifest = base.read_manifest(manifest_path.read_bytes())
                        if manifest.get("version") == prepared["newManifest"]["version"]:
                            replacement = matches[0]
                            break
                time.sleep(0.1)
            require(replacement is not None, "No new installed process appeared with the target manifest; manual fallback is forbidden")
            report["automaticStartup"] = lifecycle.observe_app(app, startup=True)
            actual = one_process(observer, app["exe"])
            require(identity(actual) == identity(replacement), "Automatic startup process changed unexpectedly")
            report["newProcess"] = actual
            deadline = time.monotonic() + 30
            updater_path = update_paths(app)["updater"]
            while observer.current(updater_path) and time.monotonic() < deadline:
                time.sleep(0.1)
            require(not observer.current(updater_path), "Native updater did not exit without forced termination")
            native = [p for p in observer.seen(updater_path) if p["firstSeenEpoch"] >= trigger]
            report["nativeChain"] = verify_native_chain(read_update_logs(app, offsets), prepared, old["pid"], cache, native)
            report["afterPayload"] = validate_payload(app, prepared, True)
            app["version"], app["packageAudit"] = prepared["newManifest"]["version"], prepared["newAudit"]
            core = report["automaticStartup"]["appInfo"]["version" if app["role"] == "client" else "coreVersion"]
            require(core == running, "Same-code repack changed the running core version")
            report["afterCheck"] = validate_check(api(app, "new-version").get("data"), app["version"], running, prepared["channel"], None)
            app_options = json.loads((app["data"] / "app.json").read_text(encoding="utf-8-sig"))["App"]
            require(app_options.get("version") == running, "Persistent app version changed to the package version")
            require(validate_payload(other, other_prepared, other_updated) == other_payload, "Other installed product changed during update")
            report["coexistence"] = lifecycle.verify_coexistence({app["role"]: app, other["role"]: other},
                app["role"] + "-automatically-updated", client_baseline, resource_id)
            require(identity(one_process(observer, other["exe"])) == identity(survivor), "Other product was restarted during update")
        finally:
            report["survivorContinuity"] = watch.evidence()
    report["survivorContinuity"] = watch.evidence()
    require(report["survivorContinuity"]["passed"], "Other product had an API outage or process identity change")
    report["passed"] = True


def run(apps, report, feed, lifecycle):
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), apps["unified"]["rid"])
    forbidden = {"BAKABASE_DATA_DIR", "BAKABASE_CLIENT_DATA_DIR", "DOTNET_STARTUP_HOOKS", "BAKABASE_UPGRADE_TEST_ROOT"}
    for app in apps.values():
        require(not any(key.upper() in forbidden and value for key, value in app["environment"].items()),
                "Automatic update cannot use data overrides or startup hooks")
    manifest = feed.manifest
    require(manifest.get("passed") is True and set(manifest["roles"]) == {"client", "unified"}, "Update preparation did not pass")
    update_report = report["automaticUpdates"] = {"passed": False, "roles": {}, "sourceSHA": manifest["sourceSHA"],
                                                  "scope": "same-code real updater download, apply and automatic restart"}
    paths = []
    for app in apps.values():
        app["originalVersion"], app["originalPackageAudit"] = app["version"], app["packageAudit"]
        app["updatePaths"] = update_paths(app)
        paths.extend([app["exe"], app["updatePaths"]["updater"]])
    observer = ProcessObserver(paths)
    report["currentStage"] = "automatic-update-observer-initialization"
    try:
        with observer:
            for role in ("unified", "client"):
                report["currentStage"] = "automatic-update-" + role
                target = update_report["roles"][role] = {"passed": False}
                perform(apps[role], apps["client" if role == "unified" else "unified"], manifest["roles"][role],
                        manifest["roles"]["client" if role == "unified" else "unified"], feed, lifecycle, observer,
                        target, report["clientSettingsBaseline"], report["resourceId"])
    finally:
        for app in apps.values():
            for path in (app["exe"], app["updatePaths"]["updater"]):
                app.setdefault("diagnosticPids", []).extend(p["pid"] for p in observer.seen(path))
        update_report["processObserverSamples"] = observer.sample_count
        update_report["processObserverErrors"] = observer.errors
        update_report["processObserverDiagnostics"] = observer.diagnostics()
    require(not observer.errors, "Native process observer failed")
    update_report["passed"] = True


def cleanup(apps, report):
    errors, forced, remaining_records = [], [], []
    for role, app in apps.items():
        paths = update_paths(app)
        remaining = base.native_processes(paths["updater"])
        if remaining:
            forced.append({"role": role, "executable": str(paths["updater"]), "processIds": remaining})
            try:
                base.stop_native(paths["updater"])
            except Exception as error:
                errors.append(str(error))
            remaining = base.native_processes(paths["updater"])
            if remaining:
                remaining_records.append({"role": role, "processIds": remaining, "executable": str(paths["updater"])})
        for index, path in enumerate(paths["logs"]):
            if path.is_file():
                try:
                    with path.open("rb") as stream:
                        stream.seek(max(0, path.stat().st_size - 1024 * 1024))
                        (app["results"] / f"default-velopack-{index}.log").write_bytes(stream.read())
                except Exception as error:
                    errors.append(str(error))
    report["updaterCleanup"] = {"forcedProcesses": forced, "remainingProcesses": remaining_records,
                                "errors": errors, "passed": not forced and not errors}
    require(not forced and not errors, "Native updater required forced cleanup or its logs could not be preserved")


def _path_key(path, windows=None):
    windows = os.name == "nt" if windows is None else windows
    value = os.path.normpath(os.path.abspath(os.fspath(path)))
    return value.casefold() if windows else os.path.realpath(value)


def parse_macos_processes(text, paths, observed_at):
    """Parse LC_ALL=C TZ=UTC ps -ww -axo pid=,lstart=,comm=.

    split(None, 6) preserves spaces in the full executable path. Unrelated rows
    are ignored. A malformed row identifying one of our paths raises an error.
    """
    wanted = {_path_key(path, False): os.fspath(path) for path in paths}
    months = {name: index for index, name in enumerate(
        ("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"), 1)}
    result = []
    for line in text.splitlines():
        fields = line.split(None, 6)
        if len(fields) != 7:
            continue
        pid, weekday, month, day, clock, year, executable = fields
        if not executable.startswith("/") or _path_key(executable, False) not in wanted:
            continue
        if weekday not in ("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"):
            raise ValueError("Malformed ps weekday for owned executable")
        hour, minute, second = (int(value) for value in clock.split(":"))
        started = dt.datetime(int(year), months[month], int(day), hour, minute, second, tzinfo=dt.timezone.utc)
        result.append({"pid": int(pid), "executable": executable,
                       "startedUtc": started.isoformat().replace("+00:00", "Z"),
                       "startedEpoch": started.timestamp(), "firstSeenEpoch": float(observed_at),
                       "startResolutionSeconds": 1.0, "startTimeSource": "ps-lstart-UTC"})
    return result


_WINDOWS_SCRIPT = r"""
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
# Windows PowerShell 5.1 emits a JSON array as one pipeline object. An outer
# @() would wrap it again and compare each process path with an array.
$targets = ConvertFrom-Json -InputObject $env:BAKABASE_OBSERVER_PATHS
if ($null -eq $targets -or $targets.Count -eq 0) { throw 'Observer has no target paths' }
foreach ($target in $targets) {
    if ($target -isnot [string] -or [string]::IsNullOrWhiteSpace($target) -or -not [IO.Path]::IsPathRooted($target)) {
        throw 'Observer target must be an absolute executable path string'
    }
}
[string[]]$targets = $targets
$names = @($targets | ForEach-Object { [IO.Path]::GetFileName($_) } | Select-Object -Unique)
$filter = ($names | ForEach-Object { "Name='" + $_.Replace("'", "''") + "'" }) -join ' OR '
$query = [ordered]@{ targets = @($targets); names = @($names); filter = $filter }
$deadline = [DateTime]::UtcNow.AddMinutes(15)
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        $rows = @(
            Get-CimInstance -ClassName Win32_Process -Filter $filter -OperationTimeoutSec 3 | ForEach-Object {
                $proc = $_
                $matches = $false
                foreach ($target in $targets) {
                    if ([StringComparer]::OrdinalIgnoreCase.Equals($proc.ExecutablePath, $target)) {
                        $matches = $true
                        break
                    }
                }
                if ($matches) {
                    if ($null -eq $proc.CreationDate) { throw 'Matching CIM process has no CreationDate' }
                    $started = $proc.CreationDate.ToUniversalTime()
                    [ordered]@{
                        pid = [int]$proc.ProcessId
                        executable = $proc.ExecutablePath
                        startedUtc = $started.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
                        startedEpoch = ($started.Ticks - 621355968000000000) / 10000000.0
                        startResolutionSeconds = 0.000001
                        startTimeSource = 'Win32_Process.CreationDate'
                    }
                }
            }
        )
        $observed = ([DateTime]::UtcNow.Ticks - 621355968000000000) / 10000000.0
        $snapshot = [ordered]@{ observedAt = $observed; processes = @($rows); query = $query }
        [Console]::WriteLine((ConvertTo-Json -InputObject $snapshot -Depth 5 -Compress))
        [Console]::Out.Flush()
        Start-Sleep -Milliseconds 50
    }
    throw 'Observer reached its 15 minute lifetime deadline'
} catch {
    [Console]::WriteLine((ConvertTo-Json -InputObject @{error = $_.Exception.Message} -Compress))
    [Console]::Out.Flush()
    exit 1
}
"""


def parse_windows_snapshot(line):
    try:
        snapshot = json.loads(line)
    except (ValueError, TypeError) as error:
        raise ValueError("Non-JSON PowerShell observer stdout: " + repr(line[:512])) from error
    require(isinstance(snapshot, dict), "PowerShell observer snapshot is not an object")
    return snapshot


class ProcessObserver:
    """Context-managed, bounded observation of an exact executable allowlist.

    errors are terminal observation failures; never treat a stale current()
    snapshot as healthy when errors is nonempty. sample_count counts successful
    complete snapshots. firstSeenEpoch is immutable per (pid, startedUtc).
    Sampling is 50 ms *plus* OS query runtime, not a guaranteed 50 ms interval.
    """
    def __init__(self, paths: list[Path]):
        if not paths:
            raise ValueError("At least one executable path is required")
        self.paths = [Path(os.path.abspath(os.fspath(path))) for path in paths]
        self._keys = {_path_key(path) for path in self.paths}
        self._current = {key: [] for key in self._keys}
        self._seen = {key: {} for key in self._keys}
        self.errors = []
        self.sample_count = 0
        self._lock = threading.RLock()
        self._stop = threading.Event()
        self._ready = threading.Event()
        self._threads = []
        self._helper = None
        self._entered = False
        self._last_sample = None
        self._started_monotonic = None
        self._stderr_text = ""
        self._stderr_characters = 0
        self._stdout_lines = 0
        self._first_stdout = None
        self._last_stdout = None

    def _error(self, message):
        with self._lock:
            self.errors.append(str(message))
        self._ready.set()
        self._stop.set()

    def _accept(self, rows, observed_at):
        current = {key: [] for key in self._keys}
        with self._lock:
            for raw in rows:
                row = dict(raw)
                key = _path_key(row["executable"])
                if key not in self._keys:
                    raise ValueError("Observer returned an executable outside the allowlist")
                row["pid"] = int(row["pid"])
                row["startedEpoch"] = float(row["startedEpoch"])
                if row["pid"] <= 0 or not row["startedUtc"]:
                    raise ValueError("Observer returned an invalid process identity")
                identity = (row["pid"], row["startedUtc"])
                old = self._seen[key].get(identity)
                row["firstSeenEpoch"] = old["firstSeenEpoch"] if old else float(observed_at)
                self._seen[key][identity] = dict(row)
                current[key].append(row)
            self._current = current
            self.sample_count += 1
            self._last_sample = time.monotonic()
        self._ready.set()

    def _mac_loop(self):
        environment = dict(os.environ, LC_ALL="C", TZ="UTC")
        try:
            while not self._stop.is_set():
                with self._lock:
                    if self._stop.is_set():
                        break
                    process = subprocess.Popen(
                        ["/bin/ps", "-ww", "-axo", "pid=,lstart=,comm="],
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                        encoding="utf-8", errors="replace", env=environment)
                    self._helper = process
                try:
                    output, stderr = process.communicate(timeout=3)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.communicate(timeout=2)
                    raise TimeoutError("ps observer query exceeded 3 seconds")
                finally:
                    with self._lock:
                        if self._helper is process:
                            self._helper = None
                if self._stop.is_set():
                    break
                if process.returncode != 0:
                    raise RuntimeError("ps observer failed: " + stderr[:1000])
                observed_at = time.time()
                self._accept(parse_macos_processes(output, self.paths, observed_at), observed_at)
                self._stop.wait(0.05)
        except Exception as error:
            if not self._stop.is_set():
                self._error(error)

    def _windows_read_loop(self):
        process = self._helper
        try:
            for line in process.stdout:
                if self._stop.is_set():
                    break
                self._remember_windows_stdout(line)
                snapshot = parse_windows_snapshot(line)
                if "error" in snapshot:
                    raise RuntimeError(snapshot["error"])
                self._accept(snapshot["processes"], snapshot["observedAt"])
            if not self._stop.is_set():
                raise RuntimeError("Persistent PowerShell observer exited unexpectedly")
        except Exception as error:
            if not self._stop.is_set():
                self._error(error)

    def _remember_windows_stdout(self, line):
        # Preserve the wire format on both successful and rejected snapshots.
        # First and most recent lines are enough to diagnose filtering/startup.
        entry = {"text": line[:8192], "characters": len(line), "truncated": len(line) > 8192}
        with self._lock:
            self._stdout_lines += 1
            if self._first_stdout is None:
                self._first_stdout = entry
            self._last_stdout = entry

    def _windows_stderr_loop(self):
        # Keep draining after a stdout failure until close() ends the helper.
        # No diagnostic stream content is accepted as a process snapshot.
        try:
            for chunk in iter(lambda: self._helper.stderr.read(1024), ""):
                with self._lock:
                    self._stderr_characters += len(chunk)
                    self._stderr_text += chunk[:max(0, 8192 - len(self._stderr_text))]
        except Exception as error:
            if not self._stop.is_set():
                self._error("PowerShell diagnostic reader failed: " + str(error))

    def diagnostics(self):
        with self._lock:
            current = [dict(row) for rows in self._current.values() for row in rows]
            seen = [dict(row) for rows in self._seen.values() for row in rows.values()]
            return {"stderr": self._stderr_text, "stderrCharacters": self._stderr_characters,
                    "stderrTruncated": self._stderr_characters > len(self._stderr_text),
                    "executablePaths": [str(path) for path in self.paths],
                    "current": current[:64], "seen": seen[:64],
                    "currentCount": len(current), "seenCount": len(seen),
                    "processRecordsTruncated": len(current) > 64 or len(seen) > 64,
                    "windowsStdout": {"lines": self._stdout_lines, "first": self._first_stdout,
                                      "last": self._last_stdout}}

    def _watchdog(self):
        while not self._stop.wait(0.1):
            now = time.monotonic()
            with self._lock:
                last = self._last_sample
            if now - self._started_monotonic > 900:
                self._error("Observer exceeded its 15 minute lifetime deadline")
            elif last is not None and now - last > 8:
                self._error("Observer produced no successful snapshot for 8 seconds")
            elif last is None and now - self._started_monotonic > 15:
                self._error("Observer produced no initial snapshot within 15 seconds")
        # A terminal failure also ends the owned helper, including a stuck CIM
        # call. No observed application/updater PID is ever signalled here.
        self._kill_helper()

    def _kill_helper(self):
        with self._lock:
            process = self._helper
        if process is not None and process.poll() is None:
            try:
                process.kill()
            except ProcessLookupError:
                pass

    def __enter__(self):
        if self._entered:
            raise RuntimeError("A ProcessObserver cannot be entered twice")
        self._entered = True
        self._started_monotonic = time.monotonic()
        try:
            if os.name == "nt":
                encoded = base64.b64encode(_WINDOWS_SCRIPT.encode("utf-16-le")).decode("ascii")
                environment = dict(os.environ, BAKABASE_OBSERVER_PATHS=json.dumps([str(path) for path in self.paths]))
                self._helper = subprocess.Popen(
                    ["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                    encoding="utf-8-sig", errors="replace", bufsize=1, env=environment,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                worker = threading.Thread(target=self._windows_read_loop, daemon=True)
            else:
                if os.uname().sysname != "Darwin":
                    raise RuntimeError("ProcessObserver only supports macOS and Windows")
                worker = threading.Thread(target=self._mac_loop, daemon=True)
            self._threads = [worker, threading.Thread(target=self._watchdog, daemon=True)]
            if os.name == "nt":
                self._threads.append(threading.Thread(target=self._windows_stderr_loop, daemon=True))
            for thread in self._threads:
                thread.start()
            if not self._ready.wait(15):
                self._error("Observer produced no initial snapshot within 15 seconds")
            if self.errors or self.sample_count == 0:
                raise RuntimeError("Process observer initialization failed: " + "; ".join(self.errors))
            return self
        except Exception as error:
            self.close()
            if self._stderr_text:
                raise RuntimeError(str(error) + "; observer stderr: " + repr(self._stderr_text[:2048])) from error
            raise

    def current(self, path):
        with self._lock:
            return [dict(row) for row in self._current[_path_key(path)]]

    def seen(self, path):
        with self._lock:
            return [dict(row) for row in self._seen[_path_key(path)].values()]

    def close(self):
        self._stop.set()
        self._kill_helper()
        for thread in self._threads:
            if thread is not threading.current_thread() and thread.ident is not None:
                thread.join(timeout=5)
                if thread.is_alive():
                    with self._lock:
                        self.errors.append("Observer helper thread failed to stop within 5 seconds")
        with self._lock:
            process = self._helper
        if process is not None:
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=2)
            if process.stdout is not None:
                process.stdout.close()
            if process.stderr is not None:
                process.stderr.close()

    def __exit__(self, exc_type, exc, traceback):
        self.close()
        return False
