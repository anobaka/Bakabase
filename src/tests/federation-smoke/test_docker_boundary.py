#!/usr/bin/env python3
"""Owned Docker/HTTP failure guards. Never starts Docker or a native application."""
import importlib.util
from contextlib import closing
import json
from pathlib import Path
import signal
import sqlite3
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import docker_boundary_support as support

spec = importlib.util.spec_from_file_location("docker_boundary", Path(__file__).with_name("docker-boundary.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class BoundaryGuards(unittest.TestCase):
    def owned(self):
        owned = support.OwnedDocker(support.Deadline(30), "this-run")
        owned.service_id = "a" * 64
        owned.curl_id = "sha256:" + "b" * 64
        owned.curl_platform = "linux/arm64"
        return owned

    def test_helper_only_joins_owned_full_container_id_and_immutable_cached_image(self):
        owned = self.owned()
        args = owned.curl_command("owned-helper", "http://127.0.0.1:34567", "/app/info", "GET", None, False)
        self.assertEqual("container:" + owned.service_id, args[args.index("--network") + 1])
        self.assertEqual("never", args[args.index("--pull") + 1])
        self.assertIn(owned.curl_id, args)
        self.assertNotIn("host", args)
        self.assertEqual("http://127.0.0.1:34567/app/info", args[-1])
        for target in ("host", "user-container", "", "a" * 12):
            owned.service_id = target
            with self.subTest(target=target), self.assertRaises(support.BoundaryFailure):
                owned.curl_command("helper", "http://127.0.0.1:34567", "/", "GET", None, False)

    def test_helper_rejects_unowned_container_before_starting_any_process(self):
        owned = self.owned()
        foreign = [{"Config": {"Labels": {support.LABEL: "another-run"}}}]
        result = subprocess.CompletedProcess([], 0, json.dumps(foreign).encode(), b"")
        with patch.object(support.subprocess, "run", return_value=result) as command:
            with self.assertRaisesRegex(support.BoundaryFailure, "not owned"):
                owned.request("/app/info")
        self.assertEqual(1, command.call_count)
        self.assertEqual(["docker", "container", "inspect", owned.service_id], command.call_args.args[0])

    def test_helper_rejects_other_network_targets(self):
        owned = self.owned()
        for base, path in (("http://user-nas:34567", "/app/info"), ("http://127.0.0.1:34567", "//other/app/info")):
            with self.subTest(base=base, path=path), self.assertRaises(support.BoundaryFailure):
                owned.curl_command("helper", base, path, "GET", None, False)

    def test_credentials_are_stdin_only_and_successful_helper_is_removed(self):
        owned = self.owned()
        response = subprocess.CompletedProcess([], 0, b'{"outcome":"granted"}\n200', b"")
        with patch.object(owned, "inspect_owned", return_value={"Id": owned.service_id}), \
                patch.object(owned, "remove") as remove, \
                patch.object(support.subprocess, "run", return_value=response) as command:
            result = owned.request("/federation/local/peers/connect", "POST", {"code": "fixture-secret"})
        self.assertEqual("granted", result["outcome"])
        self.assertNotIn("fixture-secret", " ".join(command.call_args.args[0]))
        self.assertEqual({"code": "fixture-secret"}, json.loads(command.call_args.kwargs["input"]))
        self.assertIn("@-", command.call_args.args[0])
        remove.assert_called_once_with("container", owned.containers[0])

    def test_timed_out_helper_cannot_succeed_and_is_still_cleaned(self):
        owned = self.owned()
        with patch.object(owned, "inspect_owned", return_value={"Id": owned.service_id}), \
                patch.object(owned, "remove") as remove, \
                patch.object(support.subprocess, "run", side_effect=subprocess.TimeoutExpired(["docker"], 1)):
            with self.assertRaisesRegex(support.BoundaryFailure, "timed out"):
                owned.request("/app/info")
        remove.assert_called_once_with("container", owned.containers[0])

    def test_cleanup_only_removes_registered_matching_label_resources(self):
        owned = self.owned()
        owned.containers = ["owned-main", "foreign-container"]
        owned.networks = ["owned-network"]
        commands = []
        def docker(*args, **kwargs):
            commands.append(args)
            if args[1] == "inspect":
                labels = {support.LABEL: "another-run" if args[2] == "foreign-container" else owned.token}
                data = {"Labels": labels} if args[0] == "network" else {"Config": {"Labels": labels}}
                return subprocess.CompletedProcess([], 0, json.dumps([data]).encode(), b"")
            return subprocess.CompletedProcess([], 0, b"", b"")
        with patch.object(owned, "command", side_effect=docker):
            errors = owned.cleanup()
            with self.assertRaisesRegex(support.BoundaryFailure, "unregistered"):
                owned.remove("container", "unrelated-user-container")
        self.assertEqual(["BoundaryFailure"], errors)
        self.assertIn(("container", "rm", "--force", "owned-main"), commands)
        self.assertIn(("network", "rm", "owned-network"), commands)
        self.assertFalse(any("foreign-container" in command for command in commands if "rm" in command))
        self.assertFalse(any("prune" in command for command in commands))
        self.assertEqual(["foreign-container"], owned.containers)

    def test_http_and_business_errors_never_count_as_success(self):
        for output in (b'{"ok":true}\n500', b'{"code":7,"data":{}}\n200', b'\n000', b'not JSON\n200'):
            with self.subTest(output=output), self.assertRaises(support.BoundaryFailure):
                support.parse_curl_response(output)
        self.assertEqual({"code": "Denied"}, support.parse_curl_response(b'{"code":"Denied"}\n403', expected=403))
        self.assertEqual(b"\x00\n\xff", support.parse_curl_response(b"\x00\n\xff\n206", expected=206, raw=True))

    def test_expired_deadline_or_disk_floor_prevents_starting_docker(self):
        for deadline in (support.Deadline(-1), support.Deadline(30, Path("/fixture"), 10)):
            owned = support.OwnedDocker(deadline)
            with patch.object(support.shutil, "disk_usage", return_value=types.SimpleNamespace(free=1)), \
                    patch.object(support.subprocess, "run") as process:
                with self.assertRaises(support.BoundaryFailure):
                    owned.command("info")
                process.assert_not_called()

    def test_native_cleanup_targets_its_group_even_after_leader_exits(self):
        process = Mock(pid=12345)
        with patch.object(support.os, "killpg") as kill:
            support.stop_native(process)
        self.assertEqual([(12345, signal.SIGTERM), (12345, signal.SIGKILL)], [call.args for call in kill.call_args_list])

    def test_sanitized_logs_omit_tickets_codes_and_bodies(self):
        result = support.sanitized_log(b'HTTP ticket-secret code-secret {"key":"secret"}\nSystem.IO.IOException: private path')
        self.assertEqual(["System.IO.IOException"], result["exceptionTypes"])
        self.assertNotIn("secret", json.dumps(result))

    def test_missing_or_duplicate_created_resources_cannot_pass(self):
        valid = [{"resourceId": i, "error": None} for i in range(1, 18)]
        self.assertEqual(list(range(1, 18)), runner.validate_created(valid))
        for items in ([], valid[:-1], valid[:-1] + [valid[0]], [{"resourceId": True}] * 17):
            with self.subTest(items=items), self.assertRaises(support.BoundaryFailure):
                runner.validate_created(items)

    def test_missing_database_is_not_created_by_integrity_verification(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(support.BoundaryFailure):
                runner.check_database(root)
            self.assertFalse((root / "bakabase_insideworld.db").exists())

    def test_database_requires_all_resources_and_no_play_history(self):
        for count, played in ((17, None), (16, None), (17, "2026-01-01")):
            with self.subTest(count=count, played=played), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                with closing(sqlite3.connect(root / "bakabase_insideworld.db")) as connection:
                    connection.execute("CREATE TABLE ResourcesV2 (Id INTEGER, PlayedAt TEXT)")
                    connection.executemany("INSERT INTO ResourcesV2 VALUES (?, ?)",
                                           [(number, played) for number in range(count)])
                    connection.commit()
                if count == 17 and played is None:
                    self.assertEqual(17, runner.check_database(root)["resources"])
                else:
                    with self.assertRaises(AssertionError):
                        runner.check_database(root)


if __name__ == "__main__":
    unittest.main()
