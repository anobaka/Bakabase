#!/usr/bin/env python3
"""Run real MSTest executables in class batches with disposable test databases.

The legacy TestServiceBuilder leaves one SQLite directory per test. A whole
Bakabase.Tests process can therefore fill a developer machine or CI disk. Each
class here gets its own process and TMPDIR; only that owned directory is removed
after the process exits. Logs and TRX reports survive in the results directory.

Discovery uses the SDK's VSTest adapter because MTP --list-tests prints display
names, which do not identify a class. Execution always uses the MSTest executable
and --minimum-expected-tests 1, never a potentially empty dotnet test invocation.
"""

import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time


REPOSITORY = Path(__file__).resolve().parents[2]
COUNT_PATTERN = re.compile(r"^  (total|failed|succeeded|skipped): (\d+)", re.MULTILINE)


def run_logged(command, log, *, environment, timeout=1800):
    with log.open("w", encoding="utf-8") as output:
        try:
            process = subprocess.run(
                command, cwd=REPOSITORY, env=environment, stdout=output,
                stderr=subprocess.STDOUT, timeout=timeout, check=False,
            )
            return process.returncode
        except subprocess.TimeoutExpired:
            output.write(f"\nRunner timeout after {timeout} seconds.\n")
            return 124


def discover(project, args, directory, environment):
    if not args.no_build:
        code = run_logged(
            [args.dotnet, "build", str(project), "--configuration", args.configuration, "--nologo"],
            directory / "build.log", environment=environment,
        )
        if code:
            raise RuntimeError(f"Build failed ({code}); see {directory / 'build.log'}")
    target = subprocess.run(
        [args.dotnet, "msbuild", str(project), "-nologo", "-getProperty:TargetPath",
         f"-property:Configuration={args.configuration}"],
        cwd=REPOSITORY, env=environment, capture_output=True, text=True, check=True,
    ).stdout.strip()
    assembly = Path(target)
    if not assembly.is_file():
        raise RuntimeError(f"The test assembly does not exist: {target}")
    listing = directory / "discovered-tests.txt"
    code = run_logged(
        [args.dotnet, "vstest", str(assembly), "/ListFullyQualifiedTests",
         f"/ListTestsTargetPath:{listing}"],
        directory / "discovery.log", environment=environment,
    )
    if code or not listing.is_file():
        raise RuntimeError(f"Test discovery failed ({code}); see {directory / 'discovery.log'}")
    methods = [line.strip() for line in listing.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if not methods or any("." not in method for method in methods):
        raise RuntimeError("Discovery did not produce a nonempty list of fully qualified tests.")
    classes = sorted({method.rsplit(".", 1)[0] for method in methods})
    if args.classes:
        missing = set(args.classes) - set(classes)
        if missing:
            raise RuntimeError(f"Requested test classes were not discovered: {', '.join(sorted(missing))}")
        classes = [name for name in classes if name in args.classes]
    return assembly, classes


def run_class(assembly, class_name, args, directory, environment):
    log = directory / f"{class_name}.log"
    started = time.monotonic()
    result = {"class": class_name, "log": str(log), "counts": {}}
    # Set all conventional temp variables, so both managed and native helpers use
    # this one owned directory. No global temp sweep or SQLite fixture changes.
    with tempfile.TemporaryDirectory(prefix="bakabase-test-class-") as temporary:
        child_environment = dict(environment, TMPDIR=temporary, TMP=temporary, TEMP=temporary)
        result["exitCode"] = run_logged(
            [args.dotnet, str(assembly), "--filter", f"FullyQualifiedName~{class_name}.",
             "--minimum-expected-tests", "1", "--report-trx",
             "--results-directory", str(directory), "--report-trx-filename", f"{class_name}.trx"],
            log, environment=child_environment, timeout=args.timeout,
        )
    result["seconds"] = round(time.monotonic() - started, 3)
    output = log.read_text(encoding="utf-8", errors="replace")
    result["counts"] = {key: int(value) for key, value in COUNT_PATTERN.findall(output)}
    # A crash or a silently empty test host must not look like a successful batch.
    if result["counts"].get("total", 0) < 1:
        result["error"] = "The runner did not report a nonempty completed test summary."
        result["exitCode"] = result["exitCode"] or 1
    if result["counts"].get("failed", 0):
        result["exitCode"] = result["exitCode"] or 1
    print(json.dumps(result, ensure_ascii=False), flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", action="append", type=Path,
                        help="Test csproj; repeat for multiple projects. Defaults to Bakabase.Tests.")
    parser.add_argument("--dotnet", default="dotnet", help=".NET CLI executable or absolute path.")
    parser.add_argument("--configuration", default="Debug")
    parser.add_argument("--jobs", type=int, choices=(1, 2), default=1,
                        help="Concurrent class processes. Projects are built sequentially.")
    parser.add_argument("--results-directory", type=Path,
                        help="Retained logs, TRX reports and summary.json; defaults to a new temp directory.")
    parser.add_argument("--no-build", action="store_true", help="Use already built test assemblies.")
    parser.add_argument("--class", dest="classes", action="append",
                        help="Run only this discovered fully qualified class; repeat as needed.")
    parser.add_argument("--timeout", type=int, default=1800, help="Maximum seconds per class (default 1800).")
    args = parser.parse_args()
    if args.timeout < 1:
        parser.error("--timeout must be positive")
    executable = shutil.which(args.dotnet)
    if not executable:
        parser.error(f"Cannot find the .NET executable: {args.dotnet}")
    args.dotnet = str(Path(executable).resolve())
    projects = args.project or [REPOSITORY / "src/tests/Bakabase.Tests/Bakabase.Tests.csproj"]
    projects = [project.resolve() for project in projects]
    if len({project.stem for project in projects}) != len(projects):
        parser.error("Project names must be unique within a run.")
    results_directory = (args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-test-results-"))).resolve()
    results_directory.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, DOTNET_ROOT=str(Path(args.dotnet).parent),
                       TESTINGPLATFORM_TELEMETRY_OPTOUT="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    summary = {"projects": [], "counts": {}, "success": True}
    print(f"Test results: {results_directory}", flush=True)
    for project in projects:
        directory = results_directory / project.stem
        directory.mkdir(exist_ok=True)
        project_result = {"project": str(project), "classes": []}
        summary["projects"].append(project_result)
        try:
            assembly, classes = discover(project, args, directory, environment)
            with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as workers:
                futures = [workers.submit(run_class, assembly, name, args, directory, environment) for name in classes]
                for future in concurrent.futures.as_completed(futures):
                    project_result["classes"].append(future.result())
                    (directory / "classes.json").write_text(json.dumps(project_result["classes"], indent=2), encoding="utf-8")
            project_result["classes"].sort(key=lambda value: value["class"])
        except (OSError, RuntimeError, subprocess.SubprocessError) as error:
            project_result["error"] = str(error)
            print(f"ERROR: {error}", file=sys.stderr, flush=True)
        project_result["counts"] = {
            key: sum(batch["counts"].get(key, 0) for batch in project_result["classes"])
            for key in ("total", "succeeded", "failed", "skipped")
        }
        project_result["success"] = "error" not in project_result and bool(project_result["classes"]) and all(
            batch["exitCode"] == 0 for batch in project_result["classes"])
        summary["success"] &= project_result["success"]
        print(f"{project.stem}: {project_result['counts']} success={project_result['success']}", flush=True)
        summary["counts"] = {
            key: sum(item["counts"].get(key, 0) for item in summary["projects"])
            for key in ("total", "succeeded", "failed", "skipped")
        }
        (results_directory / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"Total: {summary['counts']}; success={summary['success']}", flush=True)
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
