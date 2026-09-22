#!/usr/bin/env python3
"""Check native process observation against this Python process before installing."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

spec = importlib.util.spec_from_file_location("installed_observer", Path(__file__).with_name("installed-update-exercise.py"))
exercise = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exercise)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--rid", required=True)
parser.add_argument("--report", required=True, type=Path)
args = parser.parse_args()
exercise.base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
report_path = args.report.resolve()
exercise.require(not report_path.exists() and report_path.is_relative_to(Path(os.environ["RUNNER_TEMP"]).resolve()),
                 "Observer probe report must be new and inside RUNNER_TEMP")
executable = Path(sys.executable)
if platform.system() == "Darwin":
    # python.org's framework launcher re-execs Python.app but keeps its launch
    # path in sys.executable. Ask the OS only about our own known live PID.
    actual_path = subprocess.check_output(["ps", "-ww", "-p", str(os.getpid()), "-o", "comm="],
                                         text=True, timeout=5).strip()
    exercise.require(actual_path.startswith("/") and Path(actual_path).is_file(), "Missing native Python process path")
    executable = Path(actual_path)
report = {"passed": False, "rid": args.rid, "expectedPid": os.getpid(),
          "executable": str(executable), "launcherExecutable": sys.executable}
# Include absent paths, including the same basename, to exercise multi-target
# JSON handling and exact-path filtering before any product is installed.
absent = [Path(os.environ["RUNNER_TEMP"]) / "observer-absent" / name
          for name in (executable.name, "Update.exe" if args.rid == "win-x64" else "UpdateMac")]
exercise.require(not any(path.exists() for path in absent), "Observer probe's absent paths already exist")
report["targetPaths"] = [str(path) for path in [executable, *absent]]
observer = exercise.ProcessObserver([executable, *absent])
try:
    with observer:
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            rows = [row for row in observer.current(executable) if row["pid"] == os.getpid()]
            if len(rows) == 1 and observer.sample_count >= 2:
                report["observedProcess"] = rows[0]
                break
            exercise.require(not observer.errors, "Native observer failed before its second snapshot")
            time.sleep(0.1)
        exercise.require("observedProcess" in report, "Native observer did not identify its own Python process")
        exercise.require(not any(observer.seen(path) for path in absent), "Native observer matched a wrong executable path")
    exercise.require(not observer.errors, "Native observer did not stop cleanly")
    report["passed"] = True
except Exception as error:
    report["error"] = f"{type(error).__name__}: {error}"
finally:
    report.update(samples=observer.sample_count, observerErrors=observer.errors,
                  observerDiagnostics=observer.diagnostics())
    report_path.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
raise SystemExit(0 if report["passed"] else 1)
