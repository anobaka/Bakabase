#!/usr/bin/env python3
"""Bounded two-process SQLite/HTTP federation benchmark using production TestHost.

Build Bakabase.Federation.TestHost first. No production budgets are changed. Data is
always temporary and removed; only logs and measurement JSON are retained. Cold
means a fresh process after seeding, NOT a flushed OS filesystem cache. A transparent
loopback counting proxy adds overhead to the A -> B link and gates one response for
the cancellation check. Byte counts are HTTP payload bytes, excluding headers/TCP.
"""
import argparse
from collections import Counter
import concurrent.futures
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import platform
import re
import shutil
import socket
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


class Meter:
    def __init__(self):
        self.lock = threading.Lock()
        self.values = Counter()

    def add(self, method, path, status, sent, received):
        path = re.sub(r"/queries/[^/?]+", "/queries/{id}", path.split("?")[0])
        with self.lock:
            self.values["requests"] += 1
            self.values["requestPayloadBytes"] += sent
            self.values["responsePayloadBytes"] += received
            self.values[f"route:{method} {path}"] += 1
            self.values[f"status:{status}"] += 1

    def snapshot(self):
        with self.lock:
            return self.values.copy()

    def delta(self, before):
        with self.lock:
            return dict(self.values - before)


class LinkProxy:
    def __init__(self, upstream_port):
        self.meter = Meter()
        self.gated = threading.Event()
        self.release = threading.Event()
        self.arm = False
        proxy = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *_):
                pass

            def dispatch(self):
                length = int(self.headers.get("Content-Length", 0))
                if length > 2 * 1024 * 1024:
                    self.send_error(413)
                    return
                body = self.rfile.read(length)
                headers = {key: value for key, value in self.headers.items()
                           if key.lower() not in ("host", "connection", "transfer-encoding")}
                headers["Host"] = f"127.0.0.1:{upstream_port}"
                connection = http.client.HTTPConnection("127.0.0.1", upstream_port, timeout=12)
                status, content, response_headers = 502, b"", []
                try:
                    connection.request(self.command, self.path, body=body, headers=headers)
                    response = connection.getresponse()
                    status, response_headers = response.status, response.getheaders()
                    content = response.read(8 * 1024 * 1024 + 1)
                    if len(content) > 8 * 1024 * 1024:
                        raise RuntimeError("Benchmark proxy response exceeded its fixed 8 MiB limit")
                    proxy.meter.add(self.command, self.path, status, len(body), len(content))
                    if proxy.arm and self.path.endswith("/validate"):
                        proxy.arm = False
                        proxy.gated.set()
                        if not proxy.release.wait(12):
                            raise TimeoutError("Cancellation response gate was not released")
                    self.send_response(status)
                    for key, value in response_headers:
                        if key.lower() not in ("transfer-encoding", "connection", "content-length"):
                            self.send_header(key, value)
                    self.send_header("Content-Length", str(len(content)))
                    self.send_header("Connection", "close")
                    self.end_headers()
                    self.wfile.write(content)
                except (BrokenPipeError, ConnectionResetError):
                    pass  # Expected when the cancellation check aborts a waiting read.
                except (OSError, http.client.HTTPException):
                    proxy.meter.add(self.command, self.path, 502, len(body), 0)
                    try:
                        self.send_error(502)
                    except (BrokenPipeError, ConnectionResetError):
                        pass
                finally:
                    connection.close()
                    self.close_connection = True

            do_GET = do_POST = do_PUT = do_DELETE = dispatch

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f"http://127.0.0.1:{self.server.server_port}"

    def close(self):
        self.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


class RssSampler:
    def __init__(self, pids):
        self.pids = pids
        self.stop = threading.Event()
        self.lock = threading.Lock()
        self.samples = []
        self.thread = threading.Thread(target=self.sample, daemon=True)
        self.thread.start()

    def sample(self):
        while not self.stop.is_set():
            result = subprocess.run(["ps", "-o", "pid=,rss=", "-p", ",".join(map(str, self.pids))],
                                    capture_output=True, text=True, timeout=3)
            values = {int(parts[0]): int(parts[1]) * 1024 for line in result.stdout.splitlines()
                      if len(parts := line.split()) == 2}
            with self.lock:
                self.samples.append((time.monotonic(), values))
            self.stop.wait(.25)

    def summarize(self, since):
        with self.lock:
            values = [value for at, value in self.samples if at >= since]
        return {str(pid): {"firstRssBytes": next((v[pid] for v in values if pid in v), None),
                           "sampledPeakRssBytes": max((v[pid] for v in values if pid in v), default=None),
                           "lastRssBytes": next((v[pid] for v in reversed(values) if pid in v), None)}
                for pid in self.pids}

    def latest(self):
        with self.lock:
            return {str(pid): value for pid, value in self.samples[-1][1].items()} if self.samples else {}

    def close(self):
        self.stop.set()
        self.thread.join(timeout=5)


class Api:
    def __init__(self, deadline):
        self.deadline = deadline
        self.meter = Meter()

    def request(self, base, path, method="GET", body=None, expected=200):
        remaining = min(20, self.deadline - time.monotonic())
        if remaining <= 0:
            raise TimeoutError("Benchmark overall deadline exceeded")
        data = None if body is None else json.dumps(body, separators=(",", ":")).encode()
        request = urllib.request.Request(base + path, data=data, method=method,
                                         headers={"Content-Type": "application/json"})
        started = time.monotonic()
        try:
            response = urllib.request.urlopen(request, timeout=remaining)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            content = response.read(8 * 1024 * 1024 + 1)
            if len(content) > 8 * 1024 * 1024:
                raise AssertionError("Client response exceeded the benchmark body bound")
            self.meter.add(method, path, response.status, len(data or b""), len(content))
            allowed = (expected,) if isinstance(expected, int) else expected
            if response.status not in allowed:
                raise AssertionError(f"{method} {path}: {response.status}: {content[:700]!r}")
            return (json.loads(content) if content else None), (time.monotonic() - started) * 1000


def percentile(values, fraction):
    return sorted(values)[int((len(values) - 1) * fraction)] if values else None


def run(args):
    repo = Path(__file__).resolve().parents[3]
    dll = repo / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
    if not dll.exists():
        raise SystemExit("Build Bakabase.Federation.TestHost first")
    results = args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-federation-benchmark-results-"))
    results.mkdir(parents=True, exist_ok=True)
    root = Path(tempfile.mkdtemp(prefix="bakabase-federation-benchmark-data-"))
    api = Api(time.monotonic() + args.timeout)
    report = {"os": platform.platform(), "cpuCount": os.cpu_count(), "sdk": subprocess.check_output(
        [args.dotnet, "--version"], text=True).strip(), "gitHead": subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(), "scales": [],
        "conditions": ["Debug production TestHost; unchanged FederationQueryLimits",
                       "Process-cold restart after seeding; OS filesystem cache is not flushed",
                       "Warm run follows release in the same processes; no forced GC",
                       "A includes its own SQLite library and reads B through a counting loopback HTTP proxy",
                       "Proxy buffering/connections and RSS sampling add measurement overhead",
                       "HTTP bytes count payloads only; memory is sampled RSS, not managed live heap",
                       args.concurrent_load]}
    if platform.system() == "Darwin":
        report["cpu"] = subprocess.check_output(["sysctl", "-n", "machdep.cpu.brand_string"], text=True).strip()
        report["physicalMemoryBytes"] = int(subprocess.check_output(["sysctl", "-n", "hw.memsize"], text=True))
    all_processes, logs = [], []

    def start(label, directory, port, count):
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "ready").unlink(missing_ok=True)
        log = (results / f"{label}.log").open("w")
        logs.append(log)
        process = subprocess.Popen([args.dotnet, str(dll), str(port), str(directory), str(count)],
                                   cwd=repo, stdout=log, stderr=subprocess.STDOUT)
        all_processes.append(process)
        began = time.monotonic()
        while not (directory / "ready").exists():
            if process.poll() is not None:
                raise AssertionError(f"{label} exited; inspect retained host log")
            if time.monotonic() > min(began + 180, api.deadline):
                raise TimeoutError(f"{label} startup exceeded its bound")
            time.sleep(.2)
        return process, round((time.monotonic() - began) * 1000, 2)

    def stop(process):
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

    def query(base, ids, page_size=200):
        return api.request(base, "/federation/local/queries", "POST",
                           {"nodeIds": ids, "pageSize": page_size, "query": {}})

    def release(base, page):
        return api.request(base, f'/federation/local/queries/{page["sessionId"]}', "DELETE", expected=204)

    def page_path(page):
        return f'/federation/local/queries/{page["sessionId"]}/pages?cursor=' + urllib.parse.quote(page["nextCursor"])

    try:
        for count in args.counts:
            print(f"Preparing two libraries with {count} resources each", flush=True)
            scale = {"resourcesPerNode": count, "expectedTotal": count * 2, "runs": []}
            report["scales"].append(scale)
            directories = [root / str(count) / label for label in ("a", "b")]
            ports = [free_port(), free_port()]
            bases = [f"http://127.0.0.1:{port}" for port in ports]
            proxy = LinkProxy(ports[1])
            sampler = None
            processes = []
            try:
                # Seed sequentially to avoid measuring simultaneous EF tracking allocations.
                for index in range(2):
                    process, elapsed = start(f"{count}-{index}-seed", directories[index], ports[index], count)
                    processes.append(process)
                    scale[f"seed{index}StartupMs"] = elapsed
                statuses = [api.request(base, "/federation/local/peers")[0] for base in bases]
                ids = [status["identity"]["nodeId"] for status in statuses]
                invitation = api.request(bases[1], "/federation/local/peers/invite", "POST")[0]
                outcome = api.request(bases[0], "/federation/local/peers/connect", "POST",
                                      {"address": proxy.base, "code": invitation["code"]})[0]
                assert outcome["outcome"] == "granted"
                api.request(bases[0], "/federation/local/peers/browsing", "PUT", {"enabled": True})
                for process in processes:
                    stop(process)
                processes.clear()
                for index in range(2):
                    process, elapsed = start(f"{count}-{index}-measured", directories[index], ports[index], count)
                    processes.append(process)
                    scale[f"restart{index}StartupMs"] = elapsed
                sampler = RssSampler([process.pid for process in processes])
                time.sleep(.3)
                for temperature in ("process-cold", "warm"):
                    began = time.monotonic()
                    rss_before = sampler.latest()
                    before_ui, before_link = api.meter.snapshot(), proxy.meter.snapshot()
                    page, first_ms = query(bases[0], ids)
                    assert page["coverageComplete"] and page["totalWithinParticipants"] == count * 2, page.get("omittedNodes")
                    first = page
                    seen, previous, page_times, pages = set(), None, [], 0
                    while True:
                        pages += 1
                        for item in page["items"]:
                            ref = item["ref"]
                            identity = (ref["nodeId"], ref["libraryEpoch"], ref["resourceId"])
                            key = (item["normalizedSortKey"], *identity)
                            assert identity not in seen and (previous is None or previous < key)
                            seen.add(identity)
                            previous = key
                        if not page["nextCursor"]:
                            break
                        page, elapsed = api.request(bases[0], page_path(page))
                        page_times.append(elapsed)
                    traversal_ms = (time.monotonic() - began) * 1000
                    assert len(seen) == count * 2
                    _, release_ms = release(bases[0], first)
                    time.sleep(.3)
                    measured = {"temperature": temperature, "firstPageMs": round(first_ms, 3),
                                "traversalMs": round(traversal_ms, 3), "pages": pages, "resources": len(seen),
                                "pageP50Ms": percentile(page_times, .5), "pageP95Ms": percentile(page_times, .95),
                                "pageMaxMs": max(page_times, default=0), "releaseMs": release_ms,
                                "uiHttp": api.meter.delta(before_ui), "nodeHttp": proxy.meter.delta(before_link),
                                "rssBeforeBytes": rss_before, "rssAfterReleaseBytes": sampler.latest(),
                                "memory": sampler.summarize(began)}
                    scale["runs"].append(measured)
                    print(json.dumps({"count": count, **measured}), flush=True)
                    (results / "result.json").write_text(json.dumps(report, indent=2))

                # Cancel a real in-flight page after B has validated it, while the response
                # is gated. Existing known source snapshots must be explicitly released.
                began = time.monotonic()
                before_link = proxy.meter.snapshot()
                page, _ = query(bases[0], ids, 50)
                time.sleep(.3)
                rss_before_disable = sampler.latest()
                old_path = page_path(page)
                proxy.arm = True
                proxy.gated.clear()
                proxy.release.clear()
                with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
                    pending = executor.submit(api.request, bases[0], old_path)
                    assert proxy.gated.wait(5), "The in-flight validation did not reach the response gate"
                    _, disable_ms = api.request(bases[0], "/federation/local/peers/browsing", "PUT", {"enabled": False})
                    proxy.release.set()
                    try:
                        pending.result(timeout=5)
                        raise AssertionError("A cancelled page unexpectedly succeeded")
                    except (OSError, http.client.HTTPException) as error:
                        cancelled_as = type(error).__name__
                disabled, _ = api.request(bases[0], old_path, expected=403)
                assert disabled["code"] == "BrowsingDisabled"
                api.request(bases[0], "/federation/local/peers/browsing", "PUT", {"enabled": True})
                expired, _ = api.request(bases[0], old_path, expected=410)
                assert expired["code"] == "QuerySessionExpired"
                time.sleep(.3)
                rss_after_disable = sampler.latest()
                # Both per-owner/per-grant slots must be available again without waiting
                # for the 5/10-minute TTL, not merely enough room for one lucky new query.
                active = [query(bases[0], ids)[0], query(bases[0], ids)[0]]
                assert all(p["coverageComplete"] and p["totalWithinParticipants"] == count * 2 for p in active)
                for current in active:
                    release(bases[0], current)
                time.sleep(.5)
                cancellation = {"cancelledAs": cancelled_as, "disableMs": disable_ms,
                                "oldSessionAfterEnable": expired["code"], "bothQuotaSlotsReusable": True,
                                "rssBeforeDisableBytes": rss_before_disable,
                                "rssAfterDisableBytes": rss_after_disable,
                                "rssAfterReusingAndReleasingBothSlotsBytes": sampler.latest(),
                                "nodeHttp": proxy.meter.delta(before_link), "memory": sampler.summarize(began)}
                assert cancellation["nodeHttp"].get("route:DELETE /federation/v1/export/queries/{id}", 0) == 3
                scale["cancellation"] = cancellation
                print(json.dumps({"count": count, "cancellation": cancellation}), flush=True)
                scale["passed"] = True
            except Exception as error:
                scale["passed"] = False
                scale["error"] = f"{type(error).__name__}: {error}"
                print(json.dumps({"count": count, "error": scale["error"]}), flush=True)
            finally:
                if sampler:
                    sampler.close()
                proxy.close()
                for process in processes:
                    stop(process)
                (results / "result.json").write_text(json.dumps(report, indent=2))
        report["passed"] = all(scale.get("passed") for scale in report["scales"])
    finally:
        for process in all_processes:
            stop(process)
        for log in logs:
            log.close()
        shutil.rmtree(root)
        report["fixtureDataRemoved"] = not root.exists()
        (results / "result.json").write_text(json.dumps(report, indent=2))
        print(f"Retained benchmark results: {results}", flush=True)
    return 0 if report.get("passed") else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--counts", type=int, nargs="+", default=[10000, 100000])
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--results-directory", type=Path)
    parser.add_argument("--concurrent-load", default="Other system load was not controlled")
    args = parser.parse_args()
    if any(count < 1 or count > 100000 for count in args.counts):
        parser.error("Each library must contain 1..100000 resources; production budgets remain unchanged")
    raise SystemExit(run(args))
