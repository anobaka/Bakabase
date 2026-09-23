#!/usr/bin/env python3
"""Install audited real products and probe native AX/UIA on a fresh hosted VM.

The capability gate is read-only. --flow empty-library additionally operates
actual native controls. --flow federated pairs the installed reader with an
explicit production Shell/Service source fixture and tests query and recovery.
The established installed lifecycle owns the installers, defaults and cleanup.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import signal
import stat
import subprocess
import time
import zipfile

HERE = Path(__file__).resolve().parent


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


lifecycle = load("native_gui_owned_lifecycle", HERE.parent / "upgrade-tests/run-installed-lifecycle.py")
probe = load("native_gui_capability", HERE / "probe.py")
direct_ax = load("native_gui_direct_ax", HERE / "direct_ax.py")


def renderer_cleanup(app, report, role):
    """Called by the installed lifecycle after both owned main processes stop."""
    if not app["rid"].startswith("osx-") or report.get("macosObserver") != "direct-ax":
        return
    evidence = {"passed": False, "readOnly": True, "signalSent": False}
    report.setdefault("installedRendererCleanup", {})[role] = evidence
    if not app.get("installationAttempted"):
        evidence.update(passed=True, applicable=False, reason="InstallationNotAttempted")
        return
    helper = load("native_gui_renderer_cleanup", HERE / "native_renderer_cleanup.py")
    try:
        proof = app.get("nativeRendererExitProof")
        lifecycle.require(proof is not None, "Installed embedded renderer exit ownership was not established")
        evidence.update(helper.wait_exit(app, proof, time.monotonic() + 30))
        lifecycle.require(evidence.get("passed") is True, "Installed embedded renderer exit was not verified")
    except Exception:
        evidence.update(passed=False, retainedInstalledFixtures=True)
        raise


def audited_web_inventory(app):
    """Match every installed web file against the provenance-audited ZIP bytes."""
    audit = app["packageAudit"]
    lifecycle.require(audit.get("passed") is True and app["role"] == "unified", "Source web requires an audited unified package")
    artifact = audit["artifacts"]["portable"]
    archive_path = app["packages"] / artifact["file"]
    lifecycle.require(archive_path.stat().st_size == artifact["sizeBytes"] and
                      lifecycle.base.sha256(archive_path) == artifact["sha256"], "Source portable archive changed")
    prefix = audit["portableContent"] + "web/"
    inventory, size = {}, 0
    with zipfile.ZipFile(archive_path) as archive:
        for entry in lifecycle.base.archive_entries(archive):
            if not entry.filename.startswith(prefix) or entry.is_dir():
                continue
            lifecycle.require(not stat.S_ISLNK(entry.external_attr >> 16), "Source web must not contain archive links")
            size += entry.file_size
            lifecycle.require(size <= 256 * 1024 * 1024 and len(inventory) < 8192, "Source web exceeds its bounded fixture budget")
            data = archive.read(entry)
            inventory[entry.filename[len(prefix):]] = {"sizeBytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    lifecycle.require("index.html" in inventory, "Source web root is absent from audited package")
    return inventory


def federated(apps, report):
    app = apps["unified"]
    source_module = load("native_gui_source_fixture", HERE / "source_fixture.py")
    full_flow = load("native_gui_federated_flow", HERE / "federated_flow.py")
    source = source_module.SourceFixture.create(Path(os.environ["RUNNER_TEMP"]), app["rid"],
        Path(report["sourcePublishDirectory"]), app["exe"].parent / "web",
        json.loads(Path(report["candidateProvenanceFile"]).read_text(encoding="utf-8")),
        app["results"] / "native-source", expected_web_inventory=audited_web_inventory(app))
    report["nativeSource"] = source.report
    # Reuse this lifecycle's existing loopback deny proxy for background update
    # and telemetry traffic. Local federation traffic retains its proxy bypass.
    for key in ("BAKABASE_UPDATE_URL", "BAKABASE_CLIENT_UPDATE_URL", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY",
                "http_proxy", "https_proxy", "all_proxy", "NO_PROXY", "no_proxy"):
        if key in app["environment"]:
            source.environment[key] = app["environment"][key]
    try:
        started = source.start()
        if app.get("nativeBackend") == "macos-direct-ax":
            started["app"]["nativeBackend"] = "macos-direct-ax"
        capability = probe.capture(started["app"], started["pid"], app["results"] / "native-source-tree", require_complete=True)
        report["sourceCapability"] = capability
        lifecycle.require(capability.get("completeTreePassed") is True, "Source native complete tree is unavailable")
        observed = lifecycle.observe_app(app)
        lifecycle.require(len(observed["processIds"]) == 1, "Native flow requires one exact reader process")
        report["nativeFederatedFlow"] = full_flow.exercise(app, observed["processIds"][0], source,
            started, None, app["results"] / "federated-flow")
        lifecycle.require(report["nativeFederatedFlow"].get("workflowStepsPassed") is True, "Native federated steps did not pass")
        lifecycle.require_same_process(observed, lifecycle.observe_app(app))
    finally:
        source.close()


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
        app["nativeGuiObservedPid"] = pid
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
                    if "embeddedAXBinding" in direct_app:
                        app["embeddedAXBinding"] = direct_app["embeddedAXBinding"]
                    helper = load("native_gui_renderer_cleanup", HERE / "native_renderer_cleanup.py")
                    app["nativeRendererExitProof"] = helper.arm(app)
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
    elif report.get("requestedFlow") == "federated":
        report["currentStage"] = "native-federated-flow"
        federated(apps, report)


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
    parser.add_argument("--flow", choices=("capability", "empty-library", "federated"), default="capability")
    parser.add_argument("--source-publish", type=Path, help="Hosted build of the explicit native source fixture; required for federated flow")
    parser.add_argument("--macos-observer", choices=("system-events", "direct-ax"), default="system-events")
    args = parser.parse_args()
    lifecycle.base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    lifecycle.require(args.rid.startswith("osx-") or args.macos_observer == "system-events", "Direct AX requires a macOS runner")
    source = provenance(args.provenance, args.rid, args.version)
    lifecycle.require((args.source_publish is not None) == (args.flow == "federated"), "Native source publish is only valid for federated flow")
    results = args.results_directory.resolve()
    lifecycle.require(not results.exists(), "Results must be a new directory")
    results.mkdir(parents=True)
    report = {"passed": False, "capabilityPassed": False, "mainFlowPassed": False, "emptyLibraryFlowPassed": False,
              "requestedFlow": args.flow, "macosObserver": args.macos_observer,
              "scope": ("installed-native-reader-with-production-shell-service-source-fixture" if args.flow == "federated" else
                        "installed-native-empty-library" if args.flow == "empty-library" else "installed-native-accessibility-capability-only"), "rid": args.rid,
              "provenance": source, "packageVersion": args.version,
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=lifecycle.base.ROOT, text=True).strip(),
              "limitations": ["Capability is separate from empty-library flow; pairing, remote detail and offline recovery remain untested.",
                              "No browser automation, API mutation, accessibility permission changes or account creation.",
                              "The original installer and normal native process own the displayed UI."]}
    if args.flow == "federated":
        report.update(sourcePublishDirectory=str(args.source_publish.resolve()), candidateProvenanceFile=str(args.provenance.resolve()),
            limitations=["Installed reader plus an explicitly named production Shell/Service source fixture, on one hosted machine.",
                         "Resource preparation alone uses production API mutations; user workflows use native controls.",
                         "This gate does not certify two installed unified copies or separate physical devices."])
    # Its execute/finally retains all established ownership checks. Only the
    # explicit exercise callback differs from the installed coexistence gate.
    try:
        lifecycle.execute(args, results, report, exercise=exercise, before_remove=renderer_cleanup)
        report["passed"] = report["capabilityPassed"] = True
        if args.flow == "federated":
            lifecycle.require(report.get("nativeSource", {}).get("cleanup", {}).get("passed") is True and
                              report.get("nativeFederatedFlow", {}).get("workflowStepsPassed") is True,
                              "Native federated workflow or source cleanup did not pass")
            report["mainFlowPassed"] = True
    except (Exception, KeyboardInterrupt) as error:
        report["passed"] = report["mainFlowPassed"] = False
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
