#!/usr/bin/env python3
"""Real mpv, launched by the production federation API on disposable CI hosts.

Two production Service TestHosts own all data. No launcher/locator is replaced.
The receiver discovers the pinned mpv via PATH; a private MPV_HOME supplies only
observation, cache bounds and an adversarial proxy setting. Product arguments
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
    return re.sub(r"[a-fA-F0-9]{64}", "[redacted-ticket-or-digest]", text)


def screenshot(ipc, path):
    require(not path.exists(), "Screenshot path must be fresh")
    ipc.command("screenshot-to-file", str(path), "video")
    wait(lambda: path.is_file() and path.stat().st_size > 24, 5, "Player did not save its decoded frame")
    data = path.read_bytes()
    require(data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == (96, 64),
            "Player screenshot is not the expected decoded video frame")
    return {"file": path.name, "sha256": hashlib.sha256(data).hexdigest(), "sizeBytes": len(data)}


def seek_reached(ipc, target):
    # time-pos can reflect the seek target before decoding/output has settled.
    if ipc.get("seeking") is not False:
        return False
    position = ipc.get("time-pos")
    return type(position) in (int, float) and position >= target - .2


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
        "scope": "Production federation policy/discovery/launcher and two Service TestHosts; native mpv video output",
        "limitations": ["Pinned mpv development build; not VLC/IINA or every player version",
                        "Synthetic 120-second uncompressed AVI over loopback; not physical weak networking",
                        "MPV_HOME supplies bounded cache/IPC/proxy fixture settings; not normal user preferences",
                        "Video screenshots verify decoded frames, not physical display presentation"]}
    children, streams, ipc, trap, observer, native_created = [], [], None, None, None, False
    cleanup_errors = []
    try:
        video = work / "fixture.avi"
        report["video"] = make_video(video)
        observer = updates.ProcessObserver([executable]).__enter__()
        require(not observer.current(executable), "Pinned player already runs")
        trap = ProxyTrap()
        configuration = work / "mpv-config"
        configuration.mkdir()
        address = (r"\\.\pipe\bakabase-player-" + uuid.uuid4().hex) if os.name == "nt" else str(work / "mpv.sock")
        (configuration / "mpv.conf").write_text("\n".join([
            "input-ipc-server=" + address, "idle=yes", "keep-open=yes", "force-window=yes", "ao=null",
            "vo=gpu-next", "hwdec=no", "cache=yes", "demuxer-max-bytes=1MiB", "demuxer-readahead-secs=1",
            "http-proxy=" + trap.origin, "save-position-on-quit=no", "load-scripts=no", "osc=no",
            "log-file=" + str(work / "mpv.log"), "msg-level=all=warn"]) + "\n")
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
        ipc = IPC(address).connect(time.monotonic() + 20)
        wait(lambda: trap.hits > 0, 15, "Configured player proxy was not exercised by its control")
        report["proxyControlHits"] = trap.hits
        ipc.command("quit")
        control.wait(timeout=10)
        ipc.close()
        ipc = None
        wait(lambda: not observer.current(executable), 5, "Control player did not exit")
        if os.name != "nt":
            Path(address).unlink(missing_ok=True)

        dll = ROOT / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
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
        require(ipc.get("video-params/w") == 96 and ipc.get("video-params/h") == 64 and
                ipc.get("current-vo") not in (None, "null", "image", "tct", "caca"), "A real video output was not configured")
        report["videoOutput"] = ipc.get("current-vo")
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
        # Disable browsing while playback is paused, then force an uncached seek.
        # An old ticket cannot resume reading through a newly issued request.
        api(reader["origin"], "/federation/local/peers/browsing", "PUT", {"enabled": False})
        ipc.command("seek", 55, "absolute+exact")
        ipc.command("set_property", "pause", False)
        time.sleep(3)
        position = ipc.get("time-pos")
        stopped = ipc.get("eof-reached") or ipc.get("idle-active")
        require(stopped is True or position is None, "Disabled browsing left remote media playing after an uncached seek")
        report["browsingCancellation"] = {"eofOrIdle": stopped, "positionAvailable": position is not None, "passed": True}
        require(trap.hits == trap_before, "Production media was sent through the configured proxy")
        report["productionProxyHits"] = trap.hits - trap_before
        ipc.command("quit")
        wait(lambda: not observer.current(executable), 10, "Production player did not exit")
        ipc.close()
        ipc = None
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
        if native_created:
            cleanup("stop-owned-player", lambda: base.stop_native(executable))
        if ipc:
            cleanup("close-player-ipc", ipc.close)
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
        for path in work.glob("*.log"):
            def retain_log(path=path):
                with path.open("rb") as source:
                    source.seek(max(0, path.stat().st_size - 128 * 1024))
                    (results / path.name).write_text(redact(source.read().decode("utf-8", errors="replace")))
            cleanup("retain-sanitized-log", retain_log)
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
