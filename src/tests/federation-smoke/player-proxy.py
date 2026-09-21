#!/usr/bin/env python3
"""Verify the VLC controlled-loopback strategy with a real installed VLC, without GUI.

Starts only owned loopback HTTP fixtures and VLC child processes. No installation,
system proxy changes, real media tickets, user playlists or player settings changes.
The media is synthetic WAV; the proxy is a 503-returning request recorder. Requires
VLC with its standard AVIO, RC and dummy audio modules. The existing user's VLC is
never controlled. Example:
  python3 player-proxy.py --vlc /Applications/VLC.app/Contents/MacOS/VLC \
    --results-directory /tmp/bakabase-player-proxy-results
"""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import io
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import wave


class Fixture:
    def __init__(self, data=None):
        self.requests = []
        self.lock = threading.Lock()
        fixture = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                raw_range = self.headers.get("Range")
                start = int(raw_range.split("=", 1)[1].split("-", 1)[0]) if raw_range else 0
                with fixture.lock:
                    fixture.requests.append({"path": self.path, "range": raw_range, "offset": start})
                if data is None:
                    self.send_response(503)
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                self.send_response(206 if raw_range else 200)
                self.send_header("Content-Type", "audio/wav")
                self.send_header("Accept-Ranges", "bytes")
                self.send_header("Content-Length", str(len(data) - start))
                if raw_range:
                    self.send_header("Content-Range", f"bytes {start}-{len(data)-1}/{len(data)}")
                self.end_headers()
                try:
                    self.wfile.write(data[start:])
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
            return list(self.requests)

    def clear(self):
        with self.lock:
            self.requests.clear()

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)


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
    args = parser.parse_args()
    vlc = args.vlc.resolve(strict=True)
    output = args.results_directory.resolve()
    output.mkdir(parents=True, exist_ok=True)
    wav = io.BytesIO()
    with wave.open(wav, "wb") as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(48000)
        stream.writeframes(b"\0\0" * 48000 * 120)
    media, proxy = Fixture(wav.getvalue()), Fixture()
    url = media.origin + "/federation/local/media/" + "a" * 64
    env = dict(os.environ)
    for name in ("http_proxy", "https_proxy", "all_proxy", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
        env[name] = proxy.origin
    for name in ("no_proxy", "NO_PROXY"):
        env.pop(name, None)
    common = [str(vlc), "--ignore-config", "--no-media-library", "--no-metadata-network-access", "-vv"]
    result = {"vlc": str(vlc), "systemProxyChanged": False, "passed": False}
    process = None
    try:
        # Positive control: AVIO without the explicit value really sends the ticket
        # to the failing inherited proxy. Exit code alone cannot detect VLC failure.
        with (output / "inherited-proxy.log").open("wb") as log:
            process = subprocess.Popen(common + ["-I", "dummy", "--play-and-exit", "--no-audio",
                                                "--no-video", "avio://" + url],
                                       env=env, stdout=log, stderr=subprocess.STDOUT)
            process.wait(timeout=15)
        result["inheritedProxy"] = {"proxyRequests": proxy.snapshot(), "mediaRequests": media.snapshot()}
        assert proxy.snapshot() and not media.snapshot(), "The proxy challenge was not effective"
        proxy.clear()
        media.clear()
        with (output / "direct-seek.log").open("wb") as log:
            process = subprocess.Popen(common + ["-I", "rc", "--rc-fake-tty", "--aout=adummy",
                                                "--no-video", "avio://" + url,
                                                ":avio-options={http_proxy=direct://}"],
                                       env=env, stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT)
            wait_until(lambda: bool(media.snapshot()), process)
            # Wait for the decoder/RC interface to be ready after initial headers.
            time.sleep(1)
            process.stdin.write(b"seek 90\n")
            process.stdin.flush()
            wait_until(lambda: any(r["offset"] >= 8_000_000 for r in media.snapshot()), process)
            process.stdin.write(b"quit\n")
            process.stdin.flush()
            process.wait(timeout=5)
        result["directSeek"] = {"exitCode": process.returncode, "proxyRequests": proxy.snapshot(),
                                "mediaRequests": media.snapshot()}
        assert process.returncode == 0 and not proxy.snapshot(), "Playback used a proxy or failed"
        assert all(r["range"] for r in media.snapshot()), "Expected seekable Range requests"
        result["passed"] = True
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait(timeout=5)
        media.close()
        proxy.close()
        result["ownedProcessesStopped"] = process is None or process.poll() is not None
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
