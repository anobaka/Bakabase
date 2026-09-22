#!/usr/bin/env python3
"""Bounded two-process SQLite/HTTP federation benchmark using production TestHost.

Build Bakabase.Federation.TestHost first. No production budgets are changed. Data is
always temporary and removed; only logs and measurement JSON are retained. Cold
means a fresh process after seeding, NOT a flushed OS filesystem cache. A transparent
loopback counting proxy adds overhead to the A -> B link and gates one response for
the cancellation check. Byte counts are HTTP payload bytes, excluding headers/TCP.
"""
import argparse
from collections import Counter, deque
import concurrent.futures
import ctypes
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import math
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


MAX_TIMEOUT = 900
MAX_REPETITIONS = 5
RSS_INTERVAL_SECONDS = .25
FIXTURE_LABELS = ("benchmark-a", "benchmark-b")
FIXTURE_LABEL_ENVIRONMENT = "BAKABASE_FEDERATION_TEST_NODE_NAME"


class ProcessMemoryCounters(ctypes.Structure):
    # DWORD is always 32 bits; SIZE_T follows the Python process pointer width.
    _fields_ = [("cb", ctypes.c_uint32), ("PageFaultCount", ctypes.c_uint32)] + [
        (name, ctypes.c_size_t) for name in (
            "PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage",
            "QuotaPeakNonPagedPoolUsage", "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage")]


class WindowsRssReader:
    def __init__(self):
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.psapi = ctypes.WinDLL("psapi", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [ctypes.c_uint32, ctypes.c_int32, ctypes.c_uint32]
        self.kernel.OpenProcess.restype = ctypes.c_void_p
        self.kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        self.kernel.CloseHandle.restype = ctypes.c_int32
        self.psapi.GetProcessMemoryInfo.argtypes = [ctypes.c_void_p, ctypes.POINTER(ProcessMemoryCounters),
                                                  ctypes.c_uint32]
        self.psapi.GetProcessMemoryInfo.restype = ctypes.c_int32

    def __call__(self, pids):
        values = {}
        for pid in pids:
            # Read only the owned process working set, with query-limited rights.
            # https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo
            handle = self.kernel.OpenProcess(0x1000, False, pid)
            if not handle:
                raise OSError("Owned process RSS handle unavailable")
            try:
                counters = ProcessMemoryCounters()
                counters.cb = ctypes.sizeof(counters)
                if not self.psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
                    raise OSError("Owned process RSS unavailable")
                values[pid] = int(counters.WorkingSetSize)
            finally:
                self.kernel.CloseHandle(handle)
        return values


def posix_rss(pids):
    result = subprocess.run(["ps", "-o", "pid=,rss=", "-p", ",".join(map(str, pids))],
                            capture_output=True, text=True, timeout=3, check=True)
    values = {}
    for line in result.stdout.splitlines():
        parts = line.split()
        if len(parts) != 2:
            raise ValueError("Unexpected ps RSS row")
        pid, kib = map(int, parts)
        if pid not in pids or kib < 0 or pid in values:
            raise ValueError("Unexpected ps RSS identity/value")
        values[pid] = kib * 1024
    if set(values) != set(pids):
        raise ValueError("Owned process RSS sample incomplete")
    return values


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
        self.transport_lock = threading.Lock()
        self.transport = None
        self.transport_values = Counter()
        self.upstream_port = upstream_port
        self.client_lock = threading.Lock()
        self.clients = set()
        self.clients_stopped = threading.Event()
        self.clients_stopped.set()
        self.worker_slots = threading.BoundedSemaphore(4)
        self.gated = threading.Event()
        self.release = threading.Event()
        self.arm = False
        proxy = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def setup(self):
                super().setup()
                self.connection.settimeout(12)
                self.connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

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
                status, content, response_headers = 502, b"", []
                stage = "upstream"
                try:
                    status, response_headers, content = proxy.forward(self.command, self.path, body, headers)
                    proxy.meter.add(self.command, self.path, status, len(body), len(content))
                    stage = "downstream"
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
                    self.end_headers()
                    self.wfile.write(content)
                except (OSError, http.client.HTTPException, RuntimeError) as error:
                    proxy.record_transport_error(stage, error)
                    self.close_connection = True
                    if stage == "upstream":
                        proxy.meter.add(self.command, self.path, 502, len(body), 0)
                        try:
                            self.send_error(502)
                        except (OSError, http.client.HTTPException):
                            pass
                    # A downstream reset/abort is expected after cancelling a read.

            do_GET = do_POST = do_PUT = do_DELETE = dispatch

        class Server(ThreadingHTTPServer):
            def process_request(self, request, address):
                if not proxy.worker_slots.acquire(blocking=False):
                    proxy.record_transport_error("accept", RuntimeError("Proxy worker bound exceeded"))
                    self.shutdown_request(request)
                    return
                with proxy.client_lock:
                    proxy.clients.add(request)
                    proxy.clients_stopped.clear()
                try:
                    super().process_request(request, address)
                except Exception:
                    proxy.finish_client(request)
                    raise

            def process_request_thread(self, request, address):
                try:
                    super().process_request_thread(request, address)
                finally:
                    proxy.finish_client(request)

        self.server = Server(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = f"http://127.0.0.1:{self.server.server_port}"

    def forward(self, method, path, body, headers):
        # Keep one bounded upstream connection. Responses are fully read before
        # releasing this lock, including before the cancellation gate is entered.
        # Do not retry a failed request: the measured production operation must fail.
        with self.transport_lock:
            if self.transport is None:
                self.transport = http.client.HTTPConnection("127.0.0.1", self.upstream_port, timeout=12)
                self.transport_values["upstreamClientsCreated"] += 1
            try:
                self.transport.request(method, path, body=body, headers=headers)
                response = self.transport.getresponse()
                content = response.read(8 * 1024 * 1024 + 1)
                if len(content) > 8 * 1024 * 1024:
                    raise RuntimeError("Benchmark proxy response exceeded its fixed 8 MiB limit")
                return response.status, response.getheaders(), content
            except Exception:
                self.transport.close()
                self.transport = None
                raise

    def reset_upstream(self):
        with self.transport_lock:
            if self.transport:
                self.transport.close()
                self.transport = None

    def record_transport_error(self, stage, error):
        code = getattr(error, "winerror", None) or getattr(error, "errno", None)
        with self.transport_lock:
            self.transport_values[f"{stage}:{type(error).__name__}"] += 1
            if type(code) is int:
                self.transport_values[f"{stage}:nativeCode:{code}"] += 1

    def diagnostics(self):
        with self.transport_lock:
            return dict(self.transport_values)

    def finish_client(self, connection):
        with self.client_lock:
            self.clients.remove(connection)
            if not self.clients:
                self.clients_stopped.set()
        self.worker_slots.release()

    def close(self):
        self.release.set()
        self.server.shutdown()
        with self.client_lock:
            clients = list(self.clients)
        for client in clients:
            try:
                client.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        self.server.server_close()
        self.thread.join(timeout=5)
        self.reset_upstream()
        if self.thread.is_alive() or not self.clients_stopped.wait(5):
            raise TimeoutError("Proxy workers did not stop")


class RssSampler:
    def __init__(self, pids):
        self.pids = pids
        self.reader = WindowsRssReader() if platform.system() == "Windows" else posix_rss
        self.source = "GetProcessMemoryInfo.WorkingSetSize" if platform.system() == "Windows" else "ps.rss.KiB"
        self.stop = threading.Event()
        self.lock = threading.Lock()
        self.samples = deque(maxlen=math.ceil(MAX_TIMEOUT / RSS_INTERVAL_SECONDS) + 10)
        self.errors = Counter()
        self.capture()
        self.thread = threading.Thread(target=self.sample, daemon=True)
        self.thread.start()

    def capture(self):
        try:
            values = self.reader(self.pids)
            if set(values) != set(self.pids) or any(value <= 0 for value in values.values()):
                raise ValueError("Owned process RSS sample incomplete")
        except (OSError, ValueError, subprocess.SubprocessError) as error:
            with self.lock:
                self.errors[type(error).__name__] += 1
            return
        with self.lock:
            self.samples.append((time.monotonic(), values))

    def sample(self):
        while not self.stop.wait(RSS_INTERVAL_SECONDS):
            self.capture()

    def summarize(self, since):
        with self.lock:
            values = [value for at, value in self.samples if at >= since]
        return {str(pid): {"sampleCount": sum(pid in v for v in values),
                           "firstRssBytes": next((v[pid] for v in values if pid in v), None),
                           "sampledPeakRssBytes": max((v[pid] for v in values if pid in v), default=None),
                           "lastRssBytes": next((v[pid] for v in reversed(values) if pid in v), None)}
                for pid in self.pids}

    def latest(self):
        with self.lock:
            return {str(pid): value for pid, value in self.samples[-1][1].items()} if self.samples else {}

    def diagnostics(self):
        with self.lock:
            return {"source": self.source, "intervalSeconds": RSS_INTERVAL_SECONDS,
                    "successfulSamples": len(self.samples), "errors": dict(self.errors)}

    def require_coverage(self):
        if set(self.latest()) != {str(pid) for pid in self.pids}:
            raise AssertionError("Required resident-memory samples are missing")

    def close(self):
        self.stop.set()
        self.thread.join(timeout=5)
        if self.thread.is_alive():
            raise TimeoutError("RSS sampler did not stop")


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
        started = time.perf_counter()
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
            return (json.loads(content) if content else None), (time.perf_counter() - started) * 1000


def percentile(values, fraction):
    # Nearest-rank, including p95=max for the deliberately small first-page sample.
    return sorted(values)[max(0, math.ceil(len(values) * fraction) - 1)] if values else None


def distribution(values):
    return {"samples": len(values), "p50": percentile(values, .5), "p95": percentile(values, .95),
            "min": min(values, default=None), "max": max(values, default=None)}


def summarize_runs(runs):
    result = {}
    for temperature in ("process-cold", "warm"):
        selected = [run for run in runs if run["temperature"] == temperature]
        result[temperature] = {
            "firstPageMs": distribution([run["firstPageMs"] for run in selected]),
            "subsequentPageMs": distribution([value for run in selected for value in run["pageLatencyMs"]]),
            "traversalMs": distribution([run["traversalMs"] for run in selected]),
            "releaseMs": distribution([run["releaseMs"] for run in selected]),
            "uiHttpTotals": dict(sum((Counter(run["uiHttp"]) for run in selected), Counter())),
            "nodeHttpTotals": dict(sum((Counter(run["nodeHttp"]) for run in selected), Counter()))}
    return result


def stop_process(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


def remove_fixture(root):
    # Windows virus scanners can briefly retain files after the owned host exits.
    for attempt in range(5):
        try:
            shutil.rmtree(root)
            return
        except FileNotFoundError:
            if not root.exists():
                return
            raise
        except PermissionError:
            if attempt == 4:
                raise
            time.sleep(.2 * (attempt + 1))


def cleanup_step(errors, label, action):
    try:
        action()
    except Exception as error:
        errors.append({"step": label, "error": type(error).__name__})


def check_deadline(deadline):
    if time.monotonic() >= deadline:
        raise TimeoutError("Benchmark overall deadline exceeded")


def traverse(api, proxy, sampler, base, ids, count, repetition, temperature):
    began = time.monotonic()
    started = time.perf_counter()
    sampler.capture()
    sampler.require_coverage()
    rss_before = sampler.latest()
    before_ui, before_link = api.meter.snapshot(), proxy.meter.snapshot()
    page, first_ms = query(api, base, ids)
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
            assert len(seen) < count * 2, "Traversal exceeded the two full fixture libraries"
            seen.add(identity)
            previous = key
        if not page["nextCursor"]:
            break
        page, elapsed = api.request(base, page_path(page))
        page_times.append(elapsed)
    traversal_ms = (time.perf_counter() - started) * 1000
    assert len(seen) == count * 2
    _, release_ms = release(api, base, first)
    time.sleep(.3)
    sampler.capture()
    memory = sampler.summarize(began)
    assert all(value["sampleCount"] > 0 for value in memory.values()), "Required phase RSS samples missing"
    return {"repetition": repetition, "temperature": temperature, "firstPageMs": round(first_ms, 3),
            "traversalMs": round(traversal_ms, 3), "pages": pages, "resources": len(seen),
            "pageLatencyMs": page_times, "pageSamples": len(page_times),
            "pageP50Ms": percentile(page_times, .5), "pageP95Ms": percentile(page_times, .95),
            "pageMaxMs": max(page_times, default=0), "releaseMs": release_ms,
            "uiHttp": api.meter.delta(before_ui), "nodeHttp": proxy.meter.delta(before_link),
            "rssBeforeBytes": rss_before, "rssAfterReleaseBytes": sampler.latest(), "memory": memory}


def query(api, base, ids, page_size=200):
    return api.request(base, "/federation/local/queries", "POST",
                       {"nodeIds": ids, "pageSize": page_size, "query": {}})


def release(api, base, page):
    return api.request(base, f'/federation/local/queries/{page["sessionId"]}', "DELETE", expected=204)


def page_path(page):
    return f'/federation/local/queries/{page["sessionId"]}/pages?cursor=' + urllib.parse.quote(page["nextCursor"])


def cancel_and_release(api, proxy, sampler, base, ids, count):
    # Gate the source validation response for one in-flight page, then invalidate
    # the session by disabling browsing and prove both production slots reusable.
    began = time.monotonic()
    before_ui, before_link = api.meter.snapshot(), proxy.meter.snapshot()
    page, _ = query(api, base, ids, 50)
    time.sleep(.3)
    sampler.capture()
    rss_before_disable = sampler.latest()
    old_path = page_path(page)
    proxy.arm = True
    proxy.gated.clear()
    proxy.release.clear()
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
        pending = executor.submit(api.request, base, old_path)
        try:
            assert proxy.gated.wait(5), "The in-flight validation did not reach the response gate"
            _, disable_ms = api.request(base, "/federation/local/peers/browsing", "PUT", {"enabled": False})
        finally:
            proxy.release.set()
        try:
            pending.result(timeout=5)
            raise AssertionError("A cancelled page unexpectedly succeeded")
        except (ConnectionError, http.client.HTTPException, urllib.error.URLError) as error:
            if isinstance(error, urllib.error.URLError) and isinstance(error.reason, TimeoutError):
                raise AssertionError("A request timeout does not prove cancellation") from error
            cancelled_as = type(error).__name__
    disabled, _ = api.request(base, old_path, expected=403)
    assert disabled["code"] == "BrowsingDisabled"
    api.request(base, "/federation/local/peers/browsing", "PUT", {"enabled": True})
    expired, _ = api.request(base, old_path, expected=410)
    assert expired["code"] == "QuerySessionExpired"
    time.sleep(.3)
    sampler.capture()
    rss_after_disable = sampler.latest()
    active = [query(api, base, ids)[0], query(api, base, ids)[0]]
    assert all(p["coverageComplete"] and p["totalWithinParticipants"] == count * 2 for p in active)
    for current in active:
        release(api, base, current)
    time.sleep(.5)
    sampler.capture()
    cancellation = {"cancelledAs": cancelled_as, "disableMs": disable_ms,
                    "oldSessionAfterEnable": expired["code"], "bothQuotaSlotsReusable": True,
                    "rssBeforeDisableBytes": rss_before_disable,
                    "rssAfterDisableBytes": rss_after_disable,
                    "rssAfterReusingAndReleasingBothSlotsBytes": sampler.latest(),
                    "uiHttp": api.meter.delta(before_ui), "nodeHttp": proxy.meter.delta(before_link),
                    "memory": sampler.summarize(began)}
    assert all(value["sampleCount"] > 0 for value in cancellation["memory"].values())
    assert cancellation["nodeHttp"].get("route:DELETE /federation/v1/export/queries/{id}", 0) == 3
    return cancellation


def environment_metadata():
    result = {"system": platform.system(), "os": platform.platform(), "architecture": platform.machine(),
              "pythonPointerBits": ctypes.sizeof(ctypes.c_void_p) * 8, "cpuCount": os.cpu_count(),
              "machineName": platform.node(),
              "cpu": platform.processor(), "githubActions": os.environ.get("GITHUB_ACTIONS") == "true",
              "runnerEnvironment": os.environ.get("RUNNER_ENVIRONMENT", "unspecified"),
              "runnerOs": os.environ.get("RUNNER_OS"), "runnerArch": os.environ.get("RUNNER_ARCH"),
              "runnerImage": os.environ.get("ImageOS"), "runnerImageVersion": os.environ.get("ImageVersion")}
    if platform.system() == "Darwin":
        result["cpu"] = subprocess.check_output(["sysctl", "-n", "machdep.cpu.brand_string"],
                                                text=True, timeout=5).strip()
        result["physicalMemoryBytes"] = int(subprocess.check_output(["sysctl", "-n", "hw.memsize"],
                                                                    text=True, timeout=5))
    elif platform.system() == "Linux":
        result["physicalMemoryBytes"] = os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES")
    elif platform.system() == "Windows":
        class MemoryStatus(ctypes.Structure):
            _fields_ = [("length", ctypes.c_uint32), ("load", ctypes.c_uint32)] + [
                (name, ctypes.c_uint64) for name in ("totalPhysical", "availablePhysical", "totalPageFile",
                                                   "availablePageFile", "totalVirtual", "availableVirtual",
                                                   "availableExtendedVirtual")]
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.GlobalMemoryStatusEx.argtypes = [ctypes.POINTER(MemoryStatus)]
        kernel.GlobalMemoryStatusEx.restype = ctypes.c_int32
        status = MemoryStatus()
        status.length = ctypes.sizeof(status)
        if not kernel.GlobalMemoryStatusEx(ctypes.byref(status)):
            raise OSError("Physical memory metadata unavailable")
        result["physicalMemoryBytes"] = int(status.totalPhysical)
    else:
        raise RuntimeError("Benchmark supports Windows, Linux and macOS")
    return result


def fixture_metadata(identity, count):
    # Read-only arithmetic for the known TestHost rows, mirroring the production
    # QueryProtocol accounting. Only row 1 has the filename and longer title.
    # UTF-16 matches .NET String.Length even for a non-ASCII runner name.
    units = lambda value: len(value.encode("utf-16-le")) // 2
    base = 384 + 2 * sum(units(identity[key]) for key in ("name", "nodeId", "libraryEpoch"))
    normal = base + 2 * (3 * len("TITLE 00"))
    first = base + 2 * (3 * len("SHARED TITLE") + len("fixture.wav"))
    return {"nodeId": identity["nodeId"], "libraryEpoch": identity["libraryEpoch"],
            "fixtureLabel": identity["name"], "ownerLabelUtf16Units": units(identity["name"]),
            "estimatedSnapshotBytes": 256 + first + (count - 1) * normal,
            "snapshotBudgetBytes": 64 * 1024 * 1024,
            "accountingSource": "QueryProtocol.EstimateBytes; TestHost metadata-only rows plus fixture.wav"}


def run(args):
    repo = Path(__file__).resolve().parents[3]
    dll = repo / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
    if not dll.exists():
        raise SystemExit("Build Bakabase.Federation.TestHost first")
    results = args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-federation-benchmark-results-"))
    results.mkdir(parents=True, exist_ok=True)
    if (results / "result.json").exists():
        raise SystemExit("Use a fresh benchmark results directory")
    root = Path(tempfile.mkdtemp(prefix="bakabase-federation-benchmark-data-"))
    api = Api(time.monotonic() + args.timeout)
    report = {"schemaVersion": 2, "passed": False, "counts": args.counts, "repetitions": args.repetitions,
              "timeoutSeconds": args.timeout, "pageSize": 200, "percentileMethod": "nearest-rank",
              "latencyClock": {"name": "perf_counter", "resolutionSeconds": time.get_clock_info("perf_counter").resolution},
              "scales": [], "cleanupErrors": [],
              "conditions": ["Debug production TestHost; unchanged FederationQueryLimits",
                             "Every repetition restarts both processes before process-cold traversal",
                             "Fresh process is not an OS cold cache; filesystem caches are not flushed",
                             "Warm traversal follows release in the same processes; no forced GC",
                             "First-page latency includes query preparation and the first page response",
                             "Small first-page sample counts are descriptive, not a stable tail-latency estimate",
                             "A owns one SQLite library and reads B through a counting loopback HTTP proxy",
                             "TestHost-only fixed equal-length node labels control per-row metadata across platforms",
                             "Proxy preserves HTTP keep-alive with one upstream connection and at most four client workers",
                             "CI virtual machines and loopback HTTP do not represent physical LAN or NAS performance",
                             "Proxy buffering/connections and RSS sampling add measurement overhead",
                             "HTTP bytes are payloads only; sampled RSS/Windows working set includes shared pages",
                             "RSS is not managed live heap; session release need not immediately reduce RSS",
                             args.concurrent_load]}
    all_processes, logs = [], []

    def save():
        (results / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")

    def start(label, directory, port, count):
        check_deadline(api.deadline)
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "ready").unlink(missing_ok=True)
        log = (results / f"{label}.log").open("w", encoding="utf-8")
        logs.append(log)
        environment = dict(os.environ, **{FIXTURE_LABEL_ENVIRONMENT: FIXTURE_LABELS[0 if directory.name == "a" else 1]})
        process = subprocess.Popen([args.dotnet, str(dll), str(port), str(directory), str(count)],
                                   cwd=repo, stdout=log, stderr=subprocess.STDOUT, env=environment)
        all_processes.append(process)
        began = time.monotonic()
        started = time.perf_counter()
        while not (directory / "ready").exists():
            if process.poll() is not None:
                raise AssertionError(f"{label} exited; inspect retained host log")
            if time.monotonic() >= min(began + 180, api.deadline):
                raise TimeoutError(f"{label} startup exceeded its bound")
            time.sleep(.2)
        return process, round((time.perf_counter() - started) * 1000, 2)

    try:
        report["environment"] = environment_metadata()
        report["sdk"] = subprocess.check_output([args.dotnet, "--version"], text=True, timeout=5).strip()
        report["gitHead"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo,
                                                    text=True, timeout=5).strip()
        save()
        for count in args.counts:
            check_deadline(api.deadline)
            print(f"Preparing two libraries with {count} resources each", flush=True)
            scale = {"resourcesPerNode": count, "expectedTotal": count * 2, "runs": [],
                     "preparation": {"seedStartupMsByNode": {}, "restarts": []}}
            report["scales"].append(scale)
            directories = [root / str(count) / label for label in ("a", "b")]
            ports = [free_port(), free_port()]
            while ports[0] == ports[1]:
                check_deadline(api.deadline)
                ports[1] = free_port()
            bases = [f"http://127.0.0.1:{port}" for port in ports]
            proxy, sampler, processes = None, None, []
            first_owned_process = len(all_processes)
            try:
                proxy = LinkProxy(ports[1])
                preparation_started = time.perf_counter()
                # Seed once, sequentially; repetitions reuse the same two full databases.
                for index in range(2):
                    process, elapsed = start(f"{count}-{index}-seed", directories[index], ports[index], count)
                    processes.append(process)
                    scale["preparation"]["seedStartupMsByNode"][str(index)] = elapsed
                statuses = [api.request(base, "/federation/local/peers")[0] for base in bases]
                assert [status["identity"]["name"] for status in statuses] == list(FIXTURE_LABELS), "Fixture labels differ"
                scale["nodes"] = [fixture_metadata(status["identity"], count) for status in statuses]
                for directory, node, status in zip(directories, scale["nodes"], statuses):
                    fixture = json.loads((directory / "benchmark-node.json").read_text(encoding="utf-8"))
                    assert all(fixture[key] == node[key] for key in ("nodeId", "libraryEpoch", "fixtureLabel")), "Fixture identity differs"
                    assert isinstance(fixture["machineName"], str) and fixture["machineName"], "Native machine name missing"
                    node["nativeMachineName"] = fixture["machineName"]
                    original = dict(status["identity"], name=fixture["machineName"])
                    node["estimatedSnapshotBytesWithNativeMachineName"] = fixture_metadata(original, count)["estimatedSnapshotBytes"]
                ids = [status["identity"]["nodeId"] for status in statuses]
                pairing_started = time.perf_counter()
                invitation = api.request(bases[1], "/federation/local/peers/invite", "POST")[0]
                outcome = api.request(bases[0], "/federation/local/peers/connect", "POST",
                                      {"address": proxy.base, "code": invitation["code"]})[0]
                assert outcome["outcome"] == "granted"
                api.request(bases[0], "/federation/local/peers/browsing", "PUT", {"enabled": True})
                scale["preparation"]["pairingMs"] = (time.perf_counter() - pairing_started) * 1000
                scale["preparation"]["totalMs"] = (time.perf_counter() - preparation_started) * 1000
                for repetition in range(1, args.repetitions + 1):
                    check_deadline(api.deadline)
                    for process in processes:
                        stop_process(process)
                    processes.clear()
                    proxy.reset_upstream()
                    restart = {"repetition": repetition, "startupMsByNode": {}, "pidsByNode": {}}
                    scale["preparation"]["restarts"].append(restart)
                    for index in range(2):
                        process, elapsed = start(f"{count}-{index}-measured-{repetition}", directories[index],
                                                 ports[index], count)
                        processes.append(process)
                        restart["startupMsByNode"][str(index)] = elapsed
                        restart["pidsByNode"][str(index)] = process.pid
                    sampler = RssSampler([process.pid for process in processes])
                    for temperature in ("process-cold", "warm"):
                        measured = traverse(api, proxy, sampler, bases[0], ids, count, repetition, temperature)
                        scale["runs"].append(measured)
                        scale["summary"] = summarize_runs(scale["runs"])
                        print(json.dumps({"count": count, **{key: value for key, value in measured.items()
                                                           if key != "pageLatencyMs"}}), flush=True)
                        save()
                    if repetition == args.repetitions:
                        scale["cancellation"] = cancel_and_release(api, proxy, sampler, bases[0], ids, count)
                        print(json.dumps({"count": count, "cancellation": scale["cancellation"]}), flush=True)
                    restart["rssSampling"] = sampler.diagnostics()
                    sampler.require_coverage()
                    sampler.close()
                    sampler = None
                scale["passed"] = True
            except Exception as error:
                scale["passed"] = False
                scale["error"] = f"{type(error).__name__}: {error}"
                print(json.dumps({"count": count, "error": scale["error"]}), flush=True)
            finally:
                if sampler:
                    scale["lastRssSampling"] = sampler.diagnostics()
                    cleanup_step(report["cleanupErrors"], f"rss-{count}", sampler.close)
                if proxy:
                    scale["proxyTransport"] = proxy.diagnostics()
                    cleanup_step(report["cleanupErrors"], f"proxy-{count}", proxy.close)
                # Includes a process which failed before start() returned readiness.
                for process in all_processes[first_owned_process:]:
                    cleanup_step(report["cleanupErrors"], f"host-{process.pid}", lambda p=process: stop_process(p))
                save()
            # Cleanup failure cannot be allowed to accumulate live hosts at the next scale.
            if report["cleanupErrors"]:
                break
        report["passed"] = (len(report["scales"]) == len(args.counts) and
                            all(scale.get("passed") for scale in report["scales"]))
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        for process in all_processes:
            cleanup_step(report["cleanupErrors"], f"host-{process.pid}", lambda p=process: stop_process(p))
        report["ownedProcessesStopped"] = all(process.poll() is not None for process in all_processes)
        for index, log in enumerate(logs):
            cleanup_step(report["cleanupErrors"], f"log-{index}", log.close)
        if report["ownedProcessesStopped"]:
            cleanup_step(report["cleanupErrors"], "fixture-data", lambda: remove_fixture(root))
        report["fixtureDataRemoved"] = not root.exists()
        report["passed"] = (report["passed"] and report["ownedProcessesStopped"] and
                            report["fixtureDataRemoved"] and not report["cleanupErrors"])
        save()
        print(f"Retained benchmark results: {results}", flush=True)
    return 0 if report.get("passed") else 1


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--counts", type=int, nargs="+", default=[10000, 100000], choices=(10000, 100000))
    parser.add_argument("--repetitions", type=int, default=3,
                        help="Full process-cold/warm traversal pairs per scale (1..5; default: 3)")
    parser.add_argument("--timeout", type=int, default=MAX_TIMEOUT)
    parser.add_argument("--results-directory", type=Path)
    parser.add_argument("--concurrent-load", default="Other system load was not controlled")
    args = parser.parse_args(argv)
    if len(set(args.counts)) != len(args.counts):
        parser.error("Each scale may only be specified once")
    if not 1 <= args.repetitions <= MAX_REPETITIONS:
        parser.error("Repetitions must be 1..5; data and production budgets remain unchanged")
    if not 1 <= args.timeout <= MAX_TIMEOUT:
        parser.error("The overall benchmark deadline must be 1..900 seconds")
    return args


if __name__ == "__main__":
    raise SystemExit(run(parse_args()))
