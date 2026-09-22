import importlib.util
import contextlib
import ctypes
import io
import json
from pathlib import Path
import struct
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
