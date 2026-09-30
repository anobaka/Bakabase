"""Exercise scheduling and failure gates without invoking dotnet."""

from contextlib import redirect_stderr, redirect_stdout
import io
import json
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest
from unittest.mock import patch

import run_module_tests as runner


class ModuleRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "Build.sln").touch()
        self.names = ["A.Tests", "B.Tests", "C.Tests", "D.Tests"]
        projects = []
        for name in ["App", *self.names]:
            project = self.root / name / f"{name}.csproj"
            project.parent.mkdir()
            project.touch()
            projects.append(str(project.relative_to(self.root)).replace("/", "\\"))
        self.solution_filter = self.root / "Modules.slnf"
        self.solution_filter.write_text(json.dumps({
            "solution": {"path": "Build.sln", "projects": projects},
        }), encoding="utf-8")
        self.results = self.root / "results"
        self.output = io.StringIO()
        self.errors = io.StringIO()

    def run_runner(self, fake):
        with patch.object(runner.subprocess, "run", side_effect=fake), \
                redirect_stdout(self.output), redirect_stderr(self.errors):
            return runner.run(self.solution_filter, self.results, workers=2)

    def test_restore_or_build_failure_prevents_all_test_processes(self):
        for failure_stage in ("restore", "build"):
            with self.subTest(stage=failure_stage):
                stages = []

                def fake(command, **kwargs):
                    stages.append(command[1])
                    return subprocess.CompletedProcess(command, 9 if command[1] == failure_stage else 0)

                self.assertEqual(1, self.run_runner(fake))
                self.assertEqual(["restore"] if failure_stage == "restore" else ["restore", "build"], stages)
        self.assertFalse(self.results.exists())

    def test_two_workers_finish_every_project_and_preserve_multiple_failures(self):
        lock = threading.Lock()
        first_pair = threading.Barrier(2, timeout=5)
        stages = []
        active = 0
        peak = 0
        launched = []
        commands = []

        def fake(command, **kwargs):
            nonlocal active, peak
            if command[1] != "test":
                stages.append(command[1])
                return subprocess.CompletedProcess(command, 0)
            name = Path(command[2]).stem
            with lock:
                self.assertEqual(["restore", "build"], stages)
                active += 1
                peak = max(peak, active)
                launched.append(name)
                commands.append(command)
                join_first_pair = len(launched) <= 2
            try:
                if join_first_pair:
                    first_pair.wait()
                kwargs["stdout"].write(f"output from {name}\n")
                directory = Path(command[command.index("--results-directory") + 1])
                (directory / "results.trx").write_text(f"<results>{name}</results>", encoding="utf-8")
                return subprocess.CompletedProcess(command, 1 if name in ("A.Tests", "C.Tests") else 0)
            finally:
                with lock:
                    active -= 1

        self.assertEqual(1, self.run_runner(fake))
        self.assertEqual(2, peak, "the runner must overlap test processes and never exceed the bound")
        self.assertCountEqual(self.names, launched, "one failure must not cancel pending projects")
        for command in commands:
            self.assertIn("--no-build", command)
            self.assertIn("--no-restore", command)
        for name in self.names:
            self.assertEqual(f"output from {name}\n", (self.results / name / "test.log").read_text())
            self.assertTrue((self.results / name / "results.trx").is_file())
        self.assertIn("2 passed, 2 failed", self.output.getvalue())
        self.assertIn("Test project failed: A.Tests", self.errors.getvalue())
        self.assertIn("Test project failed: C.Tests", self.errors.getvalue())

    def test_process_launch_failure_is_reported_and_other_projects_still_run(self):
        launched = []

        def fake(command, **kwargs):
            if command[1] == "test":
                name = Path(command[2]).stem
                launched.append(name)
                if name == "A.Tests":
                    raise OSError("unable to launch test process")
            return subprocess.CompletedProcess(command, 0)

        self.assertEqual(1, self.run_runner(fake))
        self.assertCountEqual(self.names, launched)
        self.assertIn("unable to launch test process", self.output.getvalue())
        self.assertIn("3 passed, 1 failed", self.output.getvalue())

    def test_success_requires_every_project_to_pass(self):
        self.assertEqual(0, self.run_runner(lambda command, **kwargs: subprocess.CompletedProcess(command, 0)))
        self.assertIn("4 passed, 0 failed", self.output.getvalue())
        self.assertEqual("", self.errors.getvalue())


if __name__ == "__main__":
    unittest.main()
