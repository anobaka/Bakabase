#!/usr/bin/env python3
"""Build the module CI filter once, then run its test projects with bounded concurrency.

Bakabase.ModuleTests.slnf is the single project list. Its app entry point is built
with the tests; only projects named *.Tests.csproj are executed. Each test process
gets its own log and TRX directory, and failures do not cancel the other projects.
"""

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
import json
from pathlib import Path
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_FILTER = ROOT / "src/Bakabase.ModuleTests.slnf"


def test_projects(solution_filter):
    solution = json.loads(solution_filter.read_text(encoding="utf-8"))["solution"]
    solution_path = solution_filter.parent / solution["path"].replace("\\", "/")
    if not solution_path.is_file():
        raise ValueError(f"Solution does not exist: {solution_path}")
    projects = [
        (solution_path.parent / project.replace("\\", "/")).resolve()
        for project in solution["projects"]
    ]
    for project in projects:
        if not project.is_file():
            raise ValueError(f"Project does not exist: {project}")
    tests = [project for project in projects if project.name.endswith(".Tests.csproj")]
    if not tests:
        raise ValueError("The solution filter contains no *.Tests.csproj projects")
    if len({project.stem for project in tests}) != len(tests):
        raise ValueError("Test project names must be unique for separate result directories")
    return tests


def prepare(solution_filter, configuration):
    # Finish the shared project graph before any parallel test process can start.
    # No two dotnet test processes may concurrently write its obj/bin directories.
    commands = [
        ["dotnet", "restore", str(solution_filter), f"-p:Configuration={configuration}"],
        ["dotnet", "build", str(solution_filter), "--configuration", configuration, "--no-restore"],
    ]
    for command in commands:
        stage = command[1]
        print(f"Starting module {stage}", flush=True)
        started = time.monotonic()
        completed = subprocess.run(command, cwd=ROOT, check=False)
        print(f"Module {stage}: exit {completed.returncode}, {time.monotonic() - started:.1f} s", flush=True)
        if completed.returncode:
            return completed.returncode
    return 0


@dataclass(frozen=True)
class TestResult:
    project: Path
    returncode: int
    elapsed: float
    log: Path
    error: str = ""


def run_project(project, results_directory, configuration):
    directory = results_directory / project.stem
    log = directory / "test.log"
    started = time.monotonic()
    try:
        directory.mkdir(parents=True, exist_ok=True)
        command = [
            "dotnet", "test", str(project), "--configuration", configuration,
            "--no-build", "--no-restore", "--", "--report-trx",
            "--results-directory", str(directory),
        ]
        with log.open("w", encoding="utf-8") as output:
            completed = subprocess.run(command, cwd=ROOT, stdout=output,
                                       stderr=subprocess.STDOUT, check=False)
        return TestResult(project, completed.returncode, time.monotonic() - started, log)
    except OSError as error:
        return TestResult(project, 1, time.monotonic() - started, log, str(error))


def run(solution_filter, results_directory, workers=2, configuration="Debug"):
    if workers < 1:
        raise ValueError("workers must be positive")
    solution_filter = solution_filter.resolve()
    results_directory = results_directory.resolve()
    projects = test_projects(solution_filter)
    if prepare(solution_filter, configuration):
        print("::error::Module restore/build failed; tests were not started", file=sys.stderr)
        return 1

    results = []
    print(f"Running {len(projects)} module test projects with {workers} workers", flush=True)
    with ThreadPoolExecutor(max_workers=workers) as pool:
        pending = {
            pool.submit(run_project, project, results_directory, configuration): project
            for project in projects
        }
        for future in as_completed(pending):
            project = pending[future]
            try:
                result = future.result()
            except Exception as error:
                # Still await and report every other project if launching one failed.
                result = TestResult(project, 1, 0, results_directory / project.stem / "test.log", str(error))
            results.append(result)
            status = "PASS" if result.returncode == 0 else "FAIL"
            print(f"{status} {project.stem}: {result.elapsed:.1f} s, exit {result.returncode}; log: {result.log}", flush=True)
            if result.returncode:
                print(f"::group::{project.stem} failure output", flush=True)
                if result.error:
                    print(result.error, flush=True)
                if result.log.is_file():
                    print(result.log.read_text(encoding="utf-8", errors="replace"), flush=True)
                print("::endgroup::", flush=True)

    failures = sorted(result.project.stem for result in results if result.returncode)
    print(f"Module results: {len(results) - len(failures)} passed, {len(failures)} failed", flush=True)
    for project in failures:
        print(f"::error::Test project failed: {project}", file=sys.stderr)
    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--solution-filter", type=Path, default=DEFAULT_FILTER)
    parser.add_argument("--results-directory", type=Path, required=True)
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--configuration", default="Debug")
    args = parser.parse_args()
    return run(args.solution_filter, args.results_directory, args.workers, args.configuration)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError) as error:
        print(f"::error::Unable to run module tests: {error}", file=sys.stderr)
        sys.exit(1)
