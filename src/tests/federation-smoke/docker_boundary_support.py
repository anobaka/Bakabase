"""Narrow process/HTTP ownership helpers for docker-boundary.py; no work at import."""
import http.server
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid

LABEL = "bakabase.docker-boundary"


class BoundaryFailure(RuntimeError):
    """Messages are deliberately free of HTTP bodies, credentials and command arguments."""


class Deadline:
    def __init__(self, seconds, disk_path=None, disk_floor=0):
        self.end = time.monotonic() + seconds
        self.disk_path, self.disk_floor = disk_path, disk_floor

    def remaining(self, maximum=20):
        left = self.end - time.monotonic()
        if left <= 0:
            raise BoundaryFailure("Overall deadline exceeded")
        if self.disk_path and shutil.disk_usage(self.disk_path).free < self.disk_floor:
            raise BoundaryFailure("Free disk space fell below the fixture floor")
        return min(maximum, left)


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def decode_response(status, body, expected=200, raw=False):
    allowed = (expected,) if isinstance(expected, int) else expected
    if status not in allowed:
        raise BoundaryFailure(f"HTTP {status}; expected {allowed}")
    if raw:
        return body
    try:
        result = json.loads(body) if body else None
    except ValueError as error:
        raise BoundaryFailure("Invalid JSON response") from error
    if 200 <= status < 300 and isinstance(result, dict) and isinstance(result.get("code"), int):
        if result["code"] != 0:
            raise BoundaryFailure("Legacy business API returned an error")
        return result.get("data")
    return result


def parse_curl_response(output, expected=200, raw=False):
    body, separator, status = output.rpartition(b"\n")
    if not separator or not re.fullmatch(rb"[1-5][0-9]{2}", status):
        raise BoundaryFailure("Curl did not return an HTTP status")
    return decode_response(int(status), body, expected, raw)


def host_request(deadline, base, path, method="GET", body=None, expected=200, headers=None, raw=False):
    request = urllib.request.Request(base + path, method=method,
        data=None if body is None else json.dumps(body).encode(),
        headers={"Content-Type": "application/json", **(headers or {})})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        response = opener.open(request, timeout=deadline.remaining())
    except urllib.error.HTTPError as error:
        response = error
    except (OSError, urllib.error.URLError) as error:
        raise BoundaryFailure("Host HTTP transport failed") from error
    with response:
        content = response.read(2 * 1024 * 1024 + 1)
        if len(content) > 2 * 1024 * 1024:
            raise BoundaryFailure("HTTP response exceeded fixture budget")
        return decode_response(response.status, content, expected, raw)


class OwnedDocker:
    def __init__(self, deadline, token=None):
        self.deadline = deadline
        self.token = token or uuid.uuid4().hex
        self.containers = []
        self.networks = []
        self.service_id = None
        self.curl_id = None
        self.curl_platform = None
        self.allowed_bases = {"http://127.0.0.1:34567"}

    def command(self, *arguments, data=None, cleanup=False, check=True):
        try:
            result = subprocess.run(["docker", *arguments], input=data, capture_output=True,
                                    timeout=20 if cleanup else self.deadline.remaining())
        except (OSError, subprocess.SubprocessError) as error:
            raise BoundaryFailure("Docker command failed or timed out") from error
        if check and result.returncode:
            raise BoundaryFailure(f"Docker command failed (exit {result.returncode})")
        return result

    def inspect_owned(self, kind, name):
        result = self.command(kind, "inspect", name, cleanup=True, check=False)
        if result.returncode:
            if b"No such" in result.stderr or b"not found" in result.stderr:
                return None
            raise BoundaryFailure("Could not inspect owned Docker resource")
        data = json.loads(result.stdout)[0]
        labels = data.get("Labels") if kind == "network" else data.get("Config", {}).get("Labels")
        if (labels or {}).get(LABEL) != self.token:
            raise BoundaryFailure("Refusing to modify a Docker resource not owned by this run")
        return data

    def service(self, action):
        if not self.service_id or not self.inspect_owned("container", self.service_id):
            raise BoundaryFailure("Owned Service container is missing")
        if action not in ("start", "stop"):
            raise BoundaryFailure("Unsupported owned Service action")
        arguments = ["container", action]
        if action == "stop":
            arguments += ["--time", "10"]
        self.command(*arguments, self.service_id)

    def remove(self, kind, name):
        registered = self.networks if kind == "network" else self.containers
        if name not in registered:
            raise BoundaryFailure("Refusing cleanup of an unregistered Docker resource")
        if self.inspect_owned(kind, name):
            args = [kind, "rm"] + (["--force"] if kind == "container" else [])
            self.command(*args, name, cleanup=True)
        registered.remove(name)

    def curl_command(self, helper, base, path, method, headers, has_body):
        if not self.service_id or not re.fullmatch(r"[0-9a-f]{64}", self.service_id):
            raise BoundaryFailure("Curl helper requires the owned full container ID")
        if not self.curl_id or not re.fullmatch(r"sha256:[0-9a-f]{64}", self.curl_id):
            raise BoundaryFailure("Curl helper requires an inspected immutable image ID")
        if base not in self.allowed_bases or not path.startswith("/") or path.startswith("//"):
            raise BoundaryFailure("Curl helper target is outside the fixture")
        args = ["run", "--rm", "--pull", "never", "--name", helper, "--label", f"{LABEL}={self.token}",
                "--network", "container:" + self.service_id, "--platform", self.curl_platform,
                "--memory", "128m", "--pids-limit", "32", "--entrypoint", "curl", "-i", self.curl_id,
                "--silent", "--show-error", "--noproxy", "*", "--connect-timeout", "2",
                "--max-time", str(max(0.1, self.deadline.remaining(15))), "--max-filesize", "2097152",
                "-X", method, "-H", "Content-Type: application/json", "-w", "\n%{http_code}"]
        for key, value in (headers or {}).items():
            args += ["-H", f"{key}: {value}"]
        if has_body:
            args += ["--data-binary", "@-"]
        return [*args, base + path]

    def request(self, path, method="GET", body=None, expected=200, headers=None, raw=False, base="http://127.0.0.1:34567"):
        if not self.service_id or not self.inspect_owned("container", self.service_id):
            raise BoundaryFailure("Curl helper cannot join an unowned container")
        helper = "bakabase-boundary-curl-" + uuid.uuid4().hex
        args = self.curl_command(helper, base, path, method, headers, body is not None)
        self.containers.append(helper)  # Register before docker run, including CLI-timeout cases.
        try:
            result = self.command(*args, data=None if body is None else json.dumps(body).encode())
        finally:
            self.remove("container", helper)
        return parse_curl_response(result.stdout, expected, raw)

    def cleanup(self):
        failures = []
        for kind, names in (("container", self.containers), ("network", self.networks)):
            for name in list(reversed(names)):
                try:
                    self.remove(kind, name)
                except Exception as error:
                    failures.append(type(error).__name__)
        return failures


def stop_native(process):
    if process is None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        pass
    finally:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait(timeout=5)


def start_deny_proxy():
    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            self.send_error(503)
        do_POST = do_CONNECT = do_GET
        def log_message(self, *_):
            pass
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def sanitized_log(content):
    text = content.decode("utf-8", errors="replace")
    return {"bytes": len(content), "exceptionTypes": sorted(set(re.findall(
        r"\b(?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*Exception\b", text))),
        "notice": "Only exception types retained; messages, URLs, credentials and paths omitted."}
