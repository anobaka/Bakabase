#!/usr/bin/env python3
"""Bounded, best-effort diagnostics for the two owned installed test products.

No process is stopped except a diagnostic subprocess started here. Only exact
application/updater paths, current-run product crash reports, and log entries for
known product PIDs are collected. Environment and command-line dumps are omitted.
"""
import datetime
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time

FILE_LIMIT = 1024 * 1024
TOTAL_LIMIT = 5 * FILE_LIMIT
DATA_LIMIT = TOTAL_LIMIT - 64 * 1024  # Reserve space for the bounded JSON index.
ROLE_NAMES = {"unified": "Bakabase", "client": "Bakabase.Client"}
PRIVATE_KEYS = {"env", "environ", "environment", "environmentvariables", "processenvironment"}


def _redact_environment(value):
    if isinstance(value, dict):
        return {key: "[environment omitted]" if re.sub(r"[^a-z]", "", key.lower()) in PRIVATE_KEYS
                else _redact_environment(item) for key, item in value.items()}
    if isinstance(value, list):
        return [_redact_environment(item) for item in value]
    return value


def _run(arguments, environment=None, timeout=10):
    # These queries produce only the small allowlisted process fields below.
    result = subprocess.run(arguments, capture_output=True, timeout=timeout, env=environment)
    if len(result.stdout) > FILE_LIMIT:
        raise ValueError("Diagnostic process output exceeds budget")
    if result.returncode:
        raise RuntimeError("Diagnostic process query failed")
    return result.stdout.decode("utf-8", errors="replace")


def _known_pids(app):
    return {pid for key in ("observedProcessIds", "diagnosticPids") for pid in app.get(key, [])
            if type(pid) is int and 0 < pid <= 2**31 - 1}


def _processes(apps, deadline=None):
    def timeout(maximum):
        remaining = maximum if deadline is None else min(maximum, deadline - time.monotonic())
        if remaining <= 0:
            raise TimeoutError("Diagnostic deadline elapsed")
        return remaining

    expected, known = {}, set()
    for role, app in apps.items():
        if role not in ROLE_NAMES:
            continue
        executable = str(Path(app["exe"]).absolute())
        updater = str(Path(app["installRoot"]) / "Update.exe") if os.name == "nt" else str(Path(executable).parent / "UpdateMac")
        expected[executable] = (role, "application")
        expected[updater] = (role, "updater")
        known.update(_known_pids(app))
    if len(known) > 32:
        raise ValueError("Known process inventory exceeds budget")
    result = []
    if os.name == "nt":
        environment = dict(os.environ, BAKABASE_DIAGNOSTIC_PATHS=json.dumps(list(expected)))
        script = ("$paths=ConvertFrom-Json $env:BAKABASE_DIAGNOSTIC_PATHS; "
                  "@(Get-CimInstance Win32_Process -Filter \"Name='Bakabase.exe' OR Name='Bakabase.Client.exe' OR Name='Update.exe'\" | "
                  "Where-Object {$_.ExecutablePath -in $paths} | "
                  "Select-Object ProcessId,ParentProcessId,CreationDate,ExecutablePath) | ConvertTo-Json -Compress")
        rows = json.loads(_run(["powershell", "-NoProfile", "-Command", script], environment, timeout(10)) or "[]")
        for row in rows if isinstance(rows, list) else [rows]:
            path = row.get("ExecutablePath")
            match = next((key for key in expected if os.path.normcase(key) == os.path.normcase(path or "")), None)
            if match is not None:
                role, kind = expected[match]
                result.append({"pid": row["ProcessId"], "parentPid": row["ParentProcessId"],
                               "createdAt": row["CreationDate"], "executable": path, "role": role, "kind": kind})
    else:
        # pgrep is scoped by exact executable path; ps never includes args or env.
        candidates = set(known)
        for executable in expected:
            found = subprocess.run(["pgrep", "-f", "^" + re.escape(executable) + r"( |$)"],
                                   capture_output=True, timeout=timeout(5))
            if found.returncode not in (0, 1) or len(found.stdout) > FILE_LIMIT:
                raise RuntimeError("Owned process lookup failed")
            candidates.update(int(value) for value in found.stdout.split() if value.isdigit())
        if len(candidates) > 32:
            raise ValueError("Owned process candidates exceed budget")
        if candidates:
            process = subprocess.run(["ps", "-p", ",".join(map(str, sorted(candidates))),
                                      "-o", "pid=,ppid=,lstart=,comm="], capture_output=True, timeout=timeout(10))
            if process.returncode not in (0, 1) or len(process.stdout) > FILE_LIMIT:
                raise RuntimeError("Owned process inspection failed")
            for line in process.stdout.decode("utf-8", errors="replace").splitlines():
                values = line.strip().split(None, 7)
                if len(values) == 8 and values[7] in expected:
                    role, kind = expected[values[7]]
                    result.append({"pid": int(values[0]), "parentPid": int(values[1]),
                                   "createdAt": " ".join(values[2:7]), "executable": values[7], "role": role, "kind": kind})
    live = {row["pid"] for row in result}
    return {"current": result, "previouslyObservedPids": sorted(known),
            "previouslyObservedNotAtOwnedPaths": sorted(known - live)}, expected


def _crash_content(path, apps, started_epoch, ended_epoch):
    if path.is_symlink() or not path.is_file() or not started_epoch <= path.stat().st_mtime <= ended_epoch + 2:
        return None
    names = "|".join(re.escape(ROLE_NAMES[role]) for role in apps if role in ROLE_NAMES)
    if not names or not re.fullmatch(r"(?:" + names + r")[-_].+\.(?:ips|crash)", path.name):
        return None
    # Never read a huge report merely to truncate it. Typical native reports are
    # far smaller; record a skip separately when the full file exceeds the cap.
    if path.stat().st_size > FILE_LIMIT:
        return {"skipped": "perFileBudget", "fileName": path.name, "sizeBytes": path.stat().st_size}
    text = path.read_text(encoding="utf-8", errors="replace")
    owned_paths = {str(Path(app["exe"]).absolute()): ROLE_NAMES[role]
                   for role, app in apps.items() if role in ROLE_NAMES}
    if path.suffix == ".ips":
        decoder, records, position = json.JSONDecoder(), [], 0
        while position < len(text):
            while position < len(text) and text[position].isspace():
                position += 1
            if position == len(text):
                break
            value, position = decoder.raw_decode(text, position)
            records.append(value)
        if not any(isinstance(row, dict) and row.get("procPath") in owned_paths and
                   row.get("procName") == owned_paths[row["procPath"]] for row in records):
            return None
        content = "\n".join(json.dumps(_redact_environment(row), ensure_ascii=False) for row in records) + "\n"
    else:
        process = re.search(r"^Process:\s+(Bakabase(?:\.Client)?)\s+\[(\d+)\]", text, re.M)
        executable = re.search(r"^Path:\s+(.+)$", text, re.M)
        if process is None or executable is None or owned_paths.get(executable.group(1).strip()) != process.group(1):
            return None
        # Plain crash reports sometimes include an explicit environment section.
        content = re.sub(r"(?im)^Environment Variables?:[^\n]*\n(?:[^\n]+\n)*", "Environment Variables: [omitted]\n", text)
    data = content.encode("utf-8")
    if len(data) > FILE_LIMIT:
        return {"skipped": "serializedFileBudget", "fileName": path.name, "sizeBytes": len(data)}
    return {"fileName": path.name, "content": data, "sourceMtime": path.stat().st_mtime}


def _log_show(directory, pids, started_epoch, deadline, remaining):
    if not pids or remaining <= 0 or deadline <= time.monotonic():
        return {"skipped": "noKnownProductPidOrBudget"}
    ended = time.time()
    # Predicate combines our observed PID with exact product process names, and
    # the time window is limited to this fixture (at most its latest 30 minutes).
    predicate = "(" + " OR ".join("processID == " + str(pid) for pid in sorted(pids)) + ") AND (process == \"Bakabase\" OR process == \"Bakabase.Client\")"
    local_time = lambda value: datetime.datetime.fromtimestamp(value).strftime("%Y-%m-%d %H:%M:%S")
    arguments = ["/usr/bin/log", "show", "--style", "json", "--start", local_time(max(started_epoch, ended - 1800)),
                 "--end", local_time(ended), "--predicate", predicate]
    target = directory / "owned-process-system.log"
    limit = min(FILE_LIMIT, remaining)
    stopped = False
    with target.open("xb") as stream:
        child = subprocess.Popen(arguments, stdout=stream, stderr=subprocess.DEVNULL, start_new_session=True)
        end = min(deadline, time.monotonic() + 10)
        try:
            while child.poll() is None:
                if time.monotonic() >= end or target.stat().st_size >= limit:
                    stopped = True
                    break
                time.sleep(0.05)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait(timeout=2)
    if target.stat().st_size > limit:
        with target.open("r+b") as stream:
            stream.truncate(limit)
    return {"file": str(target), "sizeBytes": target.stat().st_size, "exitCode": child.returncode,
            "truncatedOrTimedOut": stopped, "pids": sorted(pids), "maximumCommandSeconds": 10}


def capture(apps, results, started_epoch):
    """Return evidence or collection errors; never obscure the original failure."""
    report = {"scope": "Known owned product processes and current-run native crashes only", "errors": [], "crashReports": []}
    deadline = time.monotonic() + 35
    try:
        if not isinstance(started_epoch, (int, float)) or not 0 < started_epoch <= time.time():
            raise ValueError("Invalid fixture start time")
        directory = Path(results) / "diagnostics"
        directory.mkdir(exist_ok=False)
        try:
            processes, _ = _processes(apps, deadline)
            report["processes"] = processes
        except Exception as error:
            report["errors"].append({"stage": "processes", "type": type(error).__name__})
        if os.name != "nt" and any(str(app.get("rid", "")).startswith("osx-") for app in apps.values()):
            consumed, ended = 0, time.time()
            for location in (Path.home() / "Library/Logs/DiagnosticReports", Path("/Library/Logs/DiagnosticReports")):
                if not location.is_dir():
                    continue
                try:
                    # Glob exact product prefixes before reading any report data.
                    candidates = sorted({path for role in apps if role in ROLE_NAMES
                        for separator in ("-", "_") for path in location.glob(ROLE_NAMES[role] + separator + "*")})
                    for path in candidates:
                        if time.monotonic() >= deadline or consumed >= DATA_LIMIT or len(report["crashReports"]) >= 64:
                            report["errors"].append({"stage": "crashReports", "type": "BudgetExceeded"})
                            break
                        try:
                            item = _crash_content(path, apps, started_epoch, ended)
                            if item is None:
                                continue
                            data = item.pop("content", None)
                            if data is not None:
                                if consumed + len(data) > DATA_LIMIT:
                                    item["skipped"] = "totalFileBudget"
                                else:
                                    destination = directory / (str(len(report["crashReports"])) + "-" + path.name)
                                    destination.write_bytes(data)
                                    consumed += len(data)
                                    item.update(file=str(destination), sizeBytes=len(data))
                            report["crashReports"].append(item)
                        except Exception as error:
                            report["errors"].append({"stage": "crashReport", "type": type(error).__name__})
                except Exception as error:
                    report["errors"].append({"stage": "crashInventory", "type": type(error).__name__})
            pids = {row["pid"] for row in report.get("processes", {}).get("current", []) if row["kind"] == "application"}
            pids.update(pid for app in apps.values() for pid in _known_pids(app))
            try:
                if len(pids) <= 32:
                    report["systemLog"] = _log_show(directory, pids, started_epoch, deadline, DATA_LIMIT - consumed)
            except Exception as error:
                report["errors"].append({"stage": "systemLog", "type": type(error).__name__})
        (directory / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        report["reportPath"] = str(directory / "report.json")
    except BaseException as error:
        report["errors"].append({"stage": "capture", "type": type(error).__name__})
    return report
