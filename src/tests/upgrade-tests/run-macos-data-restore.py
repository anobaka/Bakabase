#!/usr/bin/env python3
"""Retain original ARM-created data through Intel candidate native updates.

This never claims the defective original Intel client initiated an update.
The archived original program created the database; current installed programs
read it, update using their real updater, automatically restart, and restart again.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
from types import SimpleNamespace

import importlib.util

SPEC = importlib.util.spec_from_file_location("restored_data_contract", Path(__file__).with_name("macos-data-retention.py"))
retention = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(retention)
lifecycle, release, base, updates, require = retention.lifecycle, retention.release, retention.base, retention.updates, retention.require
SCOPE = "original-arm-library-data-restored-on-intel-candidate-native-update"


def exercise(apps, report, feed, producer_directory):
    restore = lifecycle.sibling("macos-data-restore")
    report["currentStage"] = "restore-original-data-before-candidate-install"
    baseline = restore.prepare_restore(apps, producer_directory, report)
    report["dataSeed"] = baseline["seed"]
    report["originalSemantics"] = baseline["seed"]["baselineSemantics"]
    report["originalConfiguration"] = baseline["configuration"]
    report["clientSettingsBaseline"] = baseline["clientSettingsBaseline"]
    report["resourceIds"] = baseline["seed"]["resourceIds"]
    report["resourceId"] = report["resourceIds"][0]
    for role in ("client", "unified"):
        report["currentStage"] = "candidate-" + role + "-install-over-restored-data"
        report.setdefault("candidateInstallations", {})[role] = lifecycle.install_app(apps[role], "restored-data")

    def coexistence(current_apps, label, client_expected, _resource_id):
        lifecycle.require_separate_apps(current_apps)
        observed = {role: lifecycle.observe_app(app) for role, app in current_apps.items()}
        require(set(observed["client"]["processIds"]).isdisjoint(observed["unified"]["processIds"]), "Product processes overlap")
        for role, value in observed.items():
            core = value["appInfo"]["version" if role == "client" else "coreVersion"]
            require(core == release.CANDIDATE_CORE, "Candidate process has unexpected product code")
        rows = retention.snapshot_tables(current_apps["unified"]["data"] / "bakabase_insideworld.db", ["ResourcesV2"])["ResourcesV2"]["rows"]
        require(sorted(row["Id"] for row in rows) == sorted(report["resourceIds"]), "Restored resource IDs changed")
        return {"label": label, "apps": observed, "resourceIds": report["resourceIds"],
                "clientSettingsHashes": lifecycle.verify_client_state(current_apps["client"], client_expected), "passed": True}

    report["checkpoints"] = [coexistence(apps, "candidate-reads-original-data", report["clientSettingsBaseline"], report["resourceId"])]
    report["currentStage"] = "verify-after-initial-install"
    restore.verify_restore(apps, report, "after-initial-install", baseline)
    try:
        updates.run(apps, report, feed, SimpleNamespace(observe_app=lifecycle.observe_app, verify_coexistence=coexistence))
    finally:
        if "automaticUpdates" in report:
            report["automaticUpdates"]["scope"] = SCOPE
    report["currentStage"] = "verify-after-native-updates"
    restore.verify_restore(apps, report, "after-native-updates", baseline)
    report["manualRestarts"] = {}
    for role in ("unified", "client"):
        app, other = apps[role], apps["client" if role == "unified" else "unified"]
        survivor = lifecycle.observe_app(other)
        stopped = lifecycle.stop_app(app)
        restarted = lifecycle.start_app(app, "restored-data-restart")
        require(set(stopped).isdisjoint(restarted["processIds"]), "Explicit restart reused the original process")
        lifecycle.require_same_process(survivor, lifecycle.observe_app(other))
        report["manualRestarts"][role] = restarted
    report["currentStage"] = "verify-after-explicit-restarts"
    restore.verify_restore(apps, report, "after-explicit-restarts", baseline)
    report["checkpoints"].append(coexistence(apps, "restored-data-after-restarts", report["clientSettingsBaseline"], report["resourceId"]))
    report.update(dataRetentionPassed=True, coexistencePassed=True)


def preserve_and_remove_sources(app, report, role):
    retention.preserve_data_evidence(app, report, role)
    restored = report.get("macosDataRestore", {})
    if role != "unified" or not restored:
        return
    source_root = Path(restored["sourceRoot"])
    runner = Path(os.environ["RUNNER_TEMP"]).resolve()
    require(source_root == source_root.resolve() and source_root.is_relative_to(runner) and source_root != runner,
            "Restored source media ownership changed")
    root_created = restored.get("sourceRootCreated") is True
    if not root_created and source_root.exists():
        # A failed or missing creation proof never authorizes this existing tree.
        report["restoredSourceRetainedWithoutCreationProof"] = True
        return
    directories = [Path(value) for value in restored.get("mediaCreatedDirectories", [])]
    require(len(directories) <= 20 and len(set(directories)) == len(directories),
            "Invalid restored source directory inventory")
    for path in directories:
        require(path == path.resolve() and not path.is_symlink() and path.is_relative_to(runner) and path != runner
                and (path in source_root.parents or (root_created and path.is_relative_to(source_root))),
                "Invalid restored source parent ownership")
    if not root_created:
        # mkdir may have created an ancestor before creating sourceRoot failed.
        # Only recorded ancestors are eligible, and unknown contents stop rmdir.
        for path in sorted(directories, key=lambda value: len(value.parts), reverse=True):
            if path.exists(): path.rmdir()
        report["restoredSourceParentsRemoved"] = True
        return
    files = list(source_root.rglob("*"))
    require(len(files) <= 20 and not any(path.is_symlink() for path in files), "Unexpected restored source inventory")
    evidence = report["restoredSourceEvidence"] = []
    for source in files:
        if not source.is_file():
            continue
        require(source.stat().st_size <= 4096, "Restored source evidence exceeds its bound")
        target = app["results"] / "restored-source-media" / source.relative_to(source_root)
        target.parent.mkdir(parents=True, exist_ok=True)
        with source.open("rb") as reader, target.open("xb") as writer:
            value = reader.read(4097)
            require(len(value) <= 4096, "Restored source evidence grew beyond its bound")
            writer.write(value)
        digest = base.sha256(target)
        require(digest == base.sha256(source), "Restored source changed during preservation")
        evidence.append({"originalPath": str(source), "file": target.relative_to(app["results"]).as_posix(),
                         "sha256": digest, "sizeBytes": len(value)})
    base.remove_owned_tree(source_root)
    for path in sorted(directories, key=lambda value: len(value.parts), reverse=True):
        if path.exists():
            path.rmdir()  # Never recursively remove unknown contents in a parent.
    report["restoredSourceFilesRemoved"] = not source_root.exists()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("unified-packages", "client-packages", "provenance", "updates-manifest", "producer-directory", "results-directory"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=("osx-x64",))
    parser.add_argument("--version", required=True)
    parser.add_argument("--macos-native-authorization", action="store_true")
    args = parser.parse_args(argv)
    release.hosted(args.rid)
    require(args.macos_native_authorization, "Candidate native updates require the verified macOS authorization fixture")
    provenance = json.loads(args.provenance.read_text())
    require(provenance.get("passed") is True and provenance.get("rid") == args.rid and
            provenance.get("version") == args.version and provenance.get("packageSourceSHA") == release.CANDIDATE_SHA,
            "Candidate package provenance differs")
    results = args.results_directory.resolve()
    require(not results.exists() and not args.results_directory.is_symlink() and
            results.is_relative_to(Path(os.environ["RUNNER_TEMP"]).resolve()), "Results must be new and inside RUNNER_TEMP")
    results.mkdir(parents=True)
    report = {"passed": False, "dataRetentionPassed": False, "rid": args.rid, "scope": SCOPE,
              "sourceSHA": release.CANDIDATE_SHA, "packageVersion": args.version,
              "candidateCoreVersion": release.CANDIDATE_CORE, "originalDataVersion": release.OLD_VERSION,
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=base.ROOT, text=True).strip(),
              "automaticUpdatesRequested": True, "historicalIntelUpdaterVerified": False,
              "limitations": ["The original ARM program created the restored data. The original Intel client is not launched or certified.",
                              "Initial candidate installation restores historical data, not an old Intel bundle overwrite.",
                              "Current candidate code initiates its own native updater and automatic restart; later explicit restarts are separate.",
                              "Synthetic historical data with unchanged business migrations; not every user database or power-loss recovery."]}
    try:
        lifecycle.execute(args, results, report,
            exercise=lambda apps, current, feed: exercise(apps, current, feed, args.producer_directory),
            before_remove=preserve_and_remove_sources)
        report["passed"] = True
    except (Exception, KeyboardInterrupt) as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        (results / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps({key: report.get(key) for key in ("passed", "dataRetentionPassed", "rid", "scope", "currentStage", "error")}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f"Interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
