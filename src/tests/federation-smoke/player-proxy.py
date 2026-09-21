#!/usr/bin/env python3
"""Verify real VLC pause/resume and Range seek using only owned loopback fixtures.

No installation, system proxy changes, real media tickets, user playlists or player
settings changes. Native HTTP is the product strategy; on macOS with an HTTP proxy
configured the product must exclude VLC before launch. The diagnostic AVIO option
reproduces the old regression and MUST fail the pause assertion on VLC 3.0.23.

The WAV has a virtual size of 345,600,044 bytes, with 4 MiB/s throttling and a 32 MiB
transfer budget. It is never written to disk. A seek must go beyond all bytes sent,
so a small file fully read by the player cannot produce a false positive.

  python3 player-proxy.py --vlc /Applications/VLC.app/Contents/MacOS/VLC \
    --results-directory /tmp/bakabase-player-proxy-results
"""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import queue
import re
import struct
import subprocess
import threading
import time

DURATION = 3600
BYTE_RATE = 48000 * 2
TOTAL_BYTES = 44 + DURATION * BYTE_RATE
TRANSFER_LIMIT = 32 * 1024 * 1024
HEADER = struct.pack("<4sI4s4sIHHIIHH4sI", b"RIFF", TOTAL_BYTES - 8, b"WAVE", b"fmt ",
                     16, 1, 1, 48000, BYTE_RATE, 2, 16, b"data", TOTAL_BYTES - 44)


class Fixture:
    def __init__(self, media=False):
        self.requests = []
        self.bytes_sent = 0
        self.budget_exceeded = False
        self.lock = threading.Lock()
        self.stopping = threading.Event()
        fixture = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                raw_range = self.headers.get("Range")
                start = int(raw_range.split("=", 1)[1].split("-", 1)[0]) if raw_range else 0
                record = {"path": self.path, "range": raw_range, "offset": start, "bytes": 0}
                with fixture.lock:
                    fixture.requests.append(record)
                if not media:
                    self.send_response(503)
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                if not 0 <= start < TOTAL_BYTES:
                    self.send_error(416)
                    return
                self.send_response(206 if raw_range else 200)
                self.send_header("Content-Type", "audio/wav")
                self.send_header("Accept-Ranges", "bytes")
                self.send_header("Content-Length", str(TOTAL_BYTES - start))
                if raw_range:
                    self.send_header("Content-Range", f"bytes {start}-{TOTAL_BYTES - 1}/{TOTAL_BYTES}")
                self.end_headers()
                try:
                    position = start
                    while position < TOTAL_BYTES and not fixture.stopping.is_set():
                        size = min(16384, TOTAL_BYTES - position)
                        with fixture.lock:
                            if fixture.bytes_sent + size > TRANSFER_LIMIT:
                                fixture.budget_exceeded = True
                                return
                            fixture.bytes_sent += size
                            record["bytes"] += size
                        chunk = HEADER[position:min(44, position + size)] if position < 44 else b""
                        self.wfile.write(chunk + bytes(size - len(chunk)))
                        self.wfile.flush()
                        position += size
                        fixture.stopping.wait(size / (4 * 1024 * 1024))
                except (BrokenPipeError, ConnectionResetError):
                    pass

            def log_message(self, *_):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    @property
    def origin(self):
        return f"http://localhost:{self.server.server_port}"

    def snapshot(self):
        with self.lock:
            return {"requests": [dict(r) for r in self.requests], "bytesSent": self.bytes_sent,
                    "budgetExceeded": self.budget_exceeded}

    def clear(self):
        with self.lock:
            self.requests.clear()
            self.bytes_sent = 0
            self.budget_exceeded = False

    def close(self):
        self.stopping.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)


class RemoteControl:
    def __init__(self, process, log):
        self.process = process
        self.lines = queue.Queue()
        self.log = log
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.reader.start()

    def read(self):
        for line in self.process.stdout:
            try:
                self.log.write(line)
                self.log.flush()
            except ValueError:
                pass  # Failure cleanup may close the log before the owned child exits.
            self.lines.put(line.strip().lstrip("> "))

    def send(self, command):
        self.process.stdin.write(command + "\n")
        self.process.stdin.flush()

    def query(self, command, match):
        while not self.lines.empty():
            self.lines.get_nowait()
        self.send(command)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            try:
                line = self.lines.get(timeout=0.1)
            except queue.Empty:
                if self.process.poll() is not None:
                    break
                continue
            value = match(line)
            if value is not None:
                return value
        raise AssertionError(f"VLC did not answer {command}; inspect its log")

    def time(self):
        return self.query("get_time", lambda line: int(line) if line.isdigit() else None)

    def state(self):
        return self.query("status", lambda line: (m.group(1) if
                          (m := re.fullmatch(r"\( state ([a-z]+) \)", line)) else None))


def wait_until(predicate, process, timeout=10):
    deadline = time.monotonic() + timeout
    while not predicate():
        if process.poll() is not None or time.monotonic() >= deadline:
            raise AssertionError("VLC did not make the expected direct Range request; inspect its log")
        time.sleep(0.05)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--vlc", required=True, type=Path)
    parser.add_argument("--results-directory", required=True, type=Path)
    parser.add_argument("--diagnose-avio", action="store_true",
                        help="Reproduce the rejected AVIO strategy; VLC 3.0.23 is expected to FAIL pause validation")
    args = parser.parse_args()
    vlc = args.vlc.resolve(strict=True)
    output = args.results_directory.resolve()
    output.mkdir(parents=True, exist_ok=True)
    media, proxy = Fixture(media=True), Fixture()
    url = media.origin + "/federation/local/media/" + "a" * 64
    inherited = dict(os.environ)
    for name in ("http_proxy", "https_proxy", "all_proxy", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
        inherited[name] = proxy.origin
    for name in ("no_proxy", "NO_PROXY"):
        inherited.pop(name, None)
    # Remove the fixture's inherited proxy for this owned child. This does not alter
    # OS or player settings: Darwin/Windows/libproxy may still select a proxy and the
    # native test must then fail. Never claim no_proxy guarantees VLC bypasses it.
    direct = dict(inherited)
    for name in ("http_proxy", "https_proxy", "all_proxy", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
        direct.pop(name, None)
    direct["no_proxy"] = direct["NO_PROXY"] = "localhost,127.0.0.1,::1"
    common = [str(vlc), "--ignore-config", "--no-media-library", "--no-metadata-network-access", "-vv"]
    result = {"vlc": str(vlc), "systemProxyChanged": False, "passed": False,
              "strategy": "rejected-avio" if args.diagnose_avio else "native-http",
              "logicalMediaBytes": TOTAL_BYTES, "transferBudgetBytes": TRANSFER_LIMIT}
    process = None
    control = None
    try:
        # Positive control: a fake ticket reaches the inherited proxy with AVIO's
        # ordinary settings. A zero player exit code alone does not prove playback.
        with (output / "inherited-proxy.log").open("w") as log:
            process = subprocess.Popen(common + ["-I", "dummy", "--play-and-exit", "--no-audio",
                                                "--no-video", "avio://" + url],
                                       env=inherited, stdout=log, stderr=subprocess.STDOUT)
            process.wait(timeout=15)
        result["inheritedProxy"] = {"proxy": proxy.snapshot(), "media": media.snapshot()}
        assert proxy.snapshot()["requests"] and not media.snapshot()["requests"], "Proxy challenge was ineffective"
        proxy.clear()
        media.clear()
        target = ["avio://" + url, ":avio-options={http_proxy=direct://}"] if args.diagnose_avio else [url]
        with (output / "direct-pause-seek.log").open("w") as log, (output / "rc.log").open("w") as rc_log:
            process = subprocess.Popen(common + ["-I", "rc", "--rc-fake-tty", "--aout=adummy", "--no-video"] + target,
                                       env=inherited if args.diagnose_avio else direct,
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log,
                                       text=True, bufsize=1)
            control = RemoteControl(process, rc_log)
            wait_until(lambda: bool(media.snapshot()["requests"]), process)
            time.sleep(2)
            result["initialTime"] = control.time()
            assert control.state() == "playing"
            control.send("pause")
            time.sleep(0.6)
            result["pauseState"] = control.state()
            result["pauseTimeStart"] = control.time()
            result["pauseBytesStart"] = media.snapshot()["bytesSent"]
            time.sleep(2.2)
            result["pauseTimeEnd"] = control.time()
            result["pauseBytesEnd"] = media.snapshot()["bytesSent"]
            assert result["pauseState"] == "paused", "Player did not enter paused state"
            assert result["pauseTimeStart"] == result["pauseTimeEnd"], "VLC reports paused but playback time keeps advancing"
            control.send("pause")
            time.sleep(2.2)
            result["resumedTime"] = control.time()
            assert control.state() == "playing" and result["resumedTime"] > result["pauseTimeEnd"], "Playback did not resume"
            result["bytesBeforeSeek"] = media.snapshot()["bytesSent"]
            seek_second = 2700
            expected_offset = 44 + seek_second * BYTE_RATE
            assert expected_offset > result["bytesBeforeSeek"], "Fixture cannot distinguish network seek from buffered seek"
            control.send(f"seek {seek_second}")
            wait_until(lambda: any(r["offset"] >= expected_offset - BYTE_RATE for r in media.snapshot()["requests"]), process)
            time.sleep(1.2)
            result["seekTime"] = control.time()
            assert seek_second - 1 <= result["seekTime"] <= seek_second + 5, "Range request did not seek actual playback"
            control.send("quit")
            process.wait(timeout=5)
            control.reader.join(timeout=2)
        assert process.returncode == 0 and not proxy.snapshot()["requests"], "Playback used a proxy or failed"
        assert not media.snapshot()["budgetExceeded"], "Media transfer exceeded the fixture budget"
        result["passed"] = True
    except Exception as error:
        result["error"] = str(error)
        raise
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait(timeout=5)
        if control is not None:
            control.reader.join(timeout=2)
        result["direct"] = {"proxy": proxy.snapshot(), "media": media.snapshot()}
        media.close()
        proxy.close()
        result["ownedProcessesStopped"] = process is None or process.poll() is not None
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
