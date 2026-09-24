import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("browser_runner", Path(__file__).with_name("run.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class RunnerFailureEvidenceTests(unittest.TestCase):
    def test_host_summary_retains_failure_types_but_never_payloads_or_arguments(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            hosts = {}
            processes = {}
            for role in ("unified", "source"):
                directory = root / role
                directory.mkdir()
                hosts[role] = {"directory": str(directory)}
                processes[role] = SimpleNamespace(returncode=17)
                (root / (role + ".log")).write_text(
                    'FEDERATION_TEST_READY {"key":"ready-secret"}\n'
                    'Invite code: invitation-secret\n'
                    'System.IO.IOException: secret-message address already in use\n'
                    '   at Example.Server.Start(String secret-argument) in /private/secret-path:line 42\n')
            runner.retain_host_diagnostics(root, hosts, processes, root)
            for role in hosts:
                text = (root / (role + "-host.log")).read_text()
                result = json.loads(text)
                self.assertEqual(17, result["exitCode"])
                self.assertEqual(["System.IO.IOException"], result["exceptionTypes"])
                self.assertEqual(["Example.Server.Start"], result["stackMethods"])
                self.assertEqual(["address already in use"], result["failureCategories"])
                self.assertNotIn("secret", text)
                self.assertNotIn("FEDERATION_TEST_READY", text)

    def test_failed_build_records_failed_status_without_starting_hosts(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "web").mkdir()
            (root / "web/index.html").write_text("fixture")
            (root / "node_modules/playwright").mkdir(parents=True)
            (root / "node_modules/playwright/package.json").write_text("{}")
            results = root / "results"
            with patch.object(runner, "HERE", root), patch.object(sys, "argv", [
                "run.py", "--web-root", str(root / "web"), "--results-directory", str(results)
            ]), patch.object(runner.subprocess, "run", side_effect=subprocess.CalledProcessError(1, ["dotnet", "build"])), \
                    patch.object(runner.subprocess, "Popen") as spawn:
                with self.assertRaises(subprocess.CalledProcessError):
                    runner.main()
                spawn.assert_not_called()
            result = json.loads((results / "result.json").read_text())
            self.assertFalse(result["passed"])
            self.assertEqual("failed", result["status"])
            self.assertTrue((results / "Bakabase.Federation.TestHost-build.log").is_file())


KEYS = ("BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW", "BAKABASE_CLIENT_DATA_DIR",
        "BAKABASE_FEDERATION_TEST_REQUEST_LOG", "BAKABASE_FEDERATION_TEST_SERVER_NAME")


def run_with_fake_processes(root, browser_exit=0, request_log_lines=()):
    """Runs run.py's main() with every process faked: hosts write their ready file (and, when
    given, lines into their request log), the browser exits with `browser_exit`. Returns the
    hosts by role, the configs handed to the browser, and main()'s outcome."""
    (root / "web").mkdir()
    (root / "web/index.html").write_text("fixture")
    (root / "node_modules/playwright").mkdir(parents=True)
    (root / "node_modules/playwright/package.json").write_text("{}")
    dll = root / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
    dll.parent.mkdir(parents=True)
    dll.write_text("")
    hosts, configs = {}, []

    class FakeProcess:
        returncode = 0

        def __init__(self, command, env=None, **_):
            self.code = 0
            if command[1].endswith(".dll"):
                directory = Path(command[3])
                hosts[directory.name] = {"port": command[2], "directory": str(directory), "env": env,
                                         "command": command}
                directory.mkdir(parents=True, exist_ok=True)
                (directory / "ready").write_text(command[2])
                log = env.get("BAKABASE_FEDERATION_TEST_REQUEST_LOG")
                if log:
                    Path(log).write_text("".join(line + "\n" for line in request_log_lines))
            else:
                configs.append(json.loads(Path(command[2]).read_text()))
                self.code = browser_exit

        def poll(self):
            return None

        def wait(self, timeout=None):
            return self.code

    with patch.dict(runner.os.environ), patch.object(runner, "HERE", root), \
            patch.object(runner, "ROOT", root), patch.object(runner, "stop_fixture_process"), \
            patch.object(runner.subprocess, "Popen", FakeProcess), patch.object(sys, "argv", [
                "run.py", "--no-build", "--web-root", str(root / "web"),
                "--results-directory", str(root / "results")]):
        for key in KEYS:
            runner.os.environ.pop(key, None)
        try:
            outcome = runner.main()
        except AssertionError as error:
            outcome = error
    return hosts, configs, outcome


class RunnerCompositionTests(unittest.TestCase):
    def test_only_the_unified_host_is_composed_as_the_desktop_app(self):
        """The unified fixture manages servers and reads an old thin client's data; both servers
        record what reaches them. No thin client is started: the removed program is only a
        directory the browser stage fills. Checked without .NET or a browser."""
        with tempfile.TemporaryDirectory() as temp:
            hosts, configs, outcome = run_with_fake_processes(Path(temp))
            self.assertEqual(0, outcome)
            self.assertEqual({"unified", "source"}, set(hosts))
            unified, source = (hosts[role] for role in ("unified", "source"))
            window = f"http://localhost:{unified['port']}"
            self.assertEqual(window, unified["env"]["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"])
            [config] = configs
            legacy = config["legacyClient"]["directory"]
            self.assertEqual(legacy, unified["env"]["BAKABASE_CLIENT_DATA_DIR"])
            self.assertTrue(Path(legacy).is_absolute())
            self.assertFalse(Path(legacy).exists(), "The old thin client's pairing is the browser stage's to write")
            self.assertNotIn(legacy, [host["directory"] for host in hosts.values()])
            logs = [host["env"]["BAKABASE_FEDERATION_TEST_REQUEST_LOG"] for host in (unified, source)]
            self.assertTrue(all(Path(log).is_absolute() for log in logs))
            self.assertNotEqual(*logs)
            for key in KEYS[:2]:
                self.assertNotIn(key, source["env"])
            self.assertEqual(window, config["hosts"]["unified"]["window"])
            self.assertEqual(logs, [config["hosts"][role]["requestLog"] for role in ("unified", "source")])
            self.assertNotIn("window", config["hosts"]["source"])
            self.assertEqual({"unified", "source"}, set(config["hosts"]))

    def test_servers_have_names_of_their_own(self):
        """A check by name can tell the servers apart only if the fixtures do not all carry the
        machine's name."""
        with tempfile.TemporaryDirectory() as temp:
            hosts, _, _ = run_with_fake_processes(Path(temp))
            names = [hosts[role]["env"]["BAKABASE_FEDERATION_TEST_SERVER_NAME"] for role in ("unified", "source")]
            self.assertEqual(len(set(names)), len(names))

    def test_no_host_reports_to_analytics(self):
        """Every analytics key the Service reads is blank, and tracking off, for every host the
        runner starts — the test host enforces the same itself."""
        with tempfile.TemporaryDirectory() as temp:
            hosts, configs, _ = run_with_fake_processes(Path(temp))
            first_launch = configs[0]["firstLaunch"]["env"]
            for env in [host["env"] for host in hosts.values()] + [{**runner.os.environ, **first_launch}]:
                for key, value in runner.ANALYTICS_OFF.items():
                    self.assertEqual(value, env.get(key), key)
            self.assertEqual("false", runner.ANALYTICS_OFF["App__EnableAnonymousDataTracking"])
            for key in ("Analytics__Clarity__ProjectId", "Analytics__Ga4__MeasurementId",
                        "Analytics__Sentry__FrontendDsn", "Analytics__Sentry__BackendDsn",
                        "Analytics__PostHog__ApiKey"):
                self.assertEqual("", runner.ANALYTICS_OFF[key])

    def test_the_first_launch_fixture_is_a_fresh_desktop_install_beside_an_old_thin_client(self):
        """Started by the browser stage once an old thin client's pairing is on disk: its own port
        and data directory, that thin client's data to import from, no recorded requests, no
        resources."""
        with tempfile.TemporaryDirectory() as temp:
            hosts, configs, _ = run_with_fake_processes(Path(temp))
            self.assertNotIn("first-launch", hosts, "The runner must leave starting it to the browser stage")
            spec = configs[0]["firstLaunch"]
            port = spec["command"][2]
            self.assertTrue(spec["command"][1].endswith("Bakabase.Federation.TestHost.dll"))
            self.assertEqual([port, spec["directory"], "0"], spec["command"][2:])
            self.assertNotIn(port, [host["port"] for host in hosts.values()])
            self.assertFalse(Path(spec["directory"]).exists(), "A first launch needs a new data directory")
            self.assertEqual(f"http://localhost:{port}", spec["env"]["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"])
            self.assertEqual(spec["window"], spec["env"]["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"])
            self.assertEqual(configs[0]["legacyClient"]["directory"], spec["env"]["BAKABASE_CLIENT_DATA_DIR"])
            self.assertNotIn("BAKABASE_FEDERATION_TEST_REQUEST_LOG", spec["env"])
            self.assertNotEqual(spec["env"]["BAKABASE_FEDERATION_TEST_SERVER_NAME"],
                                hosts["source"]["env"]["BAKABASE_FEDERATION_TEST_SERVER_NAME"])


class RunnerRequestLogTests(unittest.TestCase):
    LINES = (
        json.dumps({"id": 1, "method": "GET", "path": "/hub/ui", "site": "", "dest": "",
                    "origin": "http://127.0.0.1:34650", "websocket": True, "ticket": False,
                    "cookie": False, "signature": "none", "device": None, "authorization": "Bakabase-Device x"}),
        json.dumps({"id": 1, "status": 101, "denial": "", "csp": "", "body": "unexpected"}),
        "not json",
    )

    def test_a_failed_run_keeps_each_services_request_log_with_only_the_recorders_fields(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            _, _, outcome = run_with_fake_processes(root, browser_exit=1, request_log_lines=self.LINES)
            self.assertIsInstance(outcome, AssertionError)
            for role in ("unified", "source"):
                kept = [json.loads(line) for line in (root / "results" / (role + "-requests.jsonl")).read_text().splitlines()]
                self.assertEqual([
                    {"id": 1, "method": "GET", "path": "/hub/ui", "site": "", "dest": "",
                     "origin": "http://127.0.0.1:34650", "websocket": True, "ticket": False,
                     "cookie": False, "signature": "none", "device": None},
                    {"id": 1, "status": 101, "denial": "", "csp": ""},
                ], kept)

    def test_a_passing_run_keeps_none(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            _, _, outcome = run_with_fake_processes(root, request_log_lines=self.LINES)
            self.assertEqual(0, outcome)
            self.assertEqual([], sorted(path.name for path in (root / "results").glob("*-requests.jsonl")))


if __name__ == "__main__":
    unittest.main()
