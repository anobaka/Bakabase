#!/usr/bin/env python3
"""Install audited real products and probe native AX/UIA on a fresh hosted VM.

The capability gate is read-only. --flow empty-library additionally operates
actual native controls; neither mode claims the remaining remote user flows.
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
direct_ax = load("native_gui_direct_ax", HERE / "direct_ax.py")


def exercise(apps, report, feed=None):
    lifecycle.require(feed is None, "Capability probe must not trigger product updates")
    report["nativeProbes"] = {}
    report["directAXCapabilities"] = {}
    report["directAXTrees"] = {}
    for role in ("client", "unified"):
        app = apps[role]
        report["currentStage"] = "native-probe-install-" + role
        report[role + "Install"] = lifecycle.install_app(app, "native-probe")
        observed = lifecycle.observe_app(app)
        lifecycle.require(len(observed["processIds"]) == 1, "Native probe requires one exact product process")
        pid = observed["processIds"][0]
        report["currentStage"] = "native-accessibility-" + role
        report["nativeProbes"][role] = probe.capture(app, pid, app["results"] / "native-accessibility")
        if app.get("rid", "").startswith("osx-"):
            diagnostic = direct_ax.capture(app, pid)
            report["directAXCapabilities"][role] = diagnostic
            (app["results"] / "direct-ax.json").write_text(json.dumps(diagnostic, indent=2) + "\n", encoding="utf-8")
            if report.get("macosObserver") == "direct-ax":
                direct_app = dict(app, nativeBackend="macos-direct-ax")
                report["currentStage"] = "direct-ax-complete-tree-" + role
                tree = (probe.capture(direct_app, pid, app["results"] / "direct-ax-tree", require_complete=True)
                        if diagnostic.get("available") is True else
                        {"capabilityPassed": False, "completeTreePassed": False, "skippedReason": "DirectAXUnavailable"})
                report["directAXTrees"][role] = tree
                # A partial new tree never falls back to the old action provider.
                if tree.get("completeTreePassed") is True:
                    app["nativeBackend"] = "macos-direct-ax"
        lifecycle.require_same_process(observed, lifecycle.observe_app(app))
    # Collect both products even when one does not expose its WebView controls.
    selected_probes = report["directAXTrees"] if report.get("macosObserver") == "direct-ax" else report["nativeProbes"]
    if report.get("macosObserver") == "direct-ax":
        lifecycle.require(len(selected_probes) == 2 and all(item.get("completeTreePassed") is True for item in selected_probes.values()),
                          "Direct AX complete-tree gate failed")
    lifecycle.require(len(selected_probes) == 2 and all(item["capabilityPassed"] for item in selected_probes.values()),
                      "Native accessibility capability is unavailable; inspect the bounded trees")
    report["capabilityPassed"] = True
    if report.get("requestedFlow") == "empty-library":
        report["currentStage"] = "native-empty-library-flow"
        app = apps["unified"]
        observed = lifecycle.observe_app(app)
        lifecycle.require(len(observed["processIds"]) == 1, "Native flow requires one exact product process")
        flow = load("native_empty_library_flow", HERE / "flow.py")
        result = flow.run_empty_library(app, observed["processIds"][0], app["results"] / "empty-library-flow")
        lifecycle.require(result.get("emptyLibraryFlowPassed") is True, "Native empty-library flow did not pass")
        report["emptyLibraryFlowPassed"] = True
        report["nativeEmptyLibrary"] = result


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
    parser.add_argument("--flow", choices=("capability", "empty-library"), default="capability")
    parser.add_argument("--macos-observer", choices=("system-events", "direct-ax"), default="system-events")
    args = parser.parse_args()
    lifecycle.base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    lifecycle.require(args.rid.startswith("osx-") or args.macos_observer == "system-events", "Direct AX requires a macOS runner")
    source = provenance(args.provenance, args.rid, args.version)
    results = args.results_directory.resolve()
    lifecycle.require(not results.exists(), "Results must be a new directory")
    results.mkdir(parents=True)
    report = {"passed": False, "capabilityPassed": False, "mainFlowPassed": False, "emptyLibraryFlowPassed": False,
              "requestedFlow": args.flow, "macosObserver": args.macos_observer,
              "scope": "installed-native-empty-library" if args.flow == "empty-library" else "installed-native-accessibility-capability-only", "rid": args.rid,
              "provenance": source, "packageVersion": args.version,
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=lifecycle.base.ROOT, text=True).strip(),
              "limitations": ["Capability is separate from empty-library flow; pairing, remote detail and offline recovery remain untested.",
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
        print(json.dumps({key: report[key] for key in ("passed", "capabilityPassed", "emptyLibraryFlowPassed", "mainFlowPassed", "rid", "scope")}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt("Native probe interrupted")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
