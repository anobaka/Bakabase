import importlib.util
import contextlib
import ctypes
import io
import json
from pathlib import Path
import struct
import tarfile
import tempfile
import types
import unittest
from unittest.mock import Mock, patch


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


runner = load("player_runner_tests", "run.py")
prepare = load("player_prepare_tests", "prepare.py")


class NativeFunction:
    def __init__(self, callback):
        self.callback = callback
        self.calls = []

    def __call__(self, *args):
        self.calls.append(args)
        return self.callback(*args)


class PlayerAcceptanceTests(unittest.TestCase):
    @unittest.skipIf(runner.os.name == "nt", "macOS bundle symlink extraction requires POSIX permissions")
    def test_upstream_macos_tarball_preserves_executable_and_internal_links(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with tarfile.open(root / "mpv.tar.gz", "w:gz") as archive:
                item = tarfile.TarInfo("mpv.app/Contents/MacOS/mpv")
                item.mode, item.size = 0o755, 7
                archive.addfile(item, io.BytesIO(b"fixture"))
                link = tarfile.TarInfo("mpv.app/Contents/MacOS/mpv-link")
                link.type, link.linkname = tarfile.SYMTYPE, "mpv"
                archive.addfile(link)
            report = prepare.extract_macos_bundle(root)
            executable = root / "bundle/mpv.app/Contents/MacOS/mpv"
            self.assertEqual(executable.read_bytes(), b"fixture")
            self.assertEqual((executable.parent / "mpv-link").resolve(), executable.resolve())
            self.assertFalse((root / "mpv.tar.gz").exists())
            self.assertEqual(report["uncompressedBytes"], 7)
            if runner.os.name != "nt":
                self.assertEqual(executable.stat().st_mode & 0o777, 0o755)

    def test_macos_bundle_rejects_traversal_external_symlinks_and_special_files(self):
        for kind in ("traversal", "symlink", "device", "duplicate"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                with tarfile.open(root / "mpv.tar.gz", "w:gz") as archive:
                    item = tarfile.TarInfo("../escaped" if kind == "traversal" else "mpv.app/item")
                    if kind == "symlink":
                        item.type, item.linkname = tarfile.SYMTYPE, "../../escaped"
                    elif kind == "device":
                        item.type = tarfile.CHRTYPE
                    archive.addfile(item)
                    if kind == "duplicate":
                        archive.addfile(item)
                with self.assertRaises(AssertionError):
                    prepare.extract_macos_bundle(root)
                self.assertFalse((root / "bundle").exists())

    def test_requires_hosted_runner_before_any_player_or_filesystem_action(self):
        with patch.dict(runner.os.environ, {}, clear=True), patch.object(runner.tempfile, "mkdtemp") as create:
            with self.assertRaisesRegex(AssertionError, "disposable"):
                runner.run(type("Args", (), {"rid": "osx-arm64"})())
        create.assert_not_called()

    def test_pinned_asset_rejects_wrong_digest_size_id_and_filename(self):
        for rid, (identifier, size, digest, suffix) in prepare.PINNED.items():
            valid = {"id": identifier, "size": size, "digest": "sha256:" + digest,
                     "name": f"mpv-v0.41.0-dev-gc6c4c38d7-35659722192-{suffix}.zip"}
            prepare.validate_asset(valid, rid)
            for key, value in (("id", 1), ("size", size - 1), ("digest", "sha256:" + "0" * 64), ("name", "mpv.zip")):
                with self.subTest(rid=rid, key=key), self.assertRaises(AssertionError):
                    prepare.validate_asset(dict(valid, **{key: value}), rid)

    def test_video_has_exact_avi_index_and_bounded_payload(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.avi"
            report = runner.make_video(path, seconds=1)
            data = path.read_bytes()
        self.assertEqual(b"RIFF", data[:4])
        self.assertEqual(len(data) - 8, struct.unpack("<I", data[4:8])[0])
        self.assertEqual(b"AVI ", data[8:12])
        movi = data.index(b"movi")
        index = data.index(b"idx1")
        self.assertEqual(160, struct.unpack("<I", data[index + 4:index + 8])[0])
        for frame in range(10):
            name, flags, offset, length = struct.unpack("<4sIII", data[index + 8 + frame * 16:index + 24 + frame * 16])
            self.assertEqual((b"00db", 16, 96 * 64 * 3), (name, flags, length))
            self.assertEqual(b"00db", data[movi + offset:movi + offset + 4])
        self.assertEqual(len(data), report["sizeBytes"])

    def test_video_refuses_invalid_duration_and_existing_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.avi"
            for value in (0, 121, True, 1.5):
                with self.assertRaises(AssertionError):
                    runner.make_video(path, value)
            path.write_bytes(b"owned sentinel")
            with self.assertRaises(FileExistsError):
                runner.make_video(path, 1)
            self.assertEqual(b"owned sentinel", path.read_bytes())

    def test_ipc_does_not_treat_missing_property_as_successful_clock(self):
        ipc = runner.IPC("fixture")
        ipc.stream = io.BytesIO()
        ipc.responses.put({"request_id": 1, "error": "property unavailable"})
        self.assertIsNone(ipc.get("time-pos"))
        ipc.responses.put({"request_id": 2, "error": "property unavailable"})
        with self.assertRaises(AssertionError):
            ipc.command("seek", 90)

    def test_ipc_failure_and_overlong_message_are_not_accepted(self):
        ipc = runner.IPC("fixture")
        ipc.stream = io.BytesIO()
        ipc.responses.put({"request_id": 1, "error": "invalid parameter"})
        with self.assertRaises(AssertionError):
            ipc.command("seek", 90)
        ipc.stream = io.BytesIO(b"x" * 65537 + b"\n")
        ipc.read()
        self.assertEqual(["AssertionError"], ipc.errors)

    def test_logs_redact_media_tickets(self):
        self.assertNotIn("a" * 64, runner.redact("http://localhost/media/" + "a" * 64))
        for text in ("lavf://http://127.0.0.1:3456/owned-canary", 'HTTPS://127.0.0.1:3456/owned-canary'):
            self.assertNotIn("127.0.0.1", runner.redact(text))
            self.assertNotIn("owned-canary", runner.redact(text))

    def test_reference_payloads_only_target_the_owned_canary(self):
        url = "http://127.0.0.1:3456/" + "a" * 32 + "/segment.ts"
        m3u = runner.reference_payload("m3u", url).decode()
        hls = runner.reference_payload("hls-lavf", url).decode()
        self.assertEqual(["lavf://" + url], [line for line in m3u.splitlines() if not line.startswith("#")])
        self.assertEqual([url], [line for line in hls.splitlines() if not line.startswith("#")])
        self.assertIn("#EXT-X-ENDLIST", hls)
        self.assertNotIn("#EXT-X", m3u)
        for invalid in (url.replace("127.0.0.1", "example.org"), url + "?redirect=other", url + "#fragment",
                        url.replace("http://", "file://"), url.replace("127.0.0.1", "user@127.0.0.1"),
                        url.replace("a" * 32, "../other")):
            with self.subTest(invalid=invalid), self.assertRaises(AssertionError): runner.reference_payload("m3u", invalid)

    def test_owned_canary_records_counts_without_retaining_urls(self):
        canary = runner.ReferenceCanary()
        self.addCleanup(canary.close)
        opener = runner.urllib.request.build_opener(runner.urllib.request.ProxyHandler({}))
        with self.assertRaises(runner.urllib.error.HTTPError) as caught:
            opener.open(canary.url, timeout=3)
        self.assertEqual(503, caught.exception.code)
        caught.exception.close()
        self.assertEqual({"referenceRequests": 1, "unexpectedRequests": 0, "budgetExceeded": False}, canary.counts())
        self.assertNotIn("http", json.dumps(canary.counts()))
        with self.assertRaises(runner.urllib.error.HTTPError) as caught:
            opener.open(canary.url + "-wrong", timeout=3)
        self.assertEqual(404, caught.exception.code)
        caught.exception.close()
        self.assertEqual(1, canary.counts()["unexpectedRequests"])

    def test_reference_attempt_requires_real_eof_or_error_after_start_not_idle_redirect_or_quit(self):
        with tempfile.TemporaryDirectory() as directory:
            journal = Path(directory) / "events.jsonl"
            self.assertIsNone(runner.playback_completion(journal, 123))
            start = {"event": "start-file", "pid": 123}
            for events in ([], [start], [start, {"event": "file-loaded", "pid": 123}],
                           *([start, {"event": "end-file", "pid": 123, "reason": reason, "errorPresent": False}]
                             for reason in ("stop", "quit", "redirect", "unknown"))):
                journal.write_text("".join(json.dumps(item) + "\n" for item in events))
                self.assertIsNone(runner.playback_completion(journal, 123))
            for reason in ("eof", "error"):
                events = [start, {"event": "end-file", "pid": 123.0, "reason": reason, "errorPresent": reason == "error"}]
                journal.write_text("".join(json.dumps(item) + "\n" for item in events))
                self.assertEqual(reason, runner.playback_completion(journal, 123)["outcome"])
                with journal.open("ab") as stream: stream.write(b'{"event":')
                self.assertIsNone(runner.playback_completion(journal, 123), "A partially written following event is not terminal")

    def test_reference_events_reject_foreign_pid_unknown_fields_overflow_and_no_load_attempt(self):
        with tempfile.TemporaryDirectory() as directory:
            journal = Path(directory) / "events.jsonl"
            start = {"event": "start-file", "pid": 123}
            end = {"event": "end-file", "pid": 123, "reason": "error", "errorPresent": True}
            for events in ([dict(start, pid=999), end], [dict(start, pid=True)], [end],
                           [dict(start, url="must-not-retain")], [start, dict(end, reason="unbounded text")],
                           [start, dict(end, event="overflow")], [start] * 65):
                journal.write_text("".join(json.dumps(item) + "\n" for item in events))
                with self.assertRaises(AssertionError): runner.playback_completion(journal, 123)
            journal.write_bytes(b"x" * 32769)
            with self.assertRaises(AssertionError): runner.playback_completion(journal, 123)

    def test_reference_control_must_trigger_canary_and_production_must_not_add_requests(self):
        good = {"referenceRequests": 2, "unexpectedRequests": 0, "budgetExceeded": False}
        self.assertEqual(0, runner.verify_reference_counts(good, good)["productionRequests"])
        for control, after in ((dict(good, referenceRequests=0), dict(good, referenceRequests=0)),
                               (good, dict(good, referenceRequests=3)), (good, dict(good, unexpectedRequests=1)),
                               (dict(good, budgetExceeded=True), dict(good, budgetExceeded=True))):
            with self.assertRaises(AssertionError): runner.verify_reference_counts(control, after)

    def test_reference_flow_uses_same_config_and_production_api_without_security_argument_override(self):
        for kind in ("m3u", "hls-lavf"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                work = Path(directory).resolve()
                source = {"directory": work / "source", "process": Mock(pid=30)}
                source["directory"].mkdir()
                (source["directory"] / "fixture.avi").write_bytes(b"owned initial video")
                reader = {"origin": "http://127.0.0.1:3456", "process": Mock(pid=20)}
                reader["process"].poll.return_value = source["process"].poll.return_value = None
                configuration = work / "config"
                configuration.mkdir()
                observer = Mock(errors=[])
                observer.current.return_value = []
                control, production = Mock(address=str(work / "ipc")), Mock(address=str(work / "ipc"))
                child = Mock(pid=100)
                canary = Mock(url="http://127.0.0.1:3457/" + "a" * 32 + "/segment.ts")
                canary.counts.return_value = {"referenceRequests": 1, "unexpectedRequests": 0, "budgetExceeded": False}
                context = {"work": work, "executable": work / "unused-mpv", "reader": reader, "source": source,
                    "observer": observer, "address": str(work / "ipc"), "configuration": configuration,
                    "originalConfiguration": "load-scripts=no\n", "environment": {"MPV_HOME": str(configuration)},
                    "reference": {"opaque": "resource"}, "streams": [], "children": [], "ipcs": [], "canaries": [], "trap": Mock(hits=0)}
                report = {}
                try:
                    with patch.object(runner, "ReferenceCanary", return_value=canary), \
                         patch.object(runner, "IPC", side_effect=[control, production]), \
                         patch.object(runner.subprocess, "Popen", return_value=child) as spawn, \
                         patch.object(runner, "reference_asset", return_value=({"opaque": "asset"}, reader["origin"] + "/federation/local/media/" + "b" * 64)) as resolve, \
                         patch.object(runner, "observed_player", side_effect=[{"pid": 100}, {"pid": 200}]), \
                         patch.object(runner, "playback_completion", return_value={"outcome": "error", "events": [], "passed": True}), \
                         patch.object(runner, "api", return_value={"launched": True}) as api:
                        runner.exercise_references(kind, context, report)
                    self.assertEqual(1, spawn.call_count)
                    arguments = spawn.call_args.args[0]
                    self.assertIn("--access-references=yes", arguments)
                    self.assertNotIn("--access-references=no", arguments)
                    production_call = api.call_args_list[-1]
                    self.assertEqual({"assetRef": {"opaque": "asset"}, "mode": "player"}, production_call.args[3])
                    self.assertEqual(2, resolve.call_count)
                    self.assertEqual([], context["ipcs"])
                    self.assertTrue(report["embeddedReferences"][kind]["passed"])
                    self.assertEqual(0, report["embeddedReferences"][kind]["canary"]["productionRequests"])
                    self.assertEqual(kind == "hls-lavf", "demuxer=lavf\n" in (configuration / "mpv.conf").read_text())
                    self.assertEqual(kind == "hls-lavf", "curl-enabled=no\n" in (configuration / "mpv.conf").read_text())
                    self.assertEqual(kind == "hls-lavf", report["embeddedReferences"][kind]["curlDisabled"])
                    self.assertEqual(kind == "m3u", "playlist-inherit-options=yes\n" in (configuration / "mpv.conf").read_text())
                    self.assertEqual(kind == "m3u", report["embeddedReferences"][kind]["playlistInheritsPerFileOptions"])
                    script = (work / (kind + "-events.lua")).read_text()
                    self.assertNotIn("set_property", script)
                    self.assertNotIn("command", script)
                    self.assertNotIn("url", script.lower())
                finally:
                    for stream in context["streams"]: stream.close()

    def test_retained_unicode_log_is_utf8_bounded_and_redacted(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            results = root / "results"
            results.mkdir()
            log = root / "owned.log"
            log.write_bytes(b"x" * (150 * 1024) + ("\n媒体 © \ufffd " + "a" * 64).encode("utf-8"))
            runner.retain_log(log, results)
            retained = (results / log.name).read_bytes()
            self.assertLessEqual(len(retained), 128 * 1024)
            self.assertIn("媒体 © \ufffd", retained.decode("utf-8"))
            self.assertNotIn("a" * 64, retained.decode("utf-8"))

    def test_seek_waits_for_completed_seek_and_numeric_target_position(self):
        for seeking, position, expected in ((True, 90, False), (None, 90, False),
                                             (False, None, False), (False, True, False),
                                             (False, 1, False), (False, 90, True)):
            with self.subTest(seeking=seeking, position=position):
                ipc = Mock()
                ipc.get.side_effect = lambda name: {"seeking": seeking, "time-pos": position}[name]
                self.assertEqual(runner.seek_reached(ipc, 90), expected)

    def test_parent_process_read_uses_only_owned_pid_and_parent_field(self):
        for system in ("nt", "posix"):
            with self.subTest(system=system), patch.object(runner.os, "name", system), \
                    patch.object(runner.subprocess, "check_output", return_value=" 123 \n") as command:
                self.assertEqual(runner.parent_pid(456), 123)
                arguments = command.call_args.args[0]
                self.assertEqual(command.call_args.kwargs["timeout"], 5)
                if system == "nt":
                    self.assertIn("ProcessId = 456", arguments[-1])
                    self.assertIn("ExpandProperty ParentProcessId", arguments[-1])
                    self.assertNotIn("CommandLine", arguments[-1])
                else:
                    self.assertEqual(arguments, ["ps", "-p", "456", "-o", "ppid="])
        with patch.object(runner.subprocess, "check_output") as command:
            for invalid in (True, 0, -1, "456"):
                with self.assertRaises(AssertionError):
                    runner.parent_pid(invalid)
            command.assert_not_called()
        for invalid in ("", "0", "123\n456", "parent=123"):
            with patch.object(runner.subprocess, "check_output", return_value=invalid), self.assertRaises(AssertionError):
                runner.parent_pid(456)

    def pipe(self, *, writes=None, reads=None, mode_ok=True, handle=123):
        writes, reads = list(writes or []), list(reads or [])
        state = {"error": 232, "sent": bytearray()}

        def write(handle, data, length, count, overlapped):
            amount = writes.pop(0) if writes else length
            count._obj.value = amount
            state["sent"].extend(ctypes.string_at(data, amount))
            return 1

        def read(handle, data, length, count, overlapped):
            if not reads:
                return 0
            value = reads.pop(0)
            if isinstance(value, int):
                state["error"] = value
                return 0
            value, remainder = value[:length], value[length:]
            if remainder:
                reads.insert(0, remainder)
            ctypes.memmove(data, value, len(value))
            count._obj.value = len(value)
            return 1

        kernel = types.SimpleNamespace(
            CreateFileW=NativeFunction(lambda *args: handle),
            SetNamedPipeHandleState=NativeFunction(lambda handle, mode, *_: mode_ok and mode._obj.value == 1),
            WriteFile=NativeFunction(write), ReadFile=NativeFunction(read), CloseHandle=NativeFunction(lambda _: 1))
        return kernel, state

    def test_windows_pipe_uses_nonblocking_byte_mode_and_preserves_partial_writes(self):
        kernel, state = self.pipe(writes=[0, 2, 2], reads=[232, b'{"event": "idle"}\n{"requ', b'est_id": 1}\n'])
        with patch.object(runner.ctypes, "WinDLL", return_value=kernel, create=True), \
                patch.object(runner.ctypes, "get_last_error", side_effect=lambda: state["error"], create=True), \
                patch.object(runner.time, "monotonic", return_value=0), patch.object(runner.time, "sleep"):
            pipe = runner.WindowsPipe(r"\\.\pipe\fixture")
            pipe.write(b"test", 5)
            self.assertEqual(pipe.readline(5), b'{"event": "idle"}\n')
            self.assertEqual(pipe.readline(5), b'{"request_id": 1}\n')
            pipe.close()
            pipe.close()
        self.assertEqual(state["sent"], b"test")
        self.assertEqual(kernel.CreateFileW.calls[0][1:], (0xc0000000, 0, None, 3, 0, None))
        self.assertEqual(kernel.CreateFileW.restype, ctypes.c_void_p)
        self.assertEqual(kernel.CloseHandle.calls, [(123,)])

    def test_windows_pipe_no_data_and_full_write_buffer_obey_deadline(self):
        for operation in ("read", "write"):
            kernel, state = self.pipe(writes=[0, 0, 0])
            with self.subTest(operation=operation), \
                    patch.object(runner.ctypes, "WinDLL", return_value=kernel, create=True), \
                    patch.object(runner.ctypes, "get_last_error", side_effect=lambda: state["error"], create=True), \
                    patch.object(runner.time, "monotonic", side_effect=[0, 1, 2]), \
                    patch.object(runner.time, "sleep"):
                pipe = runner.WindowsPipe(r"\\.\pipe\fixture")
                with self.assertRaises(TimeoutError):
                    pipe.readline(2) if operation == "read" else pipe.write(b"test", 2)
                pipe.close()
            self.assertEqual(kernel.CloseHandle.calls, [(123,)])

    def test_windows_pipe_refuses_oversized_message_and_broken_peer(self):
        for chunks, error in (([b"x" * 65536], AssertionError), ([109], OSError)):
            kernel, state = self.pipe(reads=chunks)
            with self.subTest(error=error), patch.object(runner.ctypes, "WinDLL", return_value=kernel, create=True), \
                    patch.object(runner.ctypes, "get_last_error", side_effect=lambda: state["error"], create=True), \
                    patch.object(runner.time, "monotonic", return_value=0):
                pipe = runner.WindowsPipe(r"\\.\pipe\fixture")
                with self.assertRaises(error):
                    pipe.readline(1)
                pipe.close()

    def test_windows_pipe_closes_handle_if_nonblocking_mode_cannot_be_set(self):
        kernel, _ = self.pipe(mode_ok=False)
        with patch.object(runner.ctypes, "WinDLL", return_value=kernel, create=True), \
                patch.object(runner.ctypes, "get_last_error", return_value=5, create=True):
            with self.assertRaises(OSError):
                runner.WindowsPipe(r"\\.\pipe\fixture")
        self.assertEqual(kernel.CloseHandle.calls, [(123,)])

    def test_windows_ipc_does_not_start_reader_and_shares_one_io_deadline(self):
        pipe = Mock()
        pipe.readline.side_effect = [b'{"event":"idle"}\n', b'{"request_id":1,"error":"success","data":42}\n']
        with patch.object(runner.os, "name", "nt"), patch.object(runner, "WindowsPipe", return_value=pipe), \
                patch.object(runner.threading, "Thread") as thread, \
                patch.object(runner.time, "monotonic", return_value=10):
            ipc = runner.IPC("fixture").connect(15)
            self.assertEqual(ipc.command("get_property", "pid", timeout=3), 42)
            ipc.close()
        thread.assert_not_called()
        self.assertEqual(pipe.write.call_args.args[1], 13)
        self.assertEqual([call.args[0] for call in pipe.readline.call_args_list], [13, 13])
        pipe.close.assert_called_once_with()

    def test_failed_log_retention_still_removes_fixture_and_writes_failed_report(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            executable = directory / "mpv"
            executable.write_bytes(b"fixture")
            provenance = directory / "provenance.json"
            provenance.write_text(json.dumps({"passed": True, "rid": "osx-arm64", "executable": str(executable),
                                              "executableSHA256": "fixture"}))
            results = (directory / "results").resolve()
            args = types.SimpleNamespace(mpv=executable, results_directory=results, provenance=provenance,
                                         rid="osx-arm64")
            roots = []

            def video(path):
                roots.append(path.parent)
                (path.parent / "fixture.log").write_text("fixture log")
                raise AssertionError("injected fixture failure")

            original_write = Path.write_text

            def write(path, *args, **kwargs):
                if path == results / "fixture.log":
                    raise PermissionError("injected log retention failure")
                return original_write(path, *args, **kwargs)

            with patch.object(runner.base, "require_hosted_runner"), \
                    patch.object(runner.base, "sha256", return_value="fixture"), \
                    patch.object(runner.subprocess, "check_output", return_value="fixture"), \
                    patch.object(runner, "make_video", side_effect=video), \
                    patch.dict(runner.os.environ, {"RUNNER_TEMP": str(directory)}), \
                    patch.object(Path, "write_text", write), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.run(args), 1)
            report = json.loads((results / "report.json").read_text())
            self.assertTrue(report["ownedFilesRemoved"])
            self.assertTrue(all(not path.exists() for path in roots))
            self.assertEqual(report["cleanupErrors"], [{"stage": "retain-sanitized-log", "type": "PermissionError"}])


if __name__ == "__main__":
    unittest.main()
