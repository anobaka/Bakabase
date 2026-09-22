#!/usr/bin/env python3
"""Beta macOS data retention, distinct from the old bundle's launch compatibility.

Only hosted runners may activate the unchanged historical executable. SQLite
helpers are pure and can be exercised locally using synthetic databases.
"""
import contextlib
import importlib.util
import json
import math
from pathlib import Path
import re
import sqlite3
import subprocess
import time
from types import SimpleNamespace

SPEC = importlib.util.spec_from_file_location("historical_data_contract", Path(__file__).with_name("historical-installed.py"))
original = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(original)
release, lifecycle, base, updates, require = original.release, original.lifecycle, original.base, original.updates, original.require
SCOPE = "macos-beta-data-retention-original-executable-seed-native-update"
MAX_ROWS = 5000
# Only exercise can authorize adoption of a process started by this installation.
# Tokens are consumed once and expire when install_app returns or raises.
_pending_original_installations = {}


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False)


def backup_database(source, target, timeout=10):
    """Online SQLite backup includes committed WAL; a raw file copy does not."""
    source, target = Path(source), Path(target)
    require(source.is_file() and not source.is_symlink(), "Existing regular SQLite source is required")
    require(source.stat().st_size <= 32 * 1024 * 1024, "Synthetic database source exceeds its bound")
    require(not target.exists() and not target.is_symlink() and target.parent.is_dir(), "Backup target must be new")
    require(type(timeout) in (int, float) and 0 < timeout <= 30, "Invalid backup deadline")
    deadline = time.monotonic() + timeout

    def progress(*_):
        if time.monotonic() >= deadline:
            raise TimeoutError("SQLite evidence backup exceeded its deadline")

    try:
        with contextlib.closing(sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True, timeout=min(5, timeout))) as reader, \
                contextlib.closing(sqlite3.connect(target, timeout=min(5, timeout))) as writer:
            reader.execute("PRAGMA query_only=ON")
            pages = reader.execute("PRAGMA page_count").fetchone()[0]
            page_size = reader.execute("PRAGMA page_size").fetchone()[0]
            require(pages * page_size <= 32 * 1024 * 1024, "Synthetic logical database including WAL exceeds its bound")
            reader.backup(writer, pages=128, progress=progress, sleep=0.05)
            writer.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
            integrity = writer.execute("PRAGMA integrity_check").fetchall()
            foreign_keys = writer.execute("PRAGMA foreign_key_check").fetchmany(101)
            progress()
            require(integrity == [("ok",)], "Preserved SQLite integrity failed")
            require(not foreign_keys, "Preserved SQLite contains foreign-key violations")
        require(target.stat().st_size <= 32 * 1024 * 1024, "Synthetic database evidence exceeds its bound")
        return {"sha256": base.sha256(target), "sizeBytes": target.stat().st_size,
                "integrity": "ok", "foreignKeyViolations": []}
    except Exception:
        # This is our newly created evidence copy, never the input database.
        target.unlink(missing_ok=True)
        raise


def snapshot_tables(path, table_names, *, immutable=False):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), "Existing snapshot database is required")
    require(isinstance(table_names, list) and 0 < len(table_names) <= 32 and
            all(isinstance(name, str) and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,80}", name) for name in table_names) and
            len(set(table_names)) == len(table_names), "Invalid table inventory")
    deadline = time.monotonic() + 10
    result = {}
    uri = path.resolve().as_uri() + ("?mode=ro&immutable=1" if immutable else "?mode=ro")
    with contextlib.closing(sqlite3.connect(uri, uri=True, timeout=5)) as db:
        db.execute("PRAGMA query_only=ON")
        db.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
        db.execute("BEGIN")
        for table in table_names:
            info = db.execute(f'PRAGMA table_info("{table}")').fetchall()
            require(info and len(info) <= 256, "Required data table is absent or oversized: " + table)
            columns = [column[1] for column in info]
            primary = [column[1] for column in sorted(info, key=lambda column: column[5]) if column[5]]
            require(primary, "Data table must have a primary key: " + table)
            raw = db.execute(f'SELECT * FROM "{table}"').fetchmany(MAX_ROWS + 1)
            require(0 < len(raw) <= MAX_ROWS, "Required data table is empty or oversized: " + table)
            rows, keys = [], set()
            for raw_row in raw:
                row = {}
                for column, value in zip(columns, raw_row):
                    require(value is None or type(value) in (str, int, float, bytes), "Unexpected SQLite storage type")
                    require(type(value) is not float or math.isfinite(value), "Non-finite SQLite value")
                    row[column] = {"blobHex": value.hex()} if isinstance(value, bytes) else value
                require(len(canonical(row).encode()) <= 65536, "Synthetic row exceeds evidence budget")
                require(all(row[key] is not None for key in primary), "Null primary key")
                key = canonical([row[name] for name in primary])
                require(key not in keys, "Ambiguous primary key")
                keys.add(key)
                rows.append(row)
            rows.sort(key=lambda row: canonical([row[name] for name in primary]))
            result[table] = {"columns": columns, "primaryKey": primary, "rows": rows}
            require(time.monotonic() < deadline, "Data snapshot exceeded deadline")
    return result


def verify_rows_retained(before, after):
    require(isinstance(before, dict) and before, "Empty data baseline cannot establish retention")
    require(isinstance(after, dict), "Current data snapshot is missing")

    def validate(table):
        require(isinstance(table, dict), "Invalid table snapshot")
        columns, primary, rows = table.get("columns"), table.get("primaryKey"), table.get("rows")
        require(isinstance(columns, list) and columns and all(isinstance(value, str) and value for value in columns) and
                len(set(columns)) == len(columns), "Invalid snapshot columns")
        require(isinstance(primary, list) and primary and all(isinstance(value, str) for value in primary) and
                len(set(primary)) == len(primary) and set(primary) <= set(columns), "Invalid snapshot primary key")
        require(isinstance(rows, list) and 0 < len(rows) <= MAX_ROWS, "Empty or oversized snapshot rows")
        for row in rows:
            require(isinstance(row, dict) and set(columns) <= set(row) and
                    all(row[key] is not None for key in primary), "Invalid snapshot row or null identity")

    summary = {}
    for name, prior in before.items():
        require(name in after, "Original data table disappeared: " + name)
        current = after[name]
        validate(prior)
        validate(current)
        primary, columns = prior["primaryKey"], prior["columns"]
        require(primary and primary == current["primaryKey"] and set(columns) <= set(current["columns"]),
                "Original schema identity or columns changed: " + name)
        require(prior["rows"], "An empty baseline does not prove seeded data retention")
        indexed = {}
        for row in current["rows"]:
            key = canonical([row[column] for column in primary])
            require(key not in indexed, "Duplicate row identity: " + name)
            indexed[key] = row
        prior_keys = set()
        for row in prior["rows"]:
            key = canonical([row[column] for column in primary])
            require(key not in prior_keys, "Duplicate baseline identity: " + name)
            prior_keys.add(key)
            require(key in indexed and all(column in indexed[key] and
                    canonical(row[column]) == canonical(indexed[key][column]) for column in columns),
                    "Original row or field changed: " + name + " key=" + key)
        summary[name] = {"retainedRows": len(prior_keys), "retainedColumns": len(columns),
                         "addedRows": len(indexed) - len(prior_keys),
                         "addedColumns": sorted(set(current["columns"]) - set(columns))}
    return {"passed": True, "tables": summary}


def activate_original_for_data(app, prepared, preinstall_token=None):
    # This is explicitly a fixture activation, not a claim that LaunchServices
    # opened the historical bundle successfully. No old bytes are modified.
    release.hosted(app["rid"])
    require(app["rid"] in ("osx-x64", "osx-arm64") and app["version"] == release.OLD_VERSION,
            "Direct data seed activation requires the original macOS package")
    context = _pending_original_installations.pop(preinstall_token, None) if type(preinstall_token) is object else None
    preinstall_verified = context is not None and context[0] is app and context[1] is prepared and context[2] == {
        "installRoot": str(app["installRoot"]), "executable": str(app["exe"]),
        "dataDirectory": str(app["data"]), "rid": app["rid"], "role": app["role"], "version": app["version"],
        "installRootAbsent": True, "exactExecutableProcessIds": []}
    require(preinstall_token is None or preinstall_verified, "Current original installation proof is invalid")
    before = original.original_install_audit(app, prepared)
    running = base.native_processes(app["exe"])
    require(len(running) <= 1, "Multiple historical product processes appeared after installation")
    if running:
        require(preinstall_verified, "Running historical product has no current preinstall proof")
        expected_pid = running[0]
        method = "installer-postinstall-automatic"
    else:
        log = (app["results"] / "historical-data-seed-executable.log").open("wb")
        app["childLogs"].append(log)
        child = subprocess.Popen([str(app["exe"])], cwd=app["exe"].parent, env=app["environment"],
                                 stdout=log, stderr=subprocess.STDOUT)
        app["children"].append(child)
        expected_pid = child.pid
        method = "exact-unmodified-installed-executable-for-data-seeding"
    startup = lifecycle.observe_app(app, startup=True)
    core = startup["appInfo"]["version" if app["role"] == "client" else "coreVersion"]
    require(core == release.OLD_VERSION and startup["processIds"] == [expected_pid] and
            Path(startup["effectiveDataDirectory"]).resolve() == app["data"].resolve(),
            "Historical executable, version or default data identity differs")
    require(original.original_install_audit(app, prepared) == before, "Historical payload changed while seeding data")
    require(base.native_processes(app["exe"]) == [expected_pid], "Historical process identity changed during activation")
    return dict(startup, initialLaunch={"method": method, "preinstallProofVerified": preinstall_verified,
                "bypassLaunchServicesForHistoricalDataSeed": not bool(running), "normalBundleLaunchVerified": False,
                "unmodifiedOriginalPayloadVerified": True})


def preserve_data_evidence(app, report, role):
    """Called after native processes stop, before removing any owned AppData."""
    require(not base.native_processes(app["exe"]), "Data evidence requires the owned product to be stopped")
    evidence = report.setdefault("stoppedDataEvidence", {}).setdefault(role, {})
    if role == "unified":
        source = app["data"] / "bakabase_insideworld.db"
        if source.exists():
            target = app["results"] / "stopped-library.sqlite"
            evidence["database"] = dict(backup_database(source, target), file=target.name)
    evidence.update(archive_configuration(app, "stopped-configuration"))
    evidence["passed"] = True


def archive_configuration(app, directory):
    require(directory in ("stopped-configuration", "before-update-configuration"), "Invalid configuration evidence directory")
    evidence = {"configurationHashes": {}, "configurationFiles": {}}
    for name in ("app.json", "acceptance-sentinel.txt", "client/host.json", "client/connection.json"):
        source = app["data"] / name
        if not source.exists():
            continue
        require(source.is_file() and not source.is_symlink() and source.stat().st_size <= 256 * 1024,
                "Owned fixture configuration exceeds its evidence bound")
        data = source.read_bytes()
        require(len(data) <= 256 * 1024, "Owned fixture configuration grew beyond its evidence bound")
        target = app["results"] / directory / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(data)
        digest = base.sha256(target)
        require(digest == base.sha256(source), "Fixture configuration changed during evidence capture")
        evidence["configurationHashes"][name] = digest
        evidence["configurationFiles"][name] = target.relative_to(app["results"]).as_posix()
    return evidence


def exercise(apps, report, feed):
    require(feed is not None and all(app["rid"].startswith("osx-") for app in apps.values()),
            "macOS data retention requires original native updates")
    seed_module = lifecycle.sibling("macos-data-seed")
    for role in ("client", "unified"):
        app = apps[role]
        prepared = feed.manifest["roles"][role]
        report["currentStage"] = "data-retention-original-" + role + "-install"
        release.hosted(app["rid"])
        require(not app["installRoot"].exists() and not app["installRoot"].is_symlink(),
                "Original data seed install target already exists")
        require(not base.native_processes(app["exe"]), "Historical product runs before original package installation")
        proof = {"installRoot": str(app["installRoot"]), "executable": str(app["exe"]),
                 "dataDirectory": str(app["data"]), "rid": app["rid"], "role": app["role"], "version": app["version"],
                 "installRootAbsent": True, "exactExecutableProcessIds": []}
        report.setdefault("originalDataPreinstallProofs", {})[role] = proof
        token = object()
        _pending_original_installations[token] = (app, prepared, proof.copy())
        try:
            installed = lifecycle.install_app(app, "historical-data", audit=lambda current, entry=prepared:
                original.original_install_audit(current, entry), macos_initial_activation=lambda current, entry=prepared, token=token:
                activate_original_for_data(current, entry, token))
        finally:
            _pending_original_installations.pop(token, None)
        installed["mechanism"] = ("original-pkg-system-install-with-observed-postinstall-automatic-launch"
            if installed["startup"]["initialLaunch"]["method"] == "installer-postinstall-automatic"
            else "original-pkg-system-install-with-explicit-executable-data-seed")
        report.setdefault("originalDataSeedInstallations", {})[role] = installed
    client, unified = apps["client"], apps["unified"]
    report["originalVersions"] = {}
    for role, app in apps.items():
        info = lifecycle.observe_app(app)["appInfo"]
        core = info["version" if role == "client" else "coreVersion"]
        require(core == release.OLD_VERSION, "Data baseline requires the unchanged original code version")
        report["originalVersions"][role] = core
        report.setdefault("initialUpdateChecks", {})[role] = updates.validate_check(
            updates.api(app, "new-version").get("data"), release.OLD_VERSION, core,
            feed.manifest["roles"][role]["channel"], None)
    report["clientSettingsBaseline"] = lifecycle.client_state(client)
    report["originalConfiguration"] = {role: original.config_state(app) for role, app in apps.items()}
    report["currentStage"] = "data-retention-original-api-seed"
    seed = seed_module.seed(apps, report, unified["results"] / "source-media")
    report["dataSeed"] = seed
    report["resourceId"] = seed["resourceIds"][0]
    report["resourceIds"] = seed["resourceIds"]
    baseline_api = seed_module.verify_semantics(unified, seed)
    report["originalSemantics"] = baseline_api
    before_file = unified["results"] / "before-update.sqlite"
    report["originalDatabaseBackup"] = dict(backup_database(unified["data"] / "bakabase_insideworld.db", before_file), file=before_file.name)
    baseline = snapshot_tables(before_file, seed["requiredTables"])
    for table, minimum in seed["minimumTableRows"].items():
        require(table in baseline and len(baseline[table]["rows"]) >= minimum,
                "Historical seed did not populate required data: " + table)
    (unified["results"] / "before-tables.json").write_text(json.dumps(baseline, ensure_ascii=False, indent=2) + "\n")
    report["originalConfigurationBackups"] = {
        role: archive_configuration(app, "before-update-configuration") for role, app in apps.items()}

    def coexistence(current_apps, label, client_expected, _resource_id):
        lifecycle.require_separate_apps(current_apps)
        observed = {role: lifecycle.observe_app(app) for role, app in current_apps.items()}
        require(set(observed["client"]["processIds"]).isdisjoint(observed["unified"]["processIds"]),
                "Product processes overlap")
        source = current_apps["unified"]["data"] / "bakabase_insideworld.db"
        rows = snapshot_tables(source, ["ResourcesV2"])["ResourcesV2"]["rows"]
        require(sorted(row["Id"] for row in rows) == sorted(seed["resourceIds"]), "Original resource IDs changed")
        return {"label": label, "apps": observed, "resourceIds": seed["resourceIds"],
                "clientSettingsHashes": lifecycle.verify_client_state(current_apps["client"], client_expected), "passed": True}

    def verify_phase(phase):
        destination = unified["results"] / (phase + ".sqlite")
        report.setdefault("dataChecks", {})[phase] = check = {
            "backup": dict(backup_database(unified["data"] / "bakabase_insideworld.db", destination), file=destination.name)}
        actual = snapshot_tables(destination, seed["requiredTables"])
        (unified["results"] / (phase + "-tables.json")).write_text(json.dumps(actual, ensure_ascii=False, indent=2) + "\n")
        check["retention"] = verify_rows_retained(baseline, actual)
        check["semantics"] = seed_module.verify_semantics(unified, seed)
        require(canonical(check["semantics"]) == canonical(baseline_api), "Original API data changed")
        configuration = {role: original.config_state(app) for role, app in apps.items()}
        require(configuration == report["originalConfiguration"], "Original settings changed")
        check["configuration"] = configuration
        source_root = (unified["results"] / "source-media").resolve()
        require(seed["mediaFiles"], "Source media verification cannot be empty")
        for media in seed["mediaFiles"]:
            path = Path(media["path"])
            require(path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(source_root) and
                    path.stat().st_size == media["sizeBytes"] and base.sha256(path) == media["sha256"],
                    "Original source file disappeared or changed")
        check.update(configurationRetained=True, sourceMediaRetained=True, passed=True)

    report["checkpoints"] = [coexistence(apps, "rich-original-data-seeded", report["clientSettingsBaseline"], report["resourceId"])]
    try:
        updates.run(apps, report, feed, SimpleNamespace(observe_app=lifecycle.observe_app, verify_coexistence=coexistence))
    finally:
        if "automaticUpdates" in report:
            report["automaticUpdates"]["scope"] = SCOPE
    report["currentStage"] = "data-retention-after-native-updates"
    verify_phase("after-native-updates")
    report["manualRestarts"] = {}
    for role in ("unified", "client"):
        app, other = apps[role], apps["client" if role == "unified" else "unified"]
        survivor = lifecycle.observe_app(other)
        stopped = lifecycle.stop_app(app)
        restarted = lifecycle.start_app(app, "retention-restart")
        require(set(stopped).isdisjoint(restarted["processIds"]), "Explicit restart reused original process")
        lifecycle.require_same_process(survivor, lifecycle.observe_app(other))
        report["manualRestarts"][role] = restarted
    report["currentStage"] = "data-retention-after-explicit-restarts"
    verify_phase("after-explicit-restarts")
    report["checkpoints"].append(coexistence(apps, "retained-after-explicit-restarts", report["clientSettingsBaseline"], report["resourceId"]))
    report.update(coexistencePassed=True, dataRetentionPassed=True, historicalNormalLaunchVerified=False)
