#!/usr/bin/env python3
"""Real paired Service hosts behind an owned, bounded loopback fault relay.

Pass a synthetic video (at least 8 MiB); never use a user's media library. This
checks decoded browser frames, slow streams, cancellation, interrupted reads,
idle timeout and revocation. It is network fault injection, not a physical LAN.
"""
import argparse
from contextlib import closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import http.client
import importlib.util
import json
import os
from pathlib import Path
import select
import signal
import shutil
import socket
import sqlite3
import subprocess
import tempfile
import threading
import time
import urllib.parse

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("smoke", Path(__file__).with_name("run.py"))
smoke = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(smoke)


class FaultRelay:
    """Forwards only to its fixed fixture host; never stores auth headers/tickets."""
    def __init__(self, target_port):
        self.lock = threading.Lock()
        self.stop = threading.Event()
        self.mode = "slow"
        self.records = []
        owner = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def wait(self, seconds):
                until = time.monotonic() + seconds
                while not owner.stop.wait(min(0.02, max(0, until - time.monotonic()))):
                    if select.select([self.connection], [], [], 0)[0]:
                        if not self.connection.recv(1, socket.MSG_PEEK):
                            raise ConnectionResetError("Fixture reader disconnected")
                    if time.monotonic() >= until:
                        return
                raise ConnectionResetError("Fixture relay stopped")

            def forward(self):
                length = int(self.headers.get("Content-Length", "0"))
                if length > 1024 * 1024 or not self.path.startswith("/federation/"):
                    self.send_error(400)
                    return
                data = self.rfile.read(length) if length else None
                headers = {k: v for k, v in self.headers.items()
                           if k.lower() not in {"host", "connection", "transfer-encoding"}}
                headers["Host"] = f"127.0.0.1:{target_port}"
                upstream = http.client.HTTPConnection("127.0.0.1", target_port, timeout=8)
                streaming = self.command == "GET" and "/export/assets/" in self.path
                record = None
                if streaming:
                    with owner.lock:
                        mode = owner.mode
                        record = {"mode": mode, "range": self.headers.get("Range"),
                                  "startedMs": round(time.time() * 1000), "bytes": 0, "active": True}
                        owner.records.append(record)
                try:
                    upstream.request(self.command, self.path, data, headers)
                    response = upstream.getresponse()
                    self.send_response(response.status)
                    for key, value in response.getheaders():
                        if key.lower() not in {"connection", "transfer-encoding", "server", "date"}:
                            self.send_header(key, value)
                    self.send_header("Connection", "close")
                    self.end_headers()
                    if self.command == "HEAD":
                        return
                    while not owner.stop.is_set():
                        if record and record["bytes"] >= 65536 and mode == "stall":
                            self.wait(40)  # Longer than the product's 30-second idle deadline.
                        if record and record["bytes"] >= 131072 and mode == "drop":
                            return  # Deliberate incomplete Content-Length response.
                        chunk = response.read(16384)
                        if not chunk:
                            return
                        if record:
                            self.wait(len(chunk) / (256 * 1024))
                        self.wfile.write(chunk)
                        self.wfile.flush()
                        if record:
                            with owner.lock:
                                record["bytes"] += len(chunk)
                except (OSError, http.client.HTTPException):
                    pass  # Disconnects are injected/observed through the consumer's assertions.
                finally:
                    self.close_connection = True
                    upstream.close()
                    if record:
                        with owner.lock:
                            record["active"] = False
                            record["endedMs"] = round(time.time() * 1000)

            do_GET = do_POST = do_PUT = do_DELETE = do_HEAD = forward

            def log_message(self, *_):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    @property
    def base(self):
        return f"http://127.0.0.1:{self.server.server_port}"

    def idle(self, timeout=5):
        until = time.monotonic() + timeout
        while time.monotonic() < until:
            with self.lock:
                if not any(r["active"] for r in self.records):
                    return
            time.sleep(0.05)
        raise AssertionError("Cancelled media request still occupies the upstream relay")

    def close(self):
        self.stop.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--node", default="node")
    parser.add_argument("--media", type=Path, required=True)
    parser.add_argument("--results-directory", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=240)
    args = parser.parse_args()
    if args.timeout < 1:
        parser.error("--timeout must be positive")
    media = args.media.resolve()
    if not media.is_file() or not 8 * 1024 * 1024 <= media.stat().st_size <= 128 * 1024 * 1024:
        parser.error("Use an existing synthetic video between 8 and 128 MiB (see README).")
    results = args.results_directory.resolve()
    results.mkdir(parents=True, exist_ok=True)
    fixture = Path(tempfile.mkdtemp(prefix="bakabase-media-stream-"))
    processes, streams, relay = [], [], None
    report = {"passed": False, "scope": "two production hosts, loopback rate/failure injection",
              "mediaBytes": media.stat().st_size, "relayBytesPerSecond": 256 * 1024}
    deadline = time.monotonic() + args.timeout
    smoke.REQUEST_DEADLINE = deadline
    browser_process = None

    def stop_process(process):
        if process is browser_process:
            if os.name == "posix":
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
            elif process.poll() is None:
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=8)
        elif process.poll() is None:
            process.terminate()

    def expire():
        report["deadlineExceeded"] = True
        if relay:
            relay.stop.set()
        for process in processes:
            stop_process(process)

    watchdog = threading.Timer(args.timeout, expire)
    watchdog.daemon = True
    watchdog.start()
    try:
        nodes = []
        for role in ("reader", "source"):
            port = smoke.free_port()
            log = (fixture / (role + ".log")).open("w")
            streams.append(log)
            directory = fixture / role
            env = {**os.environ, "BAKABASE_FEDERATION_TEST_WEB_ROOT": str(ROOT / "src/web/dist"),
                   "BAKABASE_FEDERATION_TEST_MEDIA_FILE": str(media), "DOTNET_ENVIRONMENT": "Development"}
            dll = ROOT / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
            process = subprocess.Popen([args.dotnet, str(dll), str(port), str(directory), "3"],
                                       cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
            processes.append(process)
            nodes.append({"base": f"http://127.0.0.1:{port}", "directory": directory, "port": port})
        until = min(deadline, time.monotonic() + 90)
        while not all((n["directory"] / "ready").exists() for n in nodes):
            if any(p.poll() is not None for p in processes) or time.monotonic() >= until:
                raise AssertionError("Fixture startup failed")
            time.sleep(0.1)
        reader, source = nodes
        relay = FaultRelay(source["port"])
        api = smoke.request
        for node in nodes:
            node["state"] = api(node["base"], "/federation/local/peers")
        api(reader["base"], "/federation/local/peers/browsing", "PUT", {"enabled": True})

        def pair():
            invite = api(source["base"], "/federation/local/peers/invite", "POST")
            result = api(reader["base"], "/federation/local/peers/connect", "POST",
                         {"address": relay.base, "code": invite["code"]})
            assert result["outcome"] == "granted"

        pair()
        identity = source["state"]["identity"]
        ref = {"nodeId": identity["nodeId"], "libraryEpoch": identity["libraryEpoch"], "resourceId": 1}

        def ticket():
            detail = api(reader["base"], "/federation/local/resources/resolve", "POST", {"refs": [ref]})["resources"][0]
            assert detail["assets"][0]["kind"] == "video"
            value = api(reader["base"], "/federation/local/playback-sessions", "POST",
                        {"assetRef": {"resourceRef": ref, "assetId": detail["assets"][0]["assetId"]}, "mode": "preview"})
            return urllib.parse.urlsplit(value["url"]).path

        config = fixture / "browser.json"
        config.write_text(json.dumps({"base": reader["base"], "ref": ref, "results": str(results)}))
        browser_script = ROOT / "src/tests/federation-browser-smoke/media.cjs"
        with (results / "browser.log").open("w") as log:
            browser_process = subprocess.Popen([args.node, str(browser_script), str(config)], cwd=ROOT,
                                               stdout=log, stderr=subprocess.STDOUT,
                                               start_new_session=os.name == "posix")
            processes.append(browser_process)
            code = browser_process.wait(timeout=min(60, max(1, deadline - time.monotonic())))
            if code:
                raise AssertionError(f"Video browser checks failed ({code}); inspect browser.log")
        relay.idle()
        browser_result = json.loads((results / "browser-result.json").read_text())
        assert browser_result["passed"]
        seek_requests = [r for r in relay.records if r["startedMs"] >= browser_result["seekStartedMs"] and
                         r["range"] and int(r["range"].split("=")[1].split("-")[0]) > media.stat().st_size / 2]
        assert seek_requests, "Decoded seek did not request a later byte range from the real source"
        report["browser"] = browser_result
        print("PASS: actual presented video frames, pause/resume and remote Range seek", flush=True)

        def open_stream(path, start=0):
            connection = http.client.HTTPConnection("127.0.0.1", reader["port"], timeout=38)
            connection.request("GET", path, headers={"Range": f"bytes={start}-"})
            response = connection.getresponse()
            assert response.status == 206
            return connection, response

        def close_stream(connection, response):
            response.close()
            connection.close()
            relay.idle()

        path = ticket()
        started = time.monotonic()
        connection, response = open_stream(path)
        # Keep transferring beyond the eight-second header deadline.
        size = 2304 * 1024
        received = response.read(size)
        with media.open("rb") as expected:
            assert received == expected.read(size)
        elapsed = time.monotonic() - started
        assert elapsed > 8
        close_stream(connection, response)
        report["slowStream"] = {"bytes": size, "seconds": elapsed, "cancelReleasedUpstream": True}
        print("PASS: slow transfer outlives header deadline; reader disconnect releases upstream", flush=True)

        relay.mode = "drop"
        connection, response = open_stream(path)
        assert len(response.read(32768)) == 32768
        incomplete = False
        try:
            response.read()
        except http.client.IncompleteRead as error:
            incomplete = True
            assert 0 < len(error.partial) < media.stat().st_size
        except ConnectionResetError:
            incomplete = True  # Kestrel aborts a response whose upstream Content-Length is incomplete.
        assert incomplete, "Truncated stream was incorrectly treated as complete"
        close_stream(connection, response)
        relay.mode = "slow"
        offset = media.stat().st_size * 3 // 4
        connection, response = open_stream(path, offset)
        recovered = response.read(65536)
        with media.open("rb") as expected:
            expected.seek(offset)
            assert recovered == expected.read(65536)
        close_stream(connection, response)
        report["interruptedStream"] = {"incompleteDetected": True, "freshRangeRecovered": True, "offset": offset}
        print("PASS: incomplete transfer detected; fresh Range bytes match original media", flush=True)

        relay.mode = "stall"
        connection, response = open_stream(path)
        assert len(response.read(32768)) == 32768
        started = time.monotonic()
        try:
            response.read()
            raise AssertionError("Stalled stream was treated as a complete response")
        except (http.client.IncompleteRead, ConnectionResetError):
            pass
        elapsed = time.monotonic() - started
        assert 25 <= elapsed <= 36, f"Idle cancellation outside product deadline: {elapsed:.1f}s"
        close_stream(connection, response)
        report["idleTimeout"] = {"seconds": elapsed, "upstreamReleased": True}
        print("PASS: product idle timeout interrupts stalled upstream and releases it", flush=True)

        relay.mode = "slow"
        connection, response = open_stream(path)
        assert response.read(65536)
        # Local opt-out must cancel an already streaming proxy, not only new requests.
        started = time.monotonic()
        api(reader["base"], "/federation/local/peers/browsing", "PUT", {"enabled": False})
        try:
            response.read()
            raise AssertionError("Disabling browsing did not interrupt the active stream")
        except (http.client.IncompleteRead, ConnectionResetError):
            pass
        close_stream(connection, response)
        assert time.monotonic() - started < 5
        api(reader["base"], path, expected=403)
        report["browsingDisabled"] = {"activeStreamCancelled": True, "seconds": time.monotonic() - started}
        api(reader["base"], "/federation/local/peers/browsing", "PUT", {"enabled": True})
        path = ticket()
        source_state = api(source["base"], "/federation/local/peers")
        grant = next(p["inboundGrant"]["grantId"] for p in source_state["peers"]
                     if p["nodeId"] == reader["state"]["identity"]["nodeId"])
        api(source["base"], f"/federation/local/peers/grants/{grant}", "DELETE")
        api(reader["base"], path, expected=(401, 403, 409, 410))
        report["revocation"] = {"newRangeDenied": True}
        for node in nodes:
            with closing(sqlite3.connect(next(node["directory"].rglob("bakabase_insideworld*.db")))) as db:
                assert db.execute("select count(*) from ResourcesV2 where PlayedAt is not null").fetchone()[0] == 0
        report["noPlaybackHistoryWrites"] = True
        if time.monotonic() >= deadline:
            raise TimeoutError("Media validation exceeded its overall deadline")
        report["passed"] = True
        print("PASS: decoded remote video, slow stream, seek, disconnect, idle timeout and opt-out")
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        watchdog.cancel()
        if relay:
            relay.close()
            report["relay"] = relay.records
        for process in reversed(processes):
            stop_process(process)
            if process.poll() is None:
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    if process is browser_process and os.name == "posix":
                        try:
                            os.killpg(process.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                    else:
                        process.kill()
                    process.wait(timeout=5)
        for stream in streams:
            stream.close()
        report["ownedProcessesStopped"] = all(p.poll() is not None for p in processes)
        # Raw host logs contain opaque tickets. Keep only sanitized failure types.
        for role in ("reader", "source"):
            log = fixture / (role + ".log")
            if log.exists():
                import re
                report[role + "ExceptionTypes"] = sorted(set(re.findall(
                    r"\b(?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*Exception\b", log.read_text(errors="replace"))))
        shutil.rmtree(fixture)
        (results / "result.json").write_text(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
