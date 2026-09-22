#!/usr/bin/env python3
"""Pure benchmark boundary fixtures; never starts a product host or large dataset."""
from collections import Counter, deque
import contextlib
import ctypes
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import threading
import types
import unittest
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location("benchmark", Path(__file__).with_name("benchmark.py"))
benchmark = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(benchmark)


class FakeFunction:
    def __init__(self, callback):
        self.callback = callback
        self.calls = []

    def __call__(self, *args):
        self.calls.append(args)
        return self.callback(*args)


class BenchmarkTests(unittest.TestCase):
    def test_defaults_keep_both_full_scales_and_bounded_repetitions_deadline(self):
        args = benchmark.parse_args([])
        self.assertEqual(args.counts, [10000, 100000])
        self.assertEqual(args.repetitions, 3)
        self.assertEqual(args.timeout, 900)
        for argv in (["--counts", "9999"], ["--counts", "100001"],
                     ["--counts", "10000", "10000"], ["--repetitions", "0"],
                     ["--repetitions", "6"], ["--timeout", "0"], ["--timeout", "901"]):
            with self.subTest(argv=argv), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as result:
                    benchmark.parse_args(argv)
                self.assertEqual(result.exception.code, 2)

    def test_fixture_label_estimate_records_long_native_name_budget_boundary(self):
        identity = {"name": "benchmark-a", "nodeId": "a" * 32, "libraryEpoch": "e" * 32}
        short = benchmark.fixture_metadata(identity, 100000)
        long = benchmark.fixture_metadata(dict(identity, name="h" * 63), 100000)
        self.assertEqual(short["estimatedSnapshotBytes"], 58200302)
        self.assertEqual(long["estimatedSnapshotBytes"] - short["estimatedSnapshotBytes"], 10400000)
        self.assertLess(short["estimatedSnapshotBytes"], short["snapshotBudgetBytes"])
        self.assertGreater(long["estimatedSnapshotBytes"], long["snapshotBudgetBytes"])
        unicode = benchmark.fixture_metadata(dict(identity, name="\U0001f600"), 100000)
        self.assertEqual(unicode["ownerLabelUtf16Units"], 2)

    def test_latency_uses_high_resolution_clock_without_changing_deadline_clock(self):
        response = Mock(status=200)
        response.read.return_value = b"{}"
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        with patch.object(benchmark.time, "monotonic", return_value=10), \
                patch.object(benchmark.time, "perf_counter", side_effect=[1, 1.00125]), \
                patch.object(benchmark.urllib.request, "urlopen", return_value=response) as request:
            result, elapsed = benchmark.Api(12).request("http://fixture", "/query")
        self.assertEqual(result, {})
        self.assertAlmostEqual(elapsed, 1.25)
        self.assertEqual(request.call_args.kwargs["timeout"], 2)

    def proxy(self):
        proxy = benchmark.LinkProxy.__new__(benchmark.LinkProxy)
        proxy.transport_lock = threading.Lock()
        proxy.transport = None
        proxy.transport_values = Counter()
        proxy.upstream_port = 1234
        return proxy

    def test_proxy_reuses_upstream_and_explicit_restart_reset_closes_it(self):
        connection = Mock()
        response = connection.getresponse.return_value
        response.status = 200
        response.getheaders.return_value = [("Content-Length", "2")]
        response.read.return_value = b"{}"
        proxy = self.proxy()
        with patch.object(benchmark.http.client, "HTTPConnection", return_value=connection) as create:
            for _ in range(3):
                self.assertEqual(proxy.forward("GET", "/fixture", b"", {}),
                                 (200, [("Content-Length", "2")], b"{}"))
            create.assert_called_once_with("127.0.0.1", 1234, timeout=12)
            self.assertEqual(connection.request.call_count, 3)
            connection.close.assert_not_called()
            proxy.reset_upstream()
            connection.close.assert_called_once_with()
            self.assertIsNone(proxy.transport)
            proxy.forward("GET", "/fixture", b"", {})
            self.assertEqual(create.call_count, 2)
            proxy.reset_upstream()
        self.assertEqual(proxy.diagnostics(), {"upstreamClientsCreated": 2})

    def test_proxy_failed_request_never_retries_and_records_only_sanitized_diagnostics(self):
        proxy = self.proxy()
        connection = Mock()
        error = OSError(10048, "sensitive fixture URL must not be recorded")
        connection.request.side_effect = error
        with patch.object(benchmark.http.client, "HTTPConnection", return_value=connection) as create:
            with self.assertRaises(OSError) as failure:
                proxy.forward("POST", "/query", b"sensitive body", {})
        self.assertIs(failure.exception, error)
        create.assert_called_once()
        connection.request.assert_called_once()
        connection.close.assert_called_once()
        self.assertIsNone(proxy.transport)
        proxy.record_transport_error("upstream", error)
        report = proxy.diagnostics()
        self.assertEqual(report, {"upstreamClientsCreated": 1, "upstream:OSError": 1,
                                  "upstream:nativeCode:10048": 1})
        self.assertNotIn("sensitive", json.dumps(report))

    def test_proxy_close_unblocks_owned_clients_and_closes_upstream(self):
        proxy = self.proxy()
        proxy.release, proxy.server, proxy.thread = Mock(), Mock(), Mock()
        proxy.thread.is_alive.return_value = False
        proxy.client_lock = threading.Lock()
        client = Mock()
        proxy.clients = {client}
        proxy.clients_stopped = Mock()
        proxy.clients_stopped.wait.return_value = True
        transport = proxy.transport = Mock()
        proxy.close()
        client.shutdown.assert_called_once_with(benchmark.socket.SHUT_RDWR)
        transport.close.assert_called_once_with()
        proxy.clients_stopped.wait.assert_called_once_with(5)
        proxy.server.shutdown.assert_called_once_with()
        proxy.server.server_close.assert_called_once_with()

    def test_fixture_http_proxy_keeps_both_legs_alive_and_stops_its_workers(self):
        accepted = []

        class Handler(benchmark.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def setup(self):
                super().setup()
                accepted.append(self.client_address)

            def do_POST(self):
                self.rfile.read(int(self.headers.get("Content-Length", 0)))
                self.send_response(200)
                self.send_header("Content-Length", "2")
                self.end_headers()
                self.wfile.write(b"{}")

            def log_message(self, *_):
                pass

        upstream = benchmark.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        worker = threading.Thread(target=upstream.serve_forever, daemon=True)
        worker.start()
        proxy = benchmark.LinkProxy(upstream.server_port)
        client = benchmark.http.client.HTTPConnection("127.0.0.1", proxy.server.server_port, timeout=2)
        try:
            for _ in range(3):
                client.request("POST", "/fixture", body=b"{}")
                response = client.getresponse()
                self.assertEqual(response.status, 200)
                self.assertEqual(response.read(), b"{}")
                self.assertFalse(response.will_close)
            self.assertEqual(len(accepted), 1)
            self.assertEqual(proxy.meter.snapshot()["requests"], 3)
            self.assertEqual(proxy.diagnostics(), {"upstreamClientsCreated": 1})
            # Deliberately leave the client open: cleanup must unblock its worker.
            proxy.close()
            self.assertTrue(proxy.clients_stopped.is_set())
            self.assertFalse(proxy.thread.is_alive())
            self.assertIsNone(proxy.transport)
        finally:
            client.close()
            if proxy.thread.is_alive():
                proxy.close()
            upstream.shutdown()
            upstream.server_close()
            worker.join(timeout=2)

    def test_nearest_rank_small_sample_tail_and_pooled_pages_are_explicit(self):
        self.assertEqual(benchmark.distribution([8, 2, 4]),
                         {"samples": 3, "p50": 4, "p95": 8, "min": 2, "max": 8})
        runs = [{"temperature": temperature, "firstPageMs": first, "traversalMs": first + 10,
                 "releaseMs": 1, "pageLatencyMs": pages, "uiHttp": {"requests": 2},
                 "nodeHttp": {"responsePayloadBytes": 10}}
                for temperature, first, pages in [("process-cold", 100, [1, 2]),
                                                   ("process-cold", 300, [9]), ("warm", 20, [3])]]
        summary = benchmark.summarize_runs(runs)
        self.assertEqual(summary["process-cold"]["firstPageMs"]["p95"], 300)
        self.assertEqual(summary["process-cold"]["subsequentPageMs"]["samples"], 3)
        self.assertEqual(summary["process-cold"]["nodeHttpTotals"]["responsePayloadBytes"], 20)
        self.assertEqual(summary["warm"]["firstPageMs"]["samples"], 1)
        self.assertIsNone(benchmark.distribution([])["p95"])

    def test_posix_rss_uses_kib_and_requires_every_owned_pid(self):
        result = types.SimpleNamespace(stdout=" 12 512\n 34 1024\n")
        with patch.object(benchmark.subprocess, "run", return_value=result) as command:
            self.assertEqual(benchmark.posix_rss([12, 34]), {12: 524288, 34: 1048576})
            self.assertEqual(command.call_args.args[0], ["ps", "-o", "pid=,rss=", "-p", "12,34"])
            self.assertEqual(command.call_args.kwargs["timeout"], 3)
            self.assertTrue(command.call_args.kwargs["check"])
        for text in ("12 1\n", "12 1\n56 2\n", "12 -1\n34 2\n", "12 1 2\n", "12 1\n12 2\n"):
            with self.subTest(text=text), patch.object(benchmark.subprocess, "run",
                                                     return_value=types.SimpleNamespace(stdout=text)):
                with self.assertRaises(ValueError):
                    benchmark.posix_rss([12, 34])

    def windows_reader(self, success=True, open_success=True):
        kernel = types.SimpleNamespace(
            OpenProcess=FakeFunction(lambda access, inherit, pid: (pid + 100) if open_success else 0),
            CloseHandle=FakeFunction(lambda handle: 1))

        def memory(handle, pointer, size):
            self.assertEqual(size, ctypes.sizeof(benchmark.ProcessMemoryCounters))
            counter = ctypes.cast(pointer, ctypes.POINTER(benchmark.ProcessMemoryCounters)).contents
            self.assertEqual(counter.cb, size)
            counter.WorkingSetSize = (1 << 33) + handle
            counter.PeakWorkingSetSize = 1 << 40
            return success

        psapi = types.SimpleNamespace(GetProcessMemoryInfo=FakeFunction(memory))
        with patch.object(benchmark.ctypes, "WinDLL", side_effect=[kernel, psapi], create=True):
            reader = benchmark.WindowsRssReader()
        return reader, kernel, psapi

    def test_windows_rss_uses_current_working_set_and_closes_owned_query_handles(self):
        reader, kernel, psapi = self.windows_reader()
        self.assertEqual(reader([12, 34]), {12: (1 << 33) + 112, 34: (1 << 33) + 134})
        self.assertEqual(kernel.OpenProcess.calls, [(0x1000, False, 12), (0x1000, False, 34)])
        self.assertEqual(kernel.CloseHandle.calls, [(112,), (134,)])
        self.assertEqual(kernel.OpenProcess.restype, ctypes.c_void_p)
        self.assertEqual(psapi.GetProcessMemoryInfo.restype, ctypes.c_int32)
        self.assertEqual(benchmark.ProcessMemoryCounters.WorkingSetSize.offset,
                         8 + ctypes.sizeof(ctypes.c_size_t))

    def test_windows_rss_closes_handle_on_failure_without_zero_sample(self):
        reader, kernel, _ = self.windows_reader(success=False)
        with self.assertRaises(OSError):
            reader([12])
        self.assertEqual(kernel.CloseHandle.calls, [(112,)])
        reader, kernel, _ = self.windows_reader(open_success=False)
        with self.assertRaises(OSError):
            reader([12])
        self.assertEqual(kernel.CloseHandle.calls, [])

    def sampler(self, reader):
        sampler = benchmark.RssSampler.__new__(benchmark.RssSampler)
        sampler.pids = [12, 34]
        sampler.reader = reader
        sampler.source = "fixture"
        sampler.lock = threading.Lock()
        sampler.samples = deque(maxlen=3610)
        sampler.errors = Counter()
        return sampler

    def test_sampler_records_failures_recovers_and_keeps_phase_sample_counts(self):
        sampler = self.sampler(Mock(side_effect=[subprocess.TimeoutExpired(["ps"], 3),
                                                {12: 100, 34: 200}, {12: 150, 34: 180}]))
        sampler.capture()
        with self.assertRaises(AssertionError):
            sampler.require_coverage()
        with patch.object(benchmark.time, "monotonic", side_effect=[10, 20]):
            sampler.capture()
            sampler.capture()
        sampler.require_coverage()
        summary = sampler.summarize(10)
        self.assertEqual(summary["12"], {"sampleCount": 2, "firstRssBytes": 100,
                                         "sampledPeakRssBytes": 150, "lastRssBytes": 150})
        self.assertEqual(sampler.summarize(11)["34"]["sampleCount"], 1)
        self.assertEqual(sampler.diagnostics()["errors"], {"TimeoutExpired": 1})
        self.assertEqual(sampler.latest(), {"12": 150, "34": 180})

    def test_windows_sampler_never_invokes_ps(self):
        reader = Mock(return_value={12: 100, 34: 200})
        with patch.object(benchmark.platform, "system", return_value="Windows"), \
                patch.object(benchmark, "WindowsRssReader", return_value=reader), \
                patch.object(benchmark, "posix_rss") as posix, patch.object(benchmark.threading, "Thread"):
            sampler = benchmark.RssSampler([12, 34])
        reader.assert_called_once_with([12, 34])
        posix.assert_not_called()
        self.assertEqual(sampler.source, "GetProcessMemoryInfo.WorkingSetSize")

    def test_windows_memory_metadata_has_explicit_native_layout(self):
        def status(pointer):
            value = pointer._obj
            self.assertEqual(value.length, 64)
            value.totalPhysical = 16 * (1 << 30)
            return 1
        kernel = types.SimpleNamespace(GlobalMemoryStatusEx=FakeFunction(status))
        with patch.object(benchmark.platform, "system", return_value="Windows"), \
                patch.object(benchmark.ctypes, "WinDLL", return_value=kernel, create=True), \
                patch.dict(benchmark.os.environ, {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
                                                  "RUNNER_ARCH": "X64"}):
            result = benchmark.environment_metadata()
        self.assertEqual(result["physicalMemoryBytes"], 16 * (1 << 30))
        self.assertEqual(result["runnerEnvironment"], "github-hosted")
        self.assertTrue(result["githubActions"])

    def test_cleanup_stops_and_waits_before_windows_file_removal(self):
        process = Mock()
        process.poll.return_value = None
        process.wait.side_effect = [subprocess.TimeoutExpired("host", 10), 0]
        benchmark.stop_process(process)
        self.assertEqual(process.method_calls,
                         [("poll", (), {}), ("terminate", (), {}), ("wait", (), {"timeout": 10}),
                          ("kill", (), {}), ("wait", (), {"timeout": 5})])
        with patch.object(benchmark.shutil, "rmtree", side_effect=[PermissionError(), None]) as remove, \
                patch.object(benchmark.time, "sleep") as sleep:
            benchmark.remove_fixture(Path("owned-fixture"))
        self.assertEqual(remove.call_count, 2)
        sleep.assert_called_once_with(.2)

    def test_cleanup_failure_is_bounded_and_does_not_skip_other_cleanup(self):
        errors = []
        with patch.object(benchmark.shutil, "rmtree", side_effect=PermissionError()) as remove, \
                patch.object(benchmark.time, "sleep") as sleep:
            benchmark.cleanup_step(errors, "fixture", lambda: benchmark.remove_fixture(Path("owned")))
        completed = Mock()
        benchmark.cleanup_step(errors, "next", completed)
        completed.assert_called_once_with()
        self.assertEqual(remove.call_count, 5)
        self.assertEqual(sleep.call_count, 4)
        self.assertEqual(errors, [{"step": "fixture", "error": "PermissionError"}])

    def test_expired_deadline_and_stale_result_rejected_before_host_launch(self):
        with patch.object(benchmark.time, "monotonic", return_value=10):
            with self.assertRaises(TimeoutError):
                benchmark.check_deadline(10)
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory)
            (results / "result.json").write_text("{}")
            with patch.object(benchmark.Path, "exists", return_value=True), \
                    patch.object(benchmark.subprocess, "Popen") as launch:
                with self.assertRaisesRegex(SystemExit, "fresh"):
                    benchmark.run(benchmark.parse_args(["--results-directory", directory]))
            launch.assert_not_called()

    def page(self, resources, cursor=None):
        return {"coverageComplete": True, "totalWithinParticipants": 4, "sessionId": "fixture",
                "nextCursor": cursor, "items": [
                    {"ref": {"nodeId": "node", "libraryEpoch": "epoch", "resourceId": identity},
                     "normalizedSortKey": f"{identity:02}"} for identity in resources]}

    def test_full_traversal_counts_all_resources_pages_bytes_and_releases(self):
        api = types.SimpleNamespace(meter=benchmark.Meter(), request=Mock(
            side_effect=[(self.page([1, 2], "next"), 12), (self.page([3, 4]), 3), (None, 1)]))
        proxy = types.SimpleNamespace(meter=benchmark.Meter())
        sampler = Mock()
        sampler.latest.return_value = {"12": 100}
        sampler.summarize.return_value = {"12": {"sampleCount": 1}}
        with patch.object(benchmark.time, "sleep"):
            result = benchmark.traverse(api, proxy, sampler, "http://fixture", ["a", "b"], 2, 3, "warm")
        self.assertEqual(result["resources"], 4)
        self.assertEqual(result["pages"], 2)
        self.assertEqual(result["pageLatencyMs"], [3])
        self.assertEqual(result["firstPageMs"], 12)
        self.assertEqual(result["repetition"], 3)
        self.assertEqual(api.request.call_args_list[-1].args[2], "DELETE")

    def test_traversal_rejects_duplicates_missing_rows_and_excess_rows(self):
        for pages in ([self.page([1, 2], "next"), self.page([2, 4])], [self.page([1, 2])],
                      [self.page([1, 2, 3, 4, 5])]):
            api = types.SimpleNamespace(meter=benchmark.Meter(), request=Mock(
                side_effect=[(page, 1) for page in pages]))
            sampler = Mock()
            with self.subTest(pages=pages), patch.object(benchmark.time, "sleep"):
                with self.assertRaises(AssertionError):
                    benchmark.traverse(api, types.SimpleNamespace(meter=benchmark.Meter()), sampler,
                                       "http://fixture", ["a", "b"], 2, 1, "process-cold")

    def cancellation_fixture(self, failure):
        proxy = types.SimpleNamespace(meter=benchmark.Meter(), gated=Mock(), release=Mock(), arm=False)
        proxy.gated.wait.return_value = True
        sampler = Mock()
        sampler.latest.return_value = {"12": 100}
        sampler.summarize.return_value = {"12": {"sampleCount": 1}}
        requests = []

        def request(base, path, method="GET", body=None, expected=200):
            requests.append((path, method, body))
            if method == "POST":
                return self.page([], "next"), 1
            if method == "DELETE":
                proxy.meter.add("DELETE", "/federation/v1/export/queries/fixture", 204, 0, 0)
            if expected == 403:
                return {"code": "BrowsingDisabled"}, 1
            if expected == 410:
                return {"code": "QuerySessionExpired"}, 1
            if method == "PUT" and not body["enabled"]:
                proxy.meter.add("DELETE", "/federation/v1/export/queries/fixture", 204, 0, 0)
            return None, 1

        api = types.SimpleNamespace(meter=benchmark.Meter(), request=request)
        pending = Mock()
        pending.result.side_effect = failure
        executor = Mock()
        executor.submit.return_value = pending
        context = Mock()
        context.__enter__ = Mock(return_value=executor)
        context.__exit__ = Mock(return_value=False)
        return api, proxy, sampler, requests, context

    def test_cancellation_releases_exact_source_sessions_and_checks_both_slots(self):
        api, proxy, sampler, requests, executor = self.cancellation_fixture(
            benchmark.http.client.RemoteDisconnected())
        with patch.object(benchmark.concurrent.futures, "ThreadPoolExecutor", return_value=executor), \
                patch.object(benchmark.time, "sleep"):
            result = benchmark.cancel_and_release(api, proxy, sampler, "http://fixture", ["a", "b"], 2)
        self.assertTrue(result["bothQuotaSlotsReusable"])
        self.assertEqual(result["oldSessionAfterEnable"], "QuerySessionExpired")
        self.assertEqual(result["nodeHttp"]["route:DELETE /federation/v1/export/queries/{id}"], 3)
        self.assertEqual(sum(method == "POST" for _, method, _ in requests), 3)
        self.assertEqual(sum(method == "DELETE" for _, method, _ in requests), 2)
        proxy.release.set.assert_called_once_with()

    def test_cancellation_timeout_is_failure_and_still_releases_response_gate(self):
        for failure in (TimeoutError(), benchmark.urllib.error.URLError(TimeoutError())):
            api, proxy, sampler, _, executor = self.cancellation_fixture(failure)
            with self.subTest(failure=type(failure).__name__), \
                    patch.object(benchmark.concurrent.futures, "ThreadPoolExecutor", return_value=executor), \
                    patch.object(benchmark.time, "sleep"):
                with self.assertRaises((TimeoutError, AssertionError)):
                    benchmark.cancel_and_release(api, proxy, sampler, "http://fixture", ["a", "b"], 2)
            proxy.release.set.assert_called_once_with()

    def fake_run(self, directory, *, sampling_failure=False, remove_failure=False, ready=True, expire=False):
        directory = Path(directory)
        dll = directory / "repo/src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
        dll.parent.mkdir(parents=True)
        dll.touch()
        source = directory / "repo/src/tests/federation-smoke/benchmark.py"
        result_path = directory / "results"
        args = benchmark.parse_args(["--results-directory", str(result_path)])
        processes, open_logs, identities = [], [], {}

        def launch(command, **kwargs):
            process = Mock(pid=100 + len(processes))
            process.poll.return_value = None
            process.wait.side_effect = lambda **_: setattr(process.poll, "return_value", 0)
            processes.append(process)
            open_logs.append(kwargs["stdout"])
            label = kwargs["env"][benchmark.FIXTURE_LABEL_ENVIRONMENT]
            self.assertEqual(label, benchmark.FIXTURE_LABELS[0 if Path(command[3]).name == "a" else 1])
            origin = f"http://127.0.0.1:{command[2]}"
            identities[origin] = {"nodeId": origin, "libraryEpoch": "epoch", "name": label}
            if ready:
                (Path(command[3]) / "ready").write_text("ready")
                (Path(command[3]) / "benchmark-node.json").write_text(json.dumps(
                    {"nodeId": f"http://127.0.0.1:{command[2]}", "libraryEpoch": "epoch",
                     "fixtureLabel": label, "machineName": "fixture-native-machine"}))
            return process

        def request(base, path, *args, **kwargs):
            if path.endswith("invite"):
                return {"code": "fixture"}, 1
            if path.endswith("connect"):
                return {"outcome": "granted"}, 1
            if path.endswith("peers"):
                return {"identity": identities[base]}, 1
            return None, 1

        def traversal(api, proxy, sampler, base, ids, count, repetition, temperature):
            return {"temperature": temperature, "repetition": repetition, "resources": count * 2,
                    "firstPageMs": 10, "traversalMs": 20, "releaseMs": 1,
                    "pageLatencyMs": [1, 2], "uiHttp": {}, "nodeHttp": {}}

        sampler = Mock()
        sampler.diagnostics.return_value = {"fixture": True}
        if sampling_failure:
            sampler.require_coverage.side_effect = AssertionError("Missing RSS")
        proxy = Mock(base="http://fixture")
        proxy.diagnostics.return_value = {}
        actual_mkdtemp = tempfile.mkdtemp
        fixture_roots = []

        def mkdtemp(**kwargs):
            root = actual_mkdtemp(dir=directory, **kwargs)
            fixture_roots.append(Path(root))
            return root

        actual_remove = benchmark.remove_fixture

        def remove(root):
            self.assertTrue(all(process.poll() is not None for process in processes))
            self.assertTrue(all(log.closed for log in open_logs))
            if remove_failure:
                raise PermissionError("fixture failure")
            actual_remove(root)

        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(benchmark, "__file__", str(source)))
            stack.enter_context(patch.object(benchmark, "environment_metadata", return_value={"system": "fixture"}))
            stack.enter_context(patch.object(benchmark.subprocess, "check_output", return_value="fixture"))
            stack.enter_context(patch.object(benchmark.subprocess, "Popen", side_effect=launch))
            stack.enter_context(patch.object(benchmark.tempfile, "mkdtemp", side_effect=mkdtemp))
            stack.enter_context(patch.object(benchmark, "LinkProxy", return_value=proxy))
            stack.enter_context(patch.object(benchmark, "RssSampler", return_value=sampler))
            stack.enter_context(patch.object(benchmark.Api, "request", side_effect=request))
            traverse = stack.enter_context(patch.object(benchmark, "traverse", side_effect=traversal))
            cancellation = stack.enter_context(patch.object(benchmark, "cancel_and_release", return_value={"passed": True}))
            stack.enter_context(patch.object(benchmark, "remove_fixture", side_effect=remove))
            stack.enter_context(patch.object(benchmark.time, "sleep"))
            if expire:
                stack.enter_context(patch.object(benchmark.time, "monotonic", side_effect=[0, 901]))
            elif not ready:
                clock = iter(range(0, 10000, 181))
                stack.enter_context(patch.object(benchmark.time, "monotonic", side_effect=lambda: next(clock)))
            stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
            code = benchmark.run(args)
        report = json.loads((result_path / "result.json").read_text())
        return code, report, processes, fixture_roots, traverse, cancellation

    def test_run_restarts_both_full_libraries_each_repetition_and_cleans_windows_handles(self):
        with tempfile.TemporaryDirectory() as directory:
            code, report, processes, roots, traversal, cancellation = self.fake_run(directory)
            self.assertEqual(code, 0)
            self.assertEqual(report["schemaVersion"], 2)
            self.assertEqual(len(processes), 16)  # 2 scales * (2 seed + 3 * 2 fresh hosts).
            self.assertEqual(traversal.call_count, 12)
            self.assertEqual(cancellation.call_count, 2)
            for scale in report["scales"]:
                self.assertEqual(len(scale["runs"]), 6)
                self.assertEqual(len(scale["preparation"]["restarts"]), 3)
                self.assertEqual(scale["summary"]["process-cold"]["firstPageMs"]["samples"], 3)
                self.assertTrue(all(run["resources"] == scale["resourcesPerNode"] * 2 for run in scale["runs"]))
            self.assertTrue(report["ownedProcessesStopped"])
            self.assertTrue(report["fixtureDataRemoved"])
            self.assertTrue(all(not root.exists() for root in roots))
            self.assertEqual(report["cleanupErrors"], [])
            conditions = " ".join(report["conditions"])
            self.assertIn("not an OS cold cache", conditions)
            self.assertIn("do not represent physical LAN or NAS", conditions)

    def test_missing_rss_and_removal_failure_cannot_report_success(self):
        for keyword in ("sampling_failure", "remove_failure"):
            with self.subTest(keyword=keyword), tempfile.TemporaryDirectory() as directory:
                code, report, processes, roots, *_ = self.fake_run(directory, **{keyword: True})
                self.assertEqual(code, 1)
                self.assertFalse(report["passed"])
                self.assertTrue(report["ownedProcessesStopped"])
                if keyword == "remove_failure":
                    self.assertFalse(report["fixtureDataRemoved"])
                    self.assertEqual(report["cleanupErrors"], [{"step": "fixture-data", "error": "PermissionError"}])

    def test_startup_timeout_cleans_even_host_not_returned_from_start(self):
        with tempfile.TemporaryDirectory() as directory:
            code, report, processes, roots, traversal, _ = self.fake_run(directory, ready=False)
            self.assertEqual(code, 1)
            self.assertTrue(processes)
            self.assertTrue(report["ownedProcessesStopped"])
            self.assertTrue(report["fixtureDataRemoved"])
            traversal.assert_not_called()

    def test_overall_deadline_never_starts_another_host(self):
        with tempfile.TemporaryDirectory() as directory:
            code, report, processes, roots, traversal, _ = self.fake_run(directory, expire=True)
            self.assertEqual(code, 1)
            self.assertEqual(processes, [])
            self.assertTrue(report["fixtureDataRemoved"])
            traversal.assert_not_called()


if __name__ == "__main__":
    unittest.main()
