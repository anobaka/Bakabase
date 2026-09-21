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
            for role in ("unified", "source", "client"):
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


if __name__ == "__main__":
    unittest.main()
