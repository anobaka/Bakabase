#!/usr/bin/env python3
"""Bounded failure diagnostics for one caller-verified SecurityAgent process.

Never authenticate, change logging policy, inspect other PIDs, or write files.
The caller must supply the process identity observed before its one credential
submission. Returned labels are diagnostics, never proof of accepted credentials.
"""
import datetime as dt
import json
import math
import os
from pathlib import PurePosixPath
import platform
import re
import selectors
import subprocess
import time

MAX_BYTES = 256 * 1024
MAX_SECONDS = 5
MAX_WINDOW_SECONDS = 180
MAX_ENTRIES = 80
SECURITY_AGENTS = {
    "/System/Library/CoreServices/SecurityAgent.app/Contents/MacOS/SecurityAgent",
    "/System/Library/Frameworks/Security.framework/Versions/A/MachServices/SecurityAgent.bundle/Contents/MacOS/SecurityAgent",
}
TERMS = ("auth", "credential", "password", "shell", "pam", "denied", "result")
FAILURE_CODES = {
    "HostedRunnerRequired", "NativeMacRequired", "InvalidProcessIdentity", "InvalidObservationWindow",
    "InvalidRedactionSecret", "EmptyObservationWindow", "DiagnosticTimedOut", "OutputBudgetExceeded",
    "LogCommandFailed", "LogChildCleanupIncomplete", "InvalidLogJson", "DiagnosticUnavailable",
}


class DiagnosticFailure(Exception):
    def __init__(self, code):
        super().__init__(code)
        self.code = code


def require(value, code):
    if not value:
        raise DiagnosticFailure(code)


def require_hosted():
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and
            bool(os.environ.get("RUNNER_TEMP")), "HostedRunnerRequired")
    require(platform.system() == "Darwin" and platform.machine() in {"arm64", "x86_64"}, "NativeMacRequired")


def epoch(value):
    return type(value) in (int, float) and math.isfinite(value) and value > 0


def validate(record, started_epoch, secret, now):
    require(type(record) is dict and type(record.get("pid")) is int and 1 < record["pid"] <= 2 ** 31 - 1,
            "InvalidProcessIdentity")
    require(record.get("path") in SECURITY_AGENTS and isinstance(record.get("started"), str) and
            re.fullmatch(r"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2}\s+\d{2}:\d{2}:\d{2}\s+\d{4}",
                         record["started"]), "InvalidProcessIdentity")
    observed = record.get("firstObservedEpoch")
    require(epoch(started_epoch) and epoch(observed) and epoch(now) and started_epoch <= observed <= now,
            "InvalidObservationWindow")
    require(type(secret) is str and re.fullmatch(r"[A-Za-z0-9_-]{43}", secret), "InvalidRedactionSecret")
    # Round INWARD: the actual log query never starts before the fixture or the
    # caller's verified observation, even though log's arguments use seconds.
    start = math.ceil(max(started_epoch, observed, now - MAX_WINDOW_SECONDS))
    end = math.floor(now)
    require(start < end, "EmptyObservationWindow")
    return start, end


def read_log(arguments, deadline):
    """Read at most MAX_BYTES, reserving part of the same deadline for cleanup."""
    child, selector = None, None
    data = bytearray()
    try:
        require(time.monotonic() < deadline - 0.25, "DiagnosticTimedOut")
        child = subprocess.Popen(arguments, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                 stderr=subprocess.DEVNULL, close_fds=True)
        os.set_blocking(child.stdout.fileno(), False)
        selector = selectors.DefaultSelector()
        selector.register(child.stdout, selectors.EVENT_READ)
        eof = False
        while not eof:
            remaining = deadline - 0.25 - time.monotonic()
            require(remaining > 0, "DiagnosticTimedOut")
            if not selector.select(timeout=min(0.05, remaining)):
                continue
            try:
                chunk = os.read(child.stdout.fileno(), min(8192, MAX_BYTES - len(data)))
            except BlockingIOError:
                continue
            if not chunk:
                eof = True
            else:
                data.extend(chunk)
                require(len(data) < MAX_BYTES, "OutputBudgetExceeded")
        remaining = deadline - 0.25 - time.monotonic()
        require(remaining > 0, "DiagnosticTimedOut")
        try:
            child.wait(timeout=remaining)
        except subprocess.TimeoutExpired:
            raise DiagnosticFailure("DiagnosticTimedOut") from None
        require(child.returncode == 0, "LogCommandFailed")
        return bytes(data)
    finally:
        try:
            if child is not None:
                try:
                    if child.poll() is None:
                        child.kill()  # Only the exact log child created above.
                    remaining = max(0, deadline - time.monotonic())
                    try:
                        child.wait(timeout=remaining)
                    except subprocess.TimeoutExpired:
                        raise DiagnosticFailure("LogChildCleanupIncomplete") from None
                finally:
                    child.stdout.close()
        finally:
            if selector is not None:
                selector.close()


def redact(value, secret, maximum):
    # Replace BEFORE truncation, including when a secret crosses the boundary.
    return value.replace(secret, "[redacted]")[:maximum]


def entries_from_json(data, record, start, end, secret):
    try:
        rows = json.loads(data)
    except (ValueError, UnicodeError):
        raise DiagnosticFailure("InvalidLogJson") from None
    require(isinstance(rows, list), "InvalidLogJson")
    entries, rejected, matched = [], 0, 0
    for row in rows:
        if not isinstance(row, dict):
            rejected += 1
            continue
        stamp, message, path = row.get("timestamp"), row.get("eventMessage"), row.get("processImagePath")
        if not (type(row.get("processID")) is int and row["processID"] == record["pid"] and
                isinstance(path, str) and path == record["path"] and PurePosixPath(path).name == "SecurityAgent" and
                row.get("eventType") == "logEvent" and isinstance(stamp, str) and len(stamp) <= 80 and
                isinstance(message, str) and any(term in message.casefold() for term in TERMS)):
            rejected += 1
            continue
        try:
            timestamp = dt.datetime.fromisoformat(stamp.replace("Z", "+00:00"))
            valid_time = timestamp.tzinfo is not None and start <= timestamp.timestamp() <= end
        except (ValueError, OverflowError, OSError):
            valid_time = False
        if not valid_time:
            rejected += 1
            continue
        matched += 1
        if len(entries) < MAX_ENTRIES:
            item = {"timestamp": redact(stamp, secret, 80), "eventMessage": redact(message, secret, 500)}
            for key, source in (("type", "messageType"), ("subsystem", "subsystem"), ("category", "category")):
                value = row.get(source, "")
                item[key] = redact(value, secret, 160) if isinstance(value, str) else ""
            entries.append(item)
    return entries, rejected, matched > len(entries)


def capture(process_record, started_epoch, secret):
    """Return JSON-safe evidence; never save raw streams or exception messages."""
    deadline = time.monotonic() + MAX_SECONDS
    result = {"scope": "Filtered auth/credential/password/shell/pam/denied/result logEvents from one caller-verified SecurityAgent; diagnostics only, not authentication acceptance",
              "collected": False, "entries": [], "maximumBytes": MAX_BYTES, "maximumSeconds": MAX_SECONDS,
              "maximumWindowSeconds": MAX_WINDOW_SECONDS}
    try:
        require_hosted()  # Before reading logs, input identity, or any OS process.
        now = time.time()
        start, end = validate(process_record, started_epoch, secret, now)
        result.update(pid=process_record["pid"], firstObservedEpoch=process_record["firstObservedEpoch"],
                      window={"startEpoch": start, "endEpoch": end})
        terms = " OR ".join('eventMessage CONTAINS[c] "' + term + '"' for term in TERMS)
        predicate = '(processID == ' + str(process_record["pid"]) + ') AND process == "SecurityAgent" AND (' + terms + ')'
        local = lambda value: dt.datetime.fromtimestamp(value).strftime("%Y-%m-%d %H:%M:%S")
        arguments = ["/usr/bin/log", "show", "--style", "json", "--info", "--debug",
                     "--start", local(start), "--end", local(end), "--predicate", predicate]
        data = read_log(arguments, deadline)
        entries, rejected, truncated = entries_from_json(data, process_record, start, end, secret)
        require(time.monotonic() <= deadline, "DiagnosticTimedOut")
        result.update(collected=True, entries=entries, rejectedEntries=rejected, entriesTruncated=truncated)
    except DiagnosticFailure as error:
        code = error.code if type(error.code) is str and error.code in FAILURE_CODES else "DiagnosticUnavailable"
        result["error"] = {"code": code, "type": "DiagnosticFailure"}
    except Exception as error:
        # No arbitrary type names, repr, raw stderr, paths, or error messages.
        kind = next((name for cls, name in ((OSError, "OSError"), (ValueError, "ValueError"),
                                          (subprocess.TimeoutExpired, "TimeoutExpired")) if isinstance(error, cls)), "UnexpectedError")
        result["error"] = {"code": "DiagnosticUnavailable", "type": kind}
    return result
