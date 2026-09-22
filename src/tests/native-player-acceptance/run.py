#!/usr/bin/env python3
"""Real mpv, launched by the production federation API on disposable CI hosts.

Two production Service TestHosts own all data. No launcher/locator is replaced.
The receiver discovers the pinned mpv via PATH; a private MPV_HOME supplies
observation, bounded fixture settings and an adversarial proxy. Product arguments
must override that proxy. Source data and live media tickets are never uploaded.
"""
import argparse
import contextlib
import ctypes
import hashlib
import http.server
import importlib.util
import json
import os
from pathlib import Path
import platform
import queue
import re
import shutil
import socket
import sqlite3
import struct
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


base = load("native_player_base", HERE.parent / "upgrade-tests/run-package-acceptance.py")
updates = load("native_player_observer", HERE.parent / "upgrade-tests/installed-update-exercise.py")
require = base.require


def chunk(name, data):
    return name + struct.pack("<I", len(data)) + data + (b"\0" if len(data) % 2 else b"")


def make_video(path, seconds=120):
    """Indexed, uncompressed AVI: no transcoder or downloaded media required."""
    require(type(seconds) is int and 1 <= seconds <= 120, "Invalid video duration")
    width, height, fps = 96, 64, 10
    frame_size, frames = width * height * 3, seconds * fps
    avih = struct.pack("<14I", 100000, frame_size * fps, 0, 0x10, frames, 0, 1, frame_size, width, height, 0, 0, 0, 0)
    strh = struct.pack("<4s4sIHH8I4h", b"vids", b"DIB ", 0, 0, 0, 0, 1, fps, 0, frames, frame_size, 0xffffffff, 0,
                       0, 0, width, height)
    strf = struct.pack("<IiiHHIIiiII", 40, width, height, 1, 24, 0, frame_size, 0, 0, 0, 0)
    header = chunk(b"LIST", b"hdrl" + chunk(b"avih", avih) + chunk(b"LIST", b"strl" + chunk(b"strh", strh) + chunk(b"strf", strf)))
    movie_bytes = frames * (8 + frame_size)
    total = 4 + len(header) + 12 + movie_bytes + 8 + frames * 16
    with path.open("xb") as output:
        output.write(b"RIFF" + struct.pack("<I", total) + b"AVI " + header)
        output.write(b"LIST" + struct.pack("<I", 4 + movie_bytes) + b"movi")
        for frame in range(frames):
            color = bytes((220, 45, 25)) if frame >= 80 * fps else bytes((25, 45 + frame % 80, 220))
            output.write(chunk(b"00db", color * (width * height)))
        output.write(chunk(b"idx1", b"".join(struct.pack("<4sIII", b"00db", 0x10, 4 + frame * (8 + frame_size), frame_size)
                                               for frame in range(frames))))
    require(path.stat().st_size == total + 8 and path.stat().st_size < 24 * 1024 ** 2, "Video fixture size differs")
    return {"durationSeconds": seconds, "width": width, "height": height, "fps": fps,
            "sizeBytes": path.stat().st_size, "sha256": base.sha256(path)}


class ProxyTrap:
    def __init__(self):
        self.hits = 0
        owner = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                owner.hits += 1
                self.send_response(503)
                self.send_header("Content-Length", "0")
                self.end_headers()
            do_CONNECT = do_POST = do_GET
            def log_message(self, *_):
                pass
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.origin = f"http://127.0.0.1:{self.server.server_port}"

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        require(not self.thread.is_alive(), "Proxy thread did not stop")


class ReferenceCanary:
    """One owned loopback endpoint; retain counts, never requested URLs."""
    def __init__(self):
        self._lock = threading.Lock()
        self.hits, self.unexpected, self.exceeded = 0, 0, False
        self._path = "/" + uuid.uuid4().hex + "/segment.ts"
        owner = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                self.connection.settimeout(3)
                with owner._lock:
                    if self.path == owner._path:
                        owner.hits += 1
                    else:
                        owner.unexpected += 1
                    owner.exceeded |= owner.hits + owner.unexpected > 64
                self.send_response(503 if self.path == owner._path else 404)
                self.send_header("Content-Length", "0")
                self.end_headers()
            do_HEAD = do_GET
            def log_message(self, *_):
                pass
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}" + self._path

    def counts(self):
        with self._lock:
            return {"referenceRequests": self.hits, "unexpectedRequests": self.unexpected, "budgetExceeded": self.exceeded}

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        require(not self.thread.is_alive(), "Reference canary thread did not stop")


def reference_payload(kind, url):
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme == "http" and parsed.hostname == "127.0.0.1" and parsed.port and
            parsed.username is None and parsed.password is None and not parsed.query and not parsed.fragment and
            re.fullmatch(r"/[0-9a-f]{32}/segment\.ts", parsed.path), "Reference fixture must target its own loopback canary")
    if kind == "m3u":
        # Both parent and child use FFmpeg's input. This keeps the canary control
        # independent of mpv's curl interpretation of the fixture proxy option.
        return ("#EXTM3U\n#EXTINF:1,owned canary\nlavf://" + url + "\n").encode()
    require(kind == "hls-lavf", "Unknown reference fixture")
    return ("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:1\n#EXT-X-MEDIA-SEQUENCE:0\n"
            "#EXTINF:1.0,\n" + url + "\n#EXT-X-ENDLIST\n").encode()


def event_observer_script(path, journal):
    """Observation only: fixed events/reasons/PID; no player options or URLs."""
    path.write_text("""local mp = require 'mp'
local utils = require 'mp.utils'
local count = 0
local reasons = {eof=true, error=true, stop=true, quit=true, redirect=true}
local function record(name, event)
    count = count + 1
    if count > 65 then return end
    local value = {event=name, pid=mp.get_property_number('pid')}
    if count == 65 then value.event = 'overflow' end
    if name == 'end-file' then
        value.reason = reasons[event.reason] and event.reason or 'unknown'
        value.errorPresent = type(event.error) == 'string' and event.error ~= ''
    end
    local output = assert(io.open(""" + json.dumps(str(journal), ensure_ascii=False) + """, 'ab'))
    output:write(utils.format_json(value), '\\n')
    output:close()
end
for _, name in ipairs({'start-file', 'file-loaded', 'end-file'}) do
    mp.register_event(name, function(event) record(name, event) end)
end
""", encoding="utf-8")


def playback_completion(journal, pid):
    if not journal.exists():
        return None
    require(journal.stat().st_size <= 32768, "Player event evidence exceeded its bound")
    data = journal.read_bytes()
    # An atomic write is not assumed; a partial last line is still in progress.
    if data and not data.endswith(b"\n"):
        return None
    lines = data.split(b"\n")[:-1]
    require(len(lines) <= 64, "Player event count exceeded its bound")
    events, started = [], False
    for line in lines:
        value = json.loads(line)
        name = value.get("event")
        require(value.get("pid") == pid and type(value.get("pid")) in (int, float) and
                name in ("start-file", "file-loaded", "end-file"), "Unexpected player event identity")
        value["pid"] = int(value["pid"])
        if name == "start-file":
            started = True
        else:
            require(started, "Player terminal event arrived without a load attempt")
        if name == "end-file":
            require(value.get("reason") in ("eof", "error", "stop", "quit", "redirect", "unknown") and
                    type(value.get("errorPresent")) is bool, "Unexpected player end-file evidence")
        require(set(value) == ({"event", "pid", "reason", "errorPresent"} if name == "end-file" else {"event", "pid"}),
                "Player event retained unexpected data")
        events.append(value)
    # Redirecting a playlist or being idle before opening it is not a completed
    # attempt. Require actual EOF/error, not an operator stop/quit or elapsed sleep.
    terminal = next((value for value in reversed(events) if value["event"] == "end-file" and
                     value["reason"] in ("eof", "error")), None)
    if terminal is None or events[-1] != terminal:
        return None
    return {"outcome": terminal["reason"], "events": events, "passed": True}


class WindowsPipe:
    """Sequential nonblocking byte-pipe transactions, with no pending native I/O.

    A blocking read on a synchronous Windows handle serializes later writes on
    that handle. Do not run a reader thread. PIPE_NOWAIT makes each native call
    immediate; command() handles partial writes and polling within one deadline.
    https://learn.microsoft.com/windows/win32/ipc/named-pipe-type-read-and-wait-modes
    """
    def __init__(self, address):
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                            ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        self.kernel.CreateFileW.restype = ctypes.c_void_p
        self.kernel.SetNamedPipeHandleState.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint32),
                                                       ctypes.c_void_p, ctypes.c_void_p]
        self.kernel.SetNamedPipeHandleState.restype = ctypes.c_int32
        for name in ("ReadFile", "WriteFile"):
            function = getattr(self.kernel, name)
            function.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32,
                                 ctypes.POINTER(ctypes.c_uint32), ctypes.c_void_p]
            function.restype = ctypes.c_int32
        self.kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        self.kernel.CloseHandle.restype = ctypes.c_int32
        self.handle = self.kernel.CreateFileW(address, 0xc0000000, 0, None, 3, 0, None)
        self.buffer = bytearray()
        if self.handle == ctypes.c_void_p(-1).value:
            self.handle = None
            raise OSError(ctypes.get_last_error(), "Player pipe could not be opened")
        mode = ctypes.c_uint32(1)  # PIPE_READMODE_BYTE | PIPE_NOWAIT
        if not self.kernel.SetNamedPipeHandleState(self.handle, ctypes.byref(mode), None, None):
            code = ctypes.get_last_error()
            self.close()
            raise OSError(code, "Player pipe nonblocking mode unavailable")

    def write(self, data, deadline):
        require(len(data) <= 65536, "Player IPC request exceeded its bound")
        offset = 0
        while offset < len(data):
            if time.monotonic() >= deadline:
                raise TimeoutError("Player IPC write exceeded its deadline")
            payload = ctypes.create_string_buffer(data[offset:])
            written = ctypes.c_uint32()
            if not self.kernel.WriteFile(self.handle, payload, len(data) - offset, ctypes.byref(written), None):
                raise OSError(ctypes.get_last_error(), "Player IPC write failed")
            require(written.value <= len(data) - offset, "Player IPC write count differs")
            offset += written.value
            if not written.value:
                time.sleep(.01)

    def readline(self, deadline):
        while time.monotonic() < deadline:
            newline = self.buffer.find(b"\n")
            if newline >= 0:
                require(newline < 65536, "Player IPC message exceeded its bound")
                line = bytes(self.buffer[:newline + 1])
                del self.buffer[:newline + 1]
                return line
            require(len(self.buffer) < 65536, "Player IPC message exceeded its bound")
            data = ctypes.create_string_buffer(min(4096, 65536 - len(self.buffer)))
            received = ctypes.c_uint32()
            if not self.kernel.ReadFile(self.handle, data, len(data), ctypes.byref(received), None):
                code = ctypes.get_last_error()
                if code == 232:  # ERROR_NO_DATA: no bytes available on the open nonblocking pipe.
                    time.sleep(.01)
                    continue
                raise OSError(code, "Player IPC read failed")
            require(received.value <= len(data), "Player IPC read count differs")
            self.buffer.extend(data.raw[:received.value])
            if not received.value:
                time.sleep(.01)
        raise TimeoutError("Player IPC read exceeded its deadline")

    def close(self):
        if self.handle is not None:
            handle, self.handle = self.handle, None
            require(self.kernel.CloseHandle(handle), "Player IPC handle did not close")


class IPC:
    """One private mpv socket/pipe, bounded messages and request waits."""
    def __init__(self, address):
        self.address, self.stream, self.socket, self.reader = address, None, None, None
        self.pipe = None
        self.responses, self.errors, self.counter = queue.Queue(maxsize=128), [], 0

    def connect(self, deadline):
        while time.monotonic() < deadline:
            try:
                if os.name == "nt":
                    self.pipe = WindowsPipe(self.address)
                else:
                    self.socket = socket.socket(socket.AF_UNIX)
                    self.socket.settimeout(1)
                    self.socket.connect(self.address)
                    self.socket.settimeout(None)
                    self.stream = self.socket.makefile("rwb", buffering=0)
                break
            except OSError:
                if self.socket:
                    self.socket.close()
                    self.socket = None
                time.sleep(0.1)
        require(self.stream is not None or self.pipe is not None, "Player IPC was not ready within its deadline")
        if not self.pipe:
            self.reader = threading.Thread(target=self.read, daemon=True)
            self.reader.start()
        return self

    def read(self):
        try:
            while True:
                line = self.stream.readline(65537)
                if not line:
                    return
                require(len(line) <= 65536 and line.endswith(b"\n"), "Player IPC message exceeded its bound")
                value = json.loads(line)
                if "request_id" in value:
                    self.responses.put_nowait(value)
        except Exception as error:
            self.errors.append(type(error).__name__)

    def command(self, *command, timeout=5):
        require(not self.errors, "Player IPC failed")
        self.counter += 1
        deadline = time.monotonic() + timeout
        message = json.dumps({"command": list(command), "request_id": self.counter}).encode() + b"\n"
        if self.pipe:
            self.pipe.write(message, deadline)
        else:
            self.stream.write(message)
        while time.monotonic() < deadline:
            if self.pipe:
                result = json.loads(self.pipe.readline(deadline))
            else:
                try:
                    result = self.responses.get(timeout=max(.01, deadline - time.monotonic()))
                except queue.Empty:
                    break
            if result.get("request_id") == self.counter:
                if command[0] == "get_property" and result.get("error") == "property unavailable":
                    return None
                require(result.get("error") == "success", "Player IPC command failed: " + str(result.get("error")))
                return result.get("data")
        raise TimeoutError("Player IPC request exceeded its deadline")

    def get(self, name):
        return self.command("get_property", name)

    def close(self):
        if self.pipe:
            self.pipe.close()
            self.pipe = None
        if self.socket:
            with contextlib.suppress(OSError):
                self.socket.shutdown(socket.SHUT_RDWR)
        if self.stream:
            self.stream.close()
        if self.socket:
            self.socket.close()
        if self.reader:
            self.reader.join(timeout=3)
            require(not self.reader.is_alive(), "Player IPC reader did not stop")


def wait(check, timeout, message):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(.15)
    raise TimeoutError(message)


def api(origin, path, method="GET", body=None, expected=200):
    request = urllib.request.Request(origin + path, method=method,
                                     data=None if body is None else json.dumps(body).encode(),
                                     headers={"Content-Type": "application/json"})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        response = opener.open(request, timeout=12)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        data = response.read(2 * 1024 ** 2 + 1)
        require(len(data) <= 2 * 1024 ** 2, "API response exceeded its bound")
        require(response.status in ((expected,) if isinstance(expected, int) else expected),
                "Unexpected API status for " + path.split("?")[0] + ": " + str(response.status))
        return json.loads(data) if data else None


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def parent_pid(pid):
    require(type(pid) is int and pid > 0, "Player PID must be a positive integer")
    if os.name == "nt":
        command = ["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                   f"Get-CimInstance Win32_Process -Filter 'ProcessId = {pid}' | "
                   "Select-Object -ExpandProperty ParentProcessId"]
    else:
        command = ["ps", "-p", str(pid), "-o", "ppid="]
    value = subprocess.check_output(command, text=True, timeout=5).strip()
    require(re.fullmatch(r"[1-9][0-9]*", value) is not None, "Player parent PID unavailable")
    return int(value)


def redact(text):
    text = re.sub(r"(?:lavf://)?https?://[^\s\"'<>]+", "[redacted-url]", text, flags=re.IGNORECASE)
    return re.sub(r"[a-fA-F0-9]{64}", "[redacted-ticket-or-digest]", text)


def retain_log(path, results):
    with path.open("rb") as source:
        source.seek(max(0, path.stat().st_size - 128 * 1024))
        (results / path.name).write_text(redact(source.read().decode("utf-8", errors="replace")), encoding="utf-8")


def screenshot(ipc, path):
    require(not path.exists(), "Screenshot path must be fresh")
    ipc.command("screenshot-to-file", str(path), "video")
    wait(lambda: path.is_file() and path.stat().st_size > 24, 5, "Player did not save its decoded frame")
    data = path.read_bytes()
    require(data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == (96, 64),
            "Player screenshot is not the expected decoded video frame")
    return {"file": path.name, "sha256": hashlib.sha256(data).hexdigest(), "sizeBytes": len(data)}


def requested_video_output(rid):
    require(rid in ("win-x64", "osx-x64", "osx-arm64"), "Unsupported native player platform")
    # In the pinned mpv, native Cocoa/OpenGL is the standalone application's
    # libmpv VO, whose preinit creates CocoaCB/GLLayer. gpu-context=cocoa does
    # not exist in that version. Keep the other platforms' gpu-next unchanged.
    return "libmpv" if rid == "osx-x64" else "gpu-next"


def verify_video_output(ipc, rid, native_log):
    actual = ipc.get("current-vo")
    require(ipc.get("video-params/w") == 96 and ipc.get("video-params/h") == 64 and
            actual == requested_video_output(rid), "The requested real video output was not configured")
    witness = {"requestedVO": requested_video_output(rid), "actualVO": actual}
    if rid == "osx-x64":
        with native_log.open("rb") as source:
            initial = source.read(128 * 1024).decode("utf-8", errors="replace")
        cgl = re.search(r"\[cocoacb(?:/cocoacb)?\] Created CGL pixel format with attributes: [^\r\n]{1,512}", initial)
        version = re.search(r"\[[^\]\r\n]+\] GL_VERSION='([^'\r\n]{1,200})'", initial)
        renderer = re.search(r"\[[^\]\r\n]+\] GL_RENDERER='([^'\r\n]{1,200})'", initial)
        require(cgl is not None and version is not None and renderer is not None,
                "Native Cocoa/OpenGL initialization was not witnessed")
        witness.update(cocoaPixelFormatCreated=True, openGLVersion=version.group(1), openGLRenderer=renderer.group(1),
                       scope="Pinned standalone mpv Cocoa/OpenGL output; default gpu-next/macvk remains unverified on Intel")
    return witness


def seek_reached(ipc, target):
    # time-pos can reflect the seek target before decoding/output has settled.
    if ipc.get("seeking") is not False:
        return False
    position = ipc.get("time-pos")
    return type(position) in (int, float) and position >= target - .2


def player_denials(path):
    """Count fixed HTTP status diagnostics in the owned player's bounded log."""
    if not path.exists():
        return 0
    with path.open("rb") as source:
        source.seek(max(0, path.stat().st_size - 128 * 1024))
        return source.read().count(b"http: HTTP error 403 Forbidden")


def wait_browsing_cancellation(ipc, evidence, timeout=20):
    """A rejected asynchronous seek can retry before the player reaches EOF."""
    deadline = time.monotonic() + timeout
    evidence.update(passed=False, timeoutSeconds=timeout, samples=[])
    while time.monotonic() < deadline:
        snapshot = {name: ipc.get(name) for name in ("time-pos", "seeking", "eof-reached", "idle-active")}
        require(snapshot["time-pos"] is None or type(snapshot["time-pos"]) in (int, float),
                "Player returned a nonnumeric playback position")
        require(all(snapshot[name] is None or type(snapshot[name]) is bool
                    for name in ("seeking", "eof-reached", "idle-active")), "Player returned an invalid playback state")
        evidence["samples"].append(snapshot)
        require(len(evidence["samples"]) <= 100, "Cancellation observation exceeded its bound")
        if snapshot["eof-reached"] is True or snapshot["idle-active"] is True:
            evidence.update(eofOrIdle=True, positionAvailable=snapshot["time-pos"] is not None)
            return
        time.sleep(.25)
    raise TimeoutError("Disabled browsing never reached actual player EOF/idle after the uncached seek")


def reference_asset(reader, reference, payload):
    """Resolve again, then prove the real preview ticket serves the altered asset."""
    detail = api(reader["origin"], "/federation/local/resources/resolve", "POST", {"refs": [reference]})["resources"][0]
    asset = base.one((value for value in detail["assets"] if value["kind"] == "video"), "fresh remote video asset")
    asset_ref = {"resourceRef": reference, "assetId": asset["assetId"]}
    session = api(reader["origin"], "/federation/local/playback-sessions", "POST", {"assetRef": asset_ref, "mode": "preview"})
    url = session.get("url", "")
    require(session.get("launched") is False and
            re.fullmatch(re.escape(reader["origin"]) + r"/federation/local/media/[a-f0-9]{64}", url),
            "Control ticket is not this receiver's opaque loopback media URL")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(url, timeout=10) as response:
        require(response.status == 200 and response.read(4097) == payload,
                "Federation did not serve the exact altered reference fixture")
    return asset_ref, url


def observed_player(ipc, observer, executable, expected_parent, source_pid):
    pid = ipc.get("pid")
    records = wait(lambda: [p for p in observer.current(executable) if p["pid"] == pid], 5,
                   "Reference-test player executable identity differs")
    require(len(observer.current(executable)) == 1, "Unexpected player on receiver or source")
    parent = parent_pid(pid)
    require(parent == expected_parent and parent != source_pid, "Reference player has the wrong owning process")
    return dict(records[0], parentPid=parent, sourcePid=source_pid)


def close_reference_player(ipc, context, child=None):
    ipc.command("quit")
    if child is not None:
        child.wait(timeout=10)
    wait(lambda: not context["observer"].current(context["executable"]), 10, "Reference-test player did not exit")
    ipc.close()
    context["ipcs"].remove(ipc)
    if os.name != "nt":
        Path(ipc.address).unlink(missing_ok=True)


def verify_reference_counts(control, after):
    require(control["referenceRequests"] > 0 and control["unexpectedRequests"] == 0 and not control["budgetExceeded"],
            "Reference control never reached its owned canary, or exceeded its request contract")
    require(after == control, "Production player followed an embedded reference")
    return {"controlRequests": control["referenceRequests"], "productionRequests": 0,
            "unexpectedRequests": 0, "budgetExceeded": False, "passed": True}


def exercise_references(kind, context, report):
    """Same bytes/player/config; only the control opts into references manually."""
    executable, work = context["executable"], context["work"]
    reader, source, observer = context["reader"], context["source"], context["observer"]
    require(not observer.current(executable), "A previous player is still running")
    require(reader["process"].poll() is None and source["process"].poll() is None, "A federation host exited")
    if os.name != "nt":
        Path(context["address"]).unlink(missing_ok=True)
    canary = ReferenceCanary()
    context["canaries"].append(canary)
    payload = reference_payload(kind, canary.url)
    require(len(payload) <= 4096, "Reference fixture exceeds its bound")
    media = source["directory"] / "fixture.avi"
    require(media.is_file() and not media.is_symlink() and media.resolve().is_relative_to(work), "Source fixture is not owned")
    media.write_bytes(payload)
    journal, script = work / (kind + "-events.jsonl"), work / (kind + "-events.lua")
    event_observer_script(script, journal)
    configuration = context["configuration"] / "mpv.conf"
    extra = ["script=" + str(script), "log-file=" + str(work / ("mpv-reference-" + kind + ".log"))]
    if kind == "hls-lavf":
        # The pinned development mpv routes nested HTTP through curl by default,
        # where direct:// is not a proxy bypass. Exercise FFmpeg nested I/O on
        # both sides so a broken control cannot masquerade as reference denial.
        extra += ["demuxer=lavf", "demuxer-lavf-format=hls", "demuxer-lavf-allow-mimetype=no", "curl-enabled=no"]
    else:
        # A playlist redirect otherwise loses per-file direct:// and lets the
        # control's child use the adversarial proxy. Keep both sides identical.
        extra += ["playlist-inherit-options=yes"]
    configuration.write_text(context["originalConfiguration"] + "\n".join(extra) + "\n", encoding="utf-8")
    config_hash = base.sha256(configuration)
    case = report.setdefault("embeddedReferences", {})[kind] = {
        "passed": False, "fixtureSizeBytes": len(payload), "fixtureSHA256": hashlib.sha256(payload).hexdigest(),
        "forcedLavfDemuxer": kind == "hls-lavf", "curlDisabled": kind == "hls-lavf",
        "forcedLavfFormat": "hls" if kind == "hls-lavf" else None,
        "playlistInheritsPerFileOptions": kind == "m3u",
        "configurationSHA256": config_hash}
    report["currentStage"] = "reference-control-" + kind
    api(reader["origin"], "/federation/local/peers/browsing", "PUT", {"enabled": True})
    _, control_url = reference_asset(reader, context["reference"], payload)
    log = (work / ("reference-" + kind + "-control.log")).open("wb")
    context["streams"].append(log)
    child = subprocess.Popen([str(executable), "--{", "--http-proxy=direct://", "lavf://" + control_url,
                              "--access-references=yes", "--}"], env=context["environment"], stdout=log, stderr=subprocess.STDOUT)
    context["children"].append(child)
    ipc = IPC(context["address"])
    context["ipcs"].append(ipc)
    ipc.connect(time.monotonic() + 20)
    case["controlProcess"] = observed_player(ipc, observer, executable, os.getpid(), source["process"].pid)
    require(case["controlProcess"]["pid"] == child.pid, "Control IPC belongs to a different process")
    case["controlCompletion"] = wait(lambda: playback_completion(journal, child.pid), 30,
                                      "Reference control never completed a load with EOF/error")
    close_reference_player(ipc, context, child)
    control_counts = canary.counts()
    verify_reference_counts(control_counts, control_counts)
    case["controlRequests"] = control_counts["referenceRequests"]
    # All control I/O and processes have exited before resetting observation.
    journal.write_bytes(b"")
    require(base.sha256(configuration) == config_hash, "Reference-test configurations differ")
    report["currentStage"] = "reference-production-" + kind
    asset_ref, _ = reference_asset(reader, context["reference"], payload)
    proxy_before = context["trap"].hits
    launched = api(reader["origin"], "/federation/local/playback-sessions", "POST", {"assetRef": asset_ref, "mode": "player"})
    require(launched.get("launched") is True and launched.get("url") is None, "Production reference fixture was not launched")
    ipc = IPC(context["address"])
    context["ipcs"].append(ipc)
    ipc.connect(time.monotonic() + 20)
    case["productionProcess"] = observed_player(ipc, observer, executable, reader["process"].pid, source["process"].pid)
    require(case["productionProcess"]["pid"] != child.pid, "Production reused the control player")
    case["productionCompletion"] = wait(lambda: playback_completion(journal, case["productionProcess"]["pid"]), 30,
                                         "Production reference load never reached EOF/error")
    close_reference_player(ipc, context)
    case["canary"] = verify_reference_counts(control_counts, canary.counts())
    require(context["trap"].hits == proxy_before, "Production reference fixture used the adversarial proxy")
    require(reader["process"].poll() is None and source["process"].poll() is None and not observer.errors,
            "Reference test lost a host or its process observer")
    case.update(passed=True, sourcePlayerCount=0, productionProxyRequests=0, freshOpaqueAssetResolved=True,
                identicalFixtureBytesVerified=True, identicalPrivateConfigurationVerified=True)


def run(args):
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    executable, results = args.mpv.resolve(), args.results_directory.resolve()
    provenance = json.loads(args.provenance.read_text())
    require(provenance.get("passed") is True and provenance.get("rid") == args.rid and
            Path(provenance["executable"]).resolve() == executable and
            base.sha256(executable) == provenance["executableSHA256"], "Pinned mpv provenance differs")
    require(not results.exists(), "Results directory must be fresh")
    results.mkdir(parents=True)
    work = Path(tempfile.mkdtemp(prefix="bakabase-player-", dir=os.environ["RUNNER_TEMP"])).resolve()
    report = {"passed": False, "rid": args.rid, "gitHead": subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), "player": provenance,
        "scope": "Production federation policy/discovery/launcher, native mpv video output and owned embedded-reference canaries",
        "limitations": ["Pinned mpv development build; not VLC/IINA or every player version",
                        "Synthetic 120-second uncompressed AVI over loopback; not physical weak networking",
                        "MPV_HOME supplies bounded cache/IPC/proxy settings and a read-only event observer; not normal user preferences",
                        "Private settings disable ytdl fallback and do not force windows for failed/empty media; actual video must still use a real output",
                        "Intel macOS fixes the standalone mpv Cocoa/OpenGL libmpv VO; it does not certify the default gpu-next/macvk backend",
                        "M3U and forced-lavf HLS references use owned loopback canaries, not exhaustive media-format fuzzing",
                        "HLS forces lavf's hls format and disables curl in both private configs to verify FFmpeg nested I/O despite the opaque URL/AVI MIME; not every default backend combination",
                        "M3U enables playlist option inheritance in both private configs so its positive control retains the per-file proxy bypass",
                        "Video screenshots verify decoded frames, not physical display presentation"]}
    children, streams, ipc, trap, observer, native_created = [], [], None, None, None, False
    reference_ipcs, canaries = [], []
    cleanup_errors = []
    try:
        video = work / "fixture.avi"
        report["video"] = make_video(video)
        observer = updates.ProcessObserver([executable]).__enter__()
        require(not observer.current(executable), "Pinned player already runs")
        trap = ProxyTrap()
        configuration = work / "mpv-config"
        configuration.mkdir()
        cache = work / "mpv-cache"
        cache.mkdir()
        address = (r"\\.\pipe\bakabase-player-" + uuid.uuid4().hex) if os.name == "nt" else str(work / "mpv.sock")
        (configuration / "mpv.conf").write_text("\n".join([
            "input-ipc-server=" + address, "idle=yes", "keep-open=yes", "force-window=no", "ao=null",
            "vo=" + requested_video_output(args.rid), "hwdec=no", "cache=yes", "demuxer-max-bytes=1MiB", "demuxer-readahead-secs=1",
            "gpu-shader-cache-dir=" + str(cache), "icc-cache-dir=" + str(cache),
            "http-proxy=" + trap.origin, "save-position-on-quit=no", "load-scripts=no", "osc=no", "ytdl=no",
            "log-file=" + str(work / "mpv.log"), "msg-level=all=warn"]) + "\n", encoding="utf-8")
        player_environment = dict(os.environ, MPV_HOME=str(configuration), NO_PROXY="", no_proxy="",
                                  PATH=str(executable.parent) + os.pathsep + os.environ["PATH"])
        # Positive control: the same private player configuration really contacts
        # the trap unless production's per-file direct:// override is present.
        control_log = (work / "control.log").open("wb")
        streams.append(control_log)
        control_target = f"http://127.0.0.1:{free_port()}/proxy-control.avi"
        require(not control_target.startswith(trap.origin + "/"), "Proxy control origin must differ from the proxy")
        control = subprocess.Popen([str(executable), control_target], env=player_environment,
                                   stdout=control_log, stderr=subprocess.STDOUT)
        children.append(control)
        native_created = True
        report["currentStage"] = "proxy-control-ipc"
        ipc = IPC(address).connect(time.monotonic() + 20)
        wait(lambda: trap.hits > 0, 15, "Configured player proxy was not exercised by its control")
        report["proxyControlHits"] = trap.hits
        report["currentStage"] = "proxy-control-quit"
        ipc.command("quit")
        control.wait(timeout=10)
        ipc.close()
        ipc = None
        wait(lambda: not observer.current(executable), 5, "Control player did not exit")
        if os.name != "nt":
            Path(address).unlink(missing_ok=True)

        dll = ROOT / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
        report["currentStage"] = "service-fixtures"
        require(dll.is_file(), "Build the production Service TestHost first")
        nodes = []
        for label in ("reader", "source"):
            directory = work / label
            directory.mkdir()
            port = free_port()
            log = (work / (label + ".log")).open("wb")
            streams.append(log)
            environment = dict(player_environment if label == "reader" else os.environ,
                               BAKABASE_FEDERATION_TEST_MEDIA_FILE=str(video), Analytics__Sentry__BackendDsn="")
            process = subprocess.Popen([args.dotnet, str(dll), str(port), str(directory), "3"], cwd=ROOT,
                                       stdout=log, stderr=subprocess.STDOUT, env=environment)
            children.append(process)
            nodes.append({"directory": directory, "process": process, "origin": f"http://127.0.0.1:{port}"})
        def ready():
            require(all(node["process"].poll() is None for node in nodes), "Service fixture exited during startup")
            return all((node["directory"] / "ready").exists() for node in nodes)
        wait(ready, 120, "Service fixtures did not become ready")
        reader, source = nodes
        status = api(source["origin"], "/federation/local/peers")
        identity = status["identity"]
        invitation = api(source["origin"], "/federation/local/peers/invite", "POST")
        connected = api(reader["origin"], "/federation/local/peers/connect", "POST",
                        {"address": source["origin"], "code": invitation["code"]})
        require(connected["outcome"] == "granted", "Source did not grant receiver access")
        api(reader["origin"], "/federation/local/peers/browsing", "PUT", {"enabled": True})
        reference = {"nodeId": identity["nodeId"], "libraryEpoch": identity["libraryEpoch"], "resourceId": 1}
        detail = api(reader["origin"], "/federation/local/resources/resolve", "POST", {"refs": [reference]})["resources"][0]
        asset = base.one((v for v in detail["assets"] if v["kind"] == "video"), "remote video asset")
        trap_before = trap.hits
        report["currentStage"] = "production-video"
        launched = api(reader["origin"], "/federation/local/playback-sessions", "POST",
                       {"assetRef": {"resourceRef": reference, "assetId": asset["assetId"]}, "mode": "player"})
        require(launched.get("launched") is True and launched.get("url") is None, "Production API did not launch a player")
        ipc = IPC(address).connect(time.monotonic() + 20)
        pid = ipc.get("pid")
        observed = wait(lambda: [p for p in observer.current(executable) if p["pid"] == pid], 5,
                        "Launched player executable identity differs")
        report["nativePlayerProcess"] = observed[0]
        require(len(observer.current(executable)) == 1, "Unexpected extra player process")
        parent = parent_pid(pid)
        require(parent == reader["process"].pid and parent != source["process"].pid,
                "The receiver Service must be the unique native player's parent")
        report["nativePlayerProcess"]["parentPid"] = parent
        report["nativePlayerProcess"]["receiverPid"] = reader["process"].pid
        report["nativePlayerProcess"]["sourcePid"] = source["process"].pid
        wait(lambda: isinstance(ipc.get("time-pos"), (int, float)) and ipc.get("time-pos") >= 1, 25,
             "Actual video playback clock did not advance")
        report["graphicsWitness"] = verify_video_output(ipc, args.rid, work / "mpv.log")
        report["videoOutput"] = report["graphicsWitness"]["actualVO"]
        ipc.command("set_property", "pause", True)
        paused = ipc.get("time-pos")
        time.sleep(1.2)
        still = ipc.get("time-pos")
        require(ipc.get("pause") is True and abs(still - paused) <= .2, "Paused video clock kept advancing")
        report["pause"] = {"beforeSeconds": paused, "afterSeconds": still, "passed": True}
        report["firstFrame"] = screenshot(ipc, results / "first-frame.png")
        ipc.command("set_property", "pause", False)
        wait(lambda: ipc.get("time-pos") > still + 1, 8, "Video did not resume advancing")
        ipc.command("seek", 90, "absolute+exact")
        wait(lambda: seek_reached(ipc, 90), 12, "Player did not complete its seek to 90 seconds")
        ipc.command("set_property", "pause", True)
        report["seekFrame"] = screenshot(ipc, results / "seek-frame.png")
        require(report["firstFrame"]["sha256"] != report["seekFrame"]["sha256"], "Seek did not produce a changed decoded frame")
        report["seekSeconds"] = ipc.get("time-pos")
        report["normalVideoPlaybackPassed"] = True
        # Disable browsing while playback is paused, then force an uncached seek.
        # An old ticket cannot resume reading through a newly issued request.
        report["currentStage"] = "browsing-cancellation"
        denials_before = player_denials(work / "mpv.log")
        api(reader["origin"], "/federation/local/peers/browsing", "PUT", {"enabled": False})
        ipc.command("seek", 55, "absolute+exact")
        ipc.command("set_property", "pause", False)
        cancellation = report["browsingCancellation"] = {}
        try:
            wait_browsing_cancellation(ipc, cancellation)
        finally:
            cancellation["deniedHTTPRequests"] = player_denials(work / "mpv.log") - denials_before
        require(cancellation["deniedHTTPRequests"] > 0, "Player did not observe an HTTP 403 for the uncached seek")
        if ipc.get("video-out-params/w") is not None:
            cancellation["terminalFrame"] = screenshot(ipc, results / "cancelled-frame.png")
            require(cancellation["terminalFrame"]["sha256"] == report["seekFrame"]["sha256"],
                    "Player decoded a changed frame after browsing was disabled")
            cancellation["unchangedDecodedFrame"] = True
        else:
            cancellation["videoOutputUnavailable"] = True
        cancellation["passed"] = True
        require(trap.hits == trap_before, "Production media was sent through the configured proxy")
        report["productionProxyHits"] = trap.hits - trap_before
        ipc.command("quit")
        wait(lambda: not observer.current(executable), 10, "Production player did not exit")
        ipc.close()
        ipc = None
        reference_context = {"executable": executable, "work": work, "reader": reader, "source": source,
            "observer": observer, "address": address, "configuration": configuration,
            "originalConfiguration": (configuration / "mpv.conf").read_text(encoding="utf-8"),
            "environment": player_environment, "reference": reference, "streams": streams, "children": children,
            "ipcs": reference_ipcs, "canaries": canaries, "trap": trap}
        for kind in ("m3u", "hls-lavf"):
            exercise_references(kind, reference_context, report)
        for node in nodes:
            node["process"].terminate()
            node["process"].wait(timeout=10)
        report["databases"] = []
        for node in nodes:
            path = node["directory"] / "bakabase_insideworld.db"
            with contextlib.closing(sqlite3.connect(path.resolve().as_uri() + "?mode=rw", uri=True)) as db:
                require(db.execute("PRAGMA integrity_check").fetchall() == [("ok",)], "Fixture database is corrupt")
                count = db.execute("SELECT COUNT(*) FROM ResourcesV2").fetchone()[0]
                played = db.execute("SELECT COUNT(*) FROM ResourcesV2 WHERE PlayedAt IS NOT NULL").fetchone()[0]
                require(count == 3 and played == 0, "Remote playback changed resource count or playback history")
                report["databases"].append({"resources": count, "playedAtCount": played, "integrity": "ok"})
        require(not observer.errors, "Native player process observation failed")
        report["passed"] = True
    except Exception as error:
        report["error"] = {"type": type(error).__name__, "message": redact(str(error))[:1600]}
    finally:
        def cleanup(label, action):
            try:
                action()
            except Exception as error:
                cleanup_errors.append({"stage": label, "type": type(error).__name__})
        if ipc:
            cleanup("request-player-quit", lambda: ipc.command("quit", timeout=2))
        for reference_ipc in reference_ipcs:
            cleanup("request-reference-player-quit", lambda ipc=reference_ipc: ipc.command("quit", timeout=2))
        if native_created:
            cleanup("stop-owned-player", lambda: base.stop_native(executable))
        if ipc:
            cleanup("close-player-ipc", ipc.close)
        for reference_ipc in reference_ipcs:
            cleanup("close-reference-player-ipc", reference_ipc.close)
        for child in children:
            def stop():
                if child.poll() is None:
                    child.terminate()
                    try:
                        child.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        child.kill()
                        child.wait(timeout=5)
            cleanup("stop-owned-child", stop)
        if observer:
            cleanup("close-native-observer", observer.close)
            report["observerErrors"] = observer.errors
        for stream in streams:
            cleanup("close-log", stream.close)
        if trap:
            cleanup("close-proxy", trap.close)
        for canary in canaries:
            cleanup("close-reference-canary", canary.close)
        for path in work.glob("*.log"):
            cleanup("retain-sanitized-log", lambda path=path: retain_log(path, results))
        cleanup("remove-owned-fixtures", lambda: shutil.rmtree(work))
        report["ownedFilesRemoved"] = not work.exists()
        report["cleanupErrors"] = cleanup_errors
        report["passed"] = bool(report["passed"] and not cleanup_errors and report["ownedFilesRemoved"] and not report.get("observerErrors"))
        (results / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=("win-x64", "osx-x64", "osx-arm64"))
    parser.add_argument("--mpv", required=True, type=Path)
    parser.add_argument("--provenance", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--results-directory", required=True, type=Path)
    raise SystemExit(run(parser.parse_args()))
