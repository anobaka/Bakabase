#!/usr/bin/env python3
"""Install audited real products and probe native AX/UIA on a fresh hosted VM.

This first gate collects actual trees needed to implement native user flows.
It does not click controls, use a browser, or claim end-to-end GUI acceptance.
The established installed lifecycle owns the installers, defaults and cleanup.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess

HERE = Path(__file__).resolve().parent


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


lifecycle = load("native_gui_owned_lifecycle", HERE.parent / "upgrade-tests/run-installed-lifecycle.py")
probe = load("native_gui_capability", HERE / "probe.py")


def exercise(apps, report, feed=None):
    lifecycle.require(feed is None, "Capability probe must not trigger product updates")
    report["nativeProbes"] = {}
    for role in ("client", "unified"):
        app = apps[role]
        report["currentStage"] = "native-probe-install-" + role
        report[role + "Install"] = lifecycle.install_app(app, "native-probe")
        observed = lifecycle.observe_app(app)
        lifecycle.require(len(observed["processIds"]) == 1, "Native probe requires one exact product process")
        pid = observed["processIds"][0]
        report["currentStage"] = "native-accessibility-" + role
        report["nativeProbes"][role] = probe.capture(app, pid, app["results"] / "native-accessibility")
        lifecycle.require_same_process(observed, lifecycle.observe_app(app))
    # Collect both products even when one does not expose its WebView controls.
    lifecycle.require(all(item["capabilityPassed"] for item in report["nativeProbes"].values()),
                      "Native accessibility capability is unavailable; inspect the bounded trees")


def provenance(path, rid, version):
    value = json.loads(Path(path).read_text(encoding="utf-8"))
    lifecycle.require(value.get("passed") is True and value.get("rid") == rid and value.get("version") == version,
                      "Package provenance did not pass or does not match this native runner")
    source = value.get("packageSourceSHA", "")
    lifecycle.require(isinstance(source, str) and re.fullmatch(r"[0-9a-f]{40}", source), "Invalid package source SHA")
    return {key: value.get(key) for key in ("packageSourceSHA", "testSourceSHA", "repository", "runID", "rid", "version")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unified-packages", required=True, type=Path)
    parser.add_argument("--client-packages", required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=("win-x64", "osx-x64", "osx-arm64"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--provenance", required=True, type=Path)
    parser.add_argument("--results-directory", required=True, type=Path)
    args = parser.parse_args()
    lifecycle.base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    source = provenance(args.provenance, args.rid, args.version)
    results = args.results_directory.resolve()
    lifecycle.require(not results.exists(), "Results must be a new directory")
    results.mkdir(parents=True)
    report = {"passed": False, "capabilityPassed": False, "mainFlowPassed": False,
              "scope": "installed-native-accessibility-capability-only", "rid": args.rid,
              "provenance": source, "packageVersion": args.version,
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=lifecycle.base.ROOT, text=True).strip(),
              "limitations": ["Read-only capability probe: no product user flow is accepted by this result.",
                              "No browser automation, API mutation, accessibility permission changes or account creation.",
                              "The original installer and normal native process own the displayed UI."]}
    # Its execute/finally retains all established ownership checks. Only the
    # explicit exercise callback differs from the installed coexistence gate.
    try:
        lifecycle.execute(args, results, report, exercise=exercise)
        report["passed"] = report["capabilityPassed"] = True
    except (Exception, KeyboardInterrupt) as error:
        report["error"] = {"type": type(error).__name__, "code": "NativeCapabilityProbeFailed"}
    finally:
        (results / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({key: report[key] for key in ("passed", "capabilityPassed", "mainFlowPassed", "rid", "scope")}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt("Native probe interrupted")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
