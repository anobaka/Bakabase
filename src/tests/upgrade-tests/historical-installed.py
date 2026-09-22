#!/usr/bin/env python3
"""Install actual published v349 assets and update them using their own updater.

Never run on a developer machine. Shares the existing native authorization,
default-path, process observation, log evidence and fail-closed cleanup helpers.
"""
import argparse
import contextlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import sqlite3
import subprocess
from types import SimpleNamespace

SPEC = importlib.util.spec_from_file_location("historical_release_runner", Path(__file__).with_name("historical-release.py"))
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
lifecycle = release.sibling("run-installed-lifecycle")
feed_module = release.sibling("historical-feed")
base, updates, require = lifecycle.base, lifecycle.updates, release.require
RESOURCE_TITLE = "Published v349 retained library resource"
SCOPE = "original-published-installer-to-candidate-code-native-update"


def package_auditor(manifest):
    def audit(packages, role, rid, version):
        require(version == release.OLD_VERSION and rid == manifest["rid"], "Original installation identity differs")
        entry = manifest["roles"][role]
        require(packages == Path(entry["historicalPackages"]).resolve(), "Historical package directory differs")
        expected = entry["historicalAudit"]
        require(expected.get("passed") is True and expected.get("releaseTag") == release.TAG and
                expected.get("releaseID") == release.RELEASE_ID and expected["manifest"] == entry["oldManifest"] and
                expected["payloadHashes"] == entry["oldPayloadHashes"], "Original payload audit differs")
        kinds = ("installer", "portable") if rid == "win-x64" else ("installer",)
        require(set(expected["artifacts"]) == set(kinds), "Historical package inventory differs")
        for kind in kinds:
            checked = release.verify_file(packages / expected["artifacts"][kind]["file"], release.pin(role, rid, kind))
            require(checked == expected["artifacts"][kind], "Original installer differs from pinned release")
        return expected
    return audit


def original_install_audit(app, prepared):
    release.check_old_content(app["exe"].parent, app["role"], app["rid"])
    # Windows Setup is checked against its separately published portable payload;
    # macOS is checked against the payload extracted from that exact published pkg.
    return updates.validate_payload(app, prepared, False)


def open_original_macos(app, prepared):
    """Explicit first user launch; never a retry or an updater restart fallback."""
    require(app["rid"].startswith("osx-"), "Explicit historical activation is macOS-only")
    original_install_audit(app, prepared)
    require(not base.native_processes(app["exe"]), "Original product already runs before explicit initial activation")
    startup = lifecycle.start_app(app, "historical-original-explicit-user")
    return dict(startup, initialLaunch={"method": "LaunchServices-open-original-installed-bundle",
                                      "automatic": False, "unmodifiedOriginalPayloadVerified": True})


def install_original(app, prepared):
    return lifecycle.install_app(app, "historical-original",
        audit=lambda current: original_install_audit(current, prepared),
        macos_initial_activation=(lambda current: open_original_macos(current, prepared))
            if app["rid"].startswith("osx-") else None)


def config_state(app):
    value = json.loads((app["data"] / "app.json").read_text(encoding="utf-8-sig"))["App"]
    expected = {"language": "en-US", "enableAnonymousDataTracking": False,
                "listeningPorts": [app["port"]], "autoListeningPortCount": 0, "maxParallelism": 1,
                "enablePreReleaseChannel": False}
    require(all(value.get(key) == item for key, item in expected.items()), "Original app configuration was not retained")
    sentinel = app["data"] / "acceptance-sentinel.txt"
    require(sentinel.read_text() == "installer acceptance owned external AppData\n", "External AppData sentinel changed")
    return {"options": expected, "sentinelSHA256": base.sha256(sentinel)}


def resource_state(app, resource_id):
    database = lifecycle.database_state(app, resource_id)
    path = app["data"] / "bakabase_insideworld.db"
    with contextlib.closing(sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=10)) as db:
        cursor = db.execute("SELECT * FROM ResourcesV2 WHERE Id = ?", (resource_id,))
        row = cursor.fetchone()
        require(row is not None and cursor.fetchone() is None, "Created historical resource is not unique")
        fields = {column[0]: {"bytesHex": value.hex()} if isinstance(value, bytes) else value
                  for column, value in zip(cursor.description, row)}
    return {"database": database, "fields": fields}


def verify_resource_retained(before, after):
    require(before["database"] == after["database"] and before["fields"] and
            all(key in after["fields"] and value == after["fields"][key] for key, value in before["fields"].items()),
            "Historical resource row changed or disappeared during migration")
    return {"passed": True, "retainedColumnCount": len(before["fields"]),
            "addedColumns": sorted(set(after["fields"]) - set(before["fields"])), "after": after}


def exercise(apps, report, feed):
    require(feed is not None, "Historical acceptance requires real native updates")
    client, unified = apps["client"], apps["unified"]
    report["currentStage"] = "historical-original-client-install"
    report["clientFirstInstall"] = install_original(client, feed.manifest["roles"]["client"])
    client_before = lifecycle.observe_app(client)
    report["clientSettingsBaseline"] = lifecycle.client_state(client)
    report["currentStage"] = "historical-original-unified-install"
    report["unifiedSecondInstall"] = install_original(unified, feed.manifest["roles"]["unified"])
    lifecycle.require_same_process(client_before, lifecycle.observe_app(client))
    report["originalVersions"] = {}
    for role, app in apps.items():
        info = lifecycle.observe_app(app)["appInfo"]
        core = info["version" if role == "client" else "coreVersion"]
        require(core == release.OLD_VERSION, "Original installed application is not the published historical code version")
        report["originalVersions"][role] = core
        # The old binary, with the fixture's explicit stable-channel preference,
        # queries only the isolated feed. No false old full nupkg is manufactured.
        report.setdefault("initialUpdateChecks", {})[role] = updates.validate_check(
            updates.api(app, "new-version").get("data"), release.OLD_VERSION, core,
            feed.manifest["roles"][role]["channel"], None)
    require(json.loads(base.api(unified["port"], "/app/terms", {}))["code"] == 0, "Could not accept original application terms")
    report["currentStage"] = "historical-create-resource"
    created = json.loads(base.api(unified["port"], "/resource/placeholder",
        {"items": [{"title": RESOURCE_TITLE}], "acquireImmediately": False}))
    require(created.get("code") == 0 and len(created.get("data", [])) == 1, "Original application did not create a resource")
    item = created["data"][0]
    resource_id = item.get("resourceId")
    require(type(resource_id) is int and resource_id > 0 and not item.get("error"), "Original resource creation failed")
    report["resourceId"] = resource_id
    report["resourceCreation"] = {"title": RESOURCE_TITLE, "result": item, "runningVersion": release.OLD_VERSION}
    report["originalResource"] = resource_state(unified, resource_id)
    report["originalConfiguration"] = {role: config_state(app) for role, app in apps.items()}
    report["checkpoints"] = [lifecycle.verify_coexistence(apps, "both-original-published-products",
                                                        report["clientSettingsBaseline"], resource_id)]
    try:
        updates.run(apps, report, feed, SimpleNamespace(observe_app=lifecycle.observe_app,
                                                       verify_coexistence=lifecycle.verify_coexistence))
    finally:
        if "automaticUpdates" in report:
            report["automaticUpdates"]["scope"] = SCOPE
    report["currentStage"] = "historical-candidate-data-retention"
    report["resourceRetention"] = verify_resource_retained(report["originalResource"], resource_state(unified, resource_id))
    current_configuration = {role: config_state(app) for role, app in apps.items()}
    require(current_configuration == report["originalConfiguration"], "Original product configuration changed")
    report["configurationRetention"] = {"passed": True, "after": current_configuration}
    report["checkpoints"].append(lifecycle.verify_coexistence(apps, "both-candidate-products-automatically-started",
                                                              report["clientSettingsBaseline"], resource_id))
    report["coexistencePassed"] = True
    report["historicalMigrationPassed"] = True
    # Shared execute finally removes both candidate installations and verifies all
    # owned files/accounts/processes. Never reinstall the old code over migrated data.


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--rid", choices=release.RIDS, required=True)
    parser.add_argument("--results-directory", type=Path, required=True)
    parser.add_argument("--macos-native-authorization", action="store_true")
    args = parser.parse_args(argv)
    release.hosted(args.rid)
    require(args.macos_native_authorization == args.rid.startswith("osx-"),
            "macOS requires the verified native authorization fixture; Windows must not request it")
    manifest = release.read_manifest(args.manifest, args.rid)
    results = args.results_directory.resolve()
    require(not results.exists() and not args.results_directory.is_symlink() and
            results.is_relative_to(Path(os.environ["RUNNER_TEMP"]).resolve()), "Results must be new and inside RUNNER_TEMP")
    args.version, args.updates_manifest = release.OLD_VERSION, args.manifest
    args.unified_packages = Path(manifest["roles"]["unified"]["historicalPackages"])
    args.client_packages = Path(manifest["roles"]["client"]["historicalPackages"])
    results.mkdir(parents=True)
    report = {"passed": False, "rid": args.rid, "packageVersion": release.OLD_VERSION,
              "newPackageVersion": manifest["newVersion"], "candidateCoreVersion": release.CANDIDATE_CORE,
              "historicalRelease": manifest["historicalRelease"], "sourceSHA": release.CANDIDATE_SHA,
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=base.ROOT, text=True).strip(),
              "automaticUpdatesRequested": True, "scope": SCOPE,
              "limitations": ["Original published installers are tested on disposable hosted runners only.",
                              "macOS historical installation uses an explicit first user open of the unchanged bundle; updater restarts remain automatic.",
                  "The target synthetic package version is higher than the original; its product code is verified identical to the candidate packages.",
                  "This exercises original-code migration for a freshly seeded historical library, not every existing user database.",
                  "Background containment uses own loopback feeds and a rejecting proxy, not an OS network sandbox.",
                  "No production signing, notarization, Gatekeeper or SmartScreen trust is validated.",
                  "macOS removal deletes the owned bundle and receipt, not a product native uninstaller."]}
    try:
        lifecycle.execute(args, results, report, package_auditor=package_auditor(manifest),
                          feed_factory=feed_module.Feed, exercise=exercise)
        report["passed"] = True
    except (Exception, KeyboardInterrupt) as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        (results / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f"Interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
