#!/usr/bin/env python3
"""Restore byte-identical, API-created ARM v349 data on a fresh hosted Intel Mac.

The downloader verifies the artifact digest. This module verifies its old-program
evidence and data, never launches a product, and never writes or rebases SQLite.
"""
import contextlib
import copy
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import socket
import sqlite3
import time

SPEC = importlib.util.spec_from_file_location("macos_restore_retention", Path(__file__).with_name("macos-data-retention.py"))
retention = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(retention)
release, lifecycle, base, require = retention.release, retention.lifecycle, retention.base, retention.require
seed_module = lifecycle.sibling("macos-data-seed")
ROLES = ("client", "unified")
CONFIG_NAMES = {"unified": {"app.json", "acceptance-sentinel.txt"},
                "client": {"app.json", "acceptance-sentinel.txt", "client/host.json", "client/connection.json"}}
SENTINEL = b"installer acceptance owned external AppData\n"
MANIFEST_KEYS = {"verified", "area", "rid", "runId", "headSHA", "artifactSHA256", "reportSHA256"}


def _file(path, limit):
    path = Path(path)
    require(path.is_absolute() and path == path.resolve() and path.is_file() and not path.is_symlink(),
            "Evidence must be a canonical regular file")
    require(0 < path.stat().st_size <= limit, "Evidence file exceeds its bound")
    return path


def _json(path, limit=8 * 1024 * 1024):
    path = _file(path, limit)
    with path.open("rb") as source:
        data = source.read(limit + 1)
    require(len(data) <= limit, "JSON evidence grew beyond its bound")
    return json.loads(data.decode("utf-8-sig"), parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite JSON")))


def _hash(path, expected, limit):
    path = _file(path, limit)
    require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{64}", expected)
            and base.sha256(path) == expected, "Evidence SHA256 differs")
    return path


def _guard(apps):
    require(set(apps) == set(ROLES), "Both products are required")
    require(all(app.get("rid") == "osx-x64" for app in apps.values()), "Restore requires the Intel macOS consumer")
    release.hosted("osx-x64")
    lifecycle.require_separate_apps(apps)
    defaults = lifecycle.default_paths("osx-x64", Path.home(), os.environ)["data"]
    for role, app in apps.items():
        require(app["data"] == defaults[role] and app["data"] == app["data"].resolve(),
                "Restore requires each product's canonical default AppData")


def _fixture(app, report):
    root = app["data"]
    require(str(root) in report.get("preflightAbsentPaths", [])
            and str(app["installRoot"]) in report["preflightAbsentPaths"], "Current pristine-run proof is missing")
    require(not app["installRoot"].exists() and not app["installRoot"].is_symlink()
            and not base.native_processes(app["exe"]), "Restore must precede product installation and startup")
    require(root.is_dir() and not root.is_symlink(), "Prepared default data is missing")
    entries = list(root.rglob("*"))
    expected_dirs = {"client"} if app["role"] == "client" else set()
    require(len(entries) <= 8 and all(not p.is_symlink() and (p.is_file() or p.is_dir()) for p in entries)
            and {p.relative_to(root).as_posix() for p in entries if p.is_file()} == CONFIG_NAMES[app["role"]]
            and {p.relative_to(root).as_posix() for p in entries if p.is_dir()} == expected_dirs,
            "Default AppData contains more than the current prepare_data fixture")
    options = {"language": "en-US", "enableAnonymousDataTracking": False, "listeningPorts": [app["port"]],
               "autoListeningPortCount": 0, "maxParallelism": 1, "enablePreReleaseChannel": False}
    require(_json(root / "app.json", 256 * 1024) == {"App": options}
            and (root / "acceptance-sentinel.txt").read_bytes() == SENTINEL, "Prepared configuration differs")
    if app["role"] == "client":
        require(_json(root / "client/host.json") == {"LoopbackPort": app["port"]}
                and _json(root / "client/connection.json") == {"DeviceName": lifecycle.CLIENT_DEVICE_NAME,
                    "Servers": [], "ActiveServerId": None}, "Prepared client configuration differs")


def _producer(producer_dir):
    root = Path(producer_dir)
    require(root.is_absolute() and root == root.resolve() and root.is_dir(), "Producer root must be canonical")
    manifest = _json(root / "verified-producer.json", 4096)
    require(set(manifest) == MANIFEST_KEYS and manifest["verified"] is True and manifest["area"] == "macos-data"
            and manifest["rid"] == "osx-arm64" and type(manifest["runId"]) is int and manifest["runId"] > 0
            and re.fullmatch(r"[0-9a-f]{40}", manifest["headSHA"])
            and re.fullmatch(r"[0-9a-f]{64}", manifest["artifactSHA256"]), "Verified producer manifest is invalid")
    results = root / "historical-results"
    report_path = _hash(results / "report.json", manifest["reportSHA256"], 8 * 1024 * 1024)
    producer = _json(report_path)
    require(producer.get("rid") == "osx-arm64" and producer.get("scope") == retention.SCOPE
            and producer.get("executionHeadSHA") == manifest["headSHA"], "Producer execution identity differs")
    seed = copy.deepcopy(producer.get("macosDataSeed"))
    require(isinstance(seed, dict) and seed.get("passed") is True and seed.get("oldVersion") == release.OLD_VERSION
            and seed.get("oldSourceSHA") == release.RELEASE_SHA and isinstance(seed.get("baselineSemantics"), dict)
            and seed["baselineSemantics"] and producer.get("dataSeed") == seed
            and producer.get("originalSemantics") == seed["baselineSemantics"], "Original API seed is incomplete")
    require(set(seed.get("requiredTables", [])) == set(seed_module.REQUIRED_COUNTS)
            and seed.get("minimumTableRows") == seed_module.REQUIRED_COUNTS, "Original table coverage differs")
    require(len(seed.get("resourceIds", [])) == 3 and len(set(seed["resourceIds"])) == 3
            and all(type(i) is int and i > 0 for i in seed["resourceIds"]), "Original resource identity is invalid")
    for role in ROLES:
        app = producer["apps"][role]
        installed = producer["originalDataSeedInstallations"][role]
        startup, audit = installed["startup"], installed["contentAudit"]
        proof, launch = producer["originalDataPreinstallProofs"][role], startup["initialLaunch"]
        product = base.contract.PRODUCTS[role]
        package, pinned = app["packageAudit"], release.pin(role, "osx-arm64", "installer")
        require(package["passed"] is True and package["releaseID"] == release.RELEASE_ID
                and package["releaseTag"] == release.TAG and package["payloadSource"] == "original-pkg-expanded"
                and package["artifacts"] == {"installer": {key: pinned[key] for key in ("file", "sizeBytes", "sha256")}}
                and package["manifest"] == audit["manifest"]
                and package["runtimeIdentity"]["verifiedAssetRID"] == "osx-arm64",
                "Original ARM installer does not match the pinned published release")
        require(app["rid"] == "osx-arm64" and app["role"] == role and installed["passed"] is True
                and startup["passed"] is True and startup["role"] == role
                and startup["executable"] == app["executable"] == proof["executable"]
                and startup["effectiveDataDirectory"] == app["defaultData"] == proof["dataDirectory"]
                and installed["target"] == proof["installRoot"] and proof["installRootAbsent"] is True
                and proof["exactExecutableProcessIds"] == [] and proof["rid"] == "osx-arm64"
                and proof["role"] == role and proof["version"] == release.OLD_VERSION
                and len(startup["processIds"]) == 1 and type(startup["processIds"][0]) is int
                and startup["processIds"][0] > 0, "Original installed process proof is invalid")
        require(audit["passed"] is True and audit["productHashesVerified"] is True and audit["productFileCount"] > 0
                and audit["manifest"]["id"] == product["assembly"] and audit["manifest"]["mainExe"] == product["assembly"]
                and audit["manifest"]["version"] == release.OLD_VERSION and audit.get("marker") is None
                and launch["preinstallProofVerified"] is True and launch["unmodifiedOriginalPayloadVerified"] is True
                and launch["normalBundleLaunchVerified"] is False
                and launch["method"] in ("installer-postinstall-automatic", "exact-unmodified-installed-executable-for-data-seeding")
                and startup["appInfo"]["version" if role == "client" else "coreVersion"] == release.OLD_VERSION
                and producer["originalVersions"][role] == release.OLD_VERSION, "Original v349 payload proof is invalid")
    return manifest, producer, seed, results


def prepare_restore(apps, producer_dir, report):
    """Validate everything first; copy only original bytes into owned empty fixtures."""
    _guard(apps)
    for app in apps.values(): _fixture(app, report)
    manifest, producer, seed, results = _producer(producer_dir)
    database = producer["originalDatabaseBackup"]
    require(database["file"] == "before-update.sqlite" and database["integrity"] == "ok"
            and database["foreignKeyViolations"] == [], "Original pre-update database proof is invalid")
    db = _hash(results / "unified/before-update.sqlite", database["sha256"], 32 * 1024 * 1024)
    require(db.stat().st_size == database["sizeBytes"], "Original database size differs")
    wal = Path(str(db) + "-wal")
    require(not wal.is_symlink() and (not wal.exists() or (wal.is_file() and wal.stat().st_size == 0)),
            "Original backup has an unverified nonempty WAL")
    with contextlib.closing(sqlite3.connect(db.as_uri() + "?mode=ro&immutable=1", uri=True, timeout=5)) as connection:
        deadline = time.monotonic() + 10
        connection.set_progress_handler(lambda: int(time.monotonic() >= deadline), 10000)
        require(connection.execute("PRAGMA integrity_check").fetchall() == [("ok",)]
                and connection.execute("PRAGMA foreign_key_check").fetchall() == [], "Original SQLite is invalid")
    tables = retention.snapshot_tables(db, seed["requiredTables"], immutable=True)
    require(retention.canonical(tables) == retention.canonical(_json(results / "unified/before-tables.json")),
            "Original full table evidence differs from SQLite")
    for table, minimum in seed_module.REQUIRED_COUNTS.items():
        require(len(tables[table]["rows"]) >= minimum, "Original table coverage is empty")
    require(sorted(row["Id"] for row in tables["ResourcesV2"]["rows"]) == sorted(seed["resourceIds"]),
            "Original resource inventory differs")
    configurations, hashes, ports, selected = {}, {}, {}, {}
    for role in ROLES:
        require(str(apps[role]["data"]) == producer["apps"][role]["defaultData"], "Original default data path cannot be rebased")
        evidence = producer["originalConfigurationBackups"][role]
        require(set(evidence["configurationHashes"]) == set(evidence["configurationFiles"]) == CONFIG_NAMES[role],
                "Original configuration inventory differs")
        configurations[role], hashes[role] = {}, dict(evidence["configurationHashes"])
        for name in sorted(CONFIG_NAMES[role]):
            require(evidence["configurationFiles"][name] == "before-update-configuration/" + name,
                    "Restore requires pre-update original configuration")
            source = _hash(results / role / evidence["configurationFiles"][name], hashes[role][name], 256 * 1024)
            configurations[role][name] = source.read_bytes()
        config = json.loads(configurations[role]["app.json"].decode("utf-8-sig"))["App"]
        selected[role] = producer["originalConfiguration"][role]
        options = selected[role]["options"]
        require(all(config.get(key) == value for key, value in options.items())
                and configurations[role]["acceptance-sentinel.txt"] == SENTINEL
                and selected[role]["sentinelSHA256"] == hashes[role]["acceptance-sentinel.txt"],
                "Original selected configuration differs")
        require(isinstance(config.get("listeningPorts"), list) and len(config["listeningPorts"]) == 1,
                "Original listening port is ambiguous")
        port = config["listeningPorts"][0]
        require(type(port) is int and 0 < port <= 65535 and port == producer["apps"][role]["port"], "Original port is invalid")
        ports[role] = port
        expected_options = {"language": "en-US", "enableAnonymousDataTracking": False,
                            "listeningPorts": [port], "autoListeningPortCount": 0, "maxParallelism": 1,
                            "enablePreReleaseChannel": False}
        require(options == expected_options, "Original retained option coverage differs")
        if role == "client":
            host = json.loads(configurations[role]["client/host.json"].decode("utf-8-sig"))
            connection = json.loads(configurations[role]["client/connection.json"].decode("utf-8-sig"))
            require(host == {"LoopbackPort": port} and connection == {"DeviceName": lifecycle.CLIENT_DEVICE_NAME,
                    "Servers": [], "ActiveServerId": None}, "Original client settings differ")
            require(producer["clientSettingsBaseline"] == {name: hashes[role]["client/" + name]
                    for name in ("host.json", "connection.json")}, "Original client hashes differ")
    require(ports["client"] != ports["unified"], "Original ports overlap")
    source_root = Path(seed["sourceRoot"])
    runner = Path(os.environ["RUNNER_TEMP"]).resolve()
    require(source_root.is_absolute() and source_root == source_root.resolve() and source_root.is_relative_to(runner)
            and source_root != runner and not source_root.exists() and not source_root.is_symlink(),
            "Original media path must be new and unchanged inside this RUNNER_TEMP")
    require(seed["directoryPath"] == str(source_root / "旧版 媒体库") and len(seed["mediaFiles"]) == 1,
            "Original media inventory differs")
    media = seed["mediaFiles"][0]
    destination = Path(media["path"])
    require(destination == source_root / "旧版 媒体库/样本 一.txt" and type(media["sizeBytes"]) is int
            and 0 < media["sizeBytes"] <= 4096, "Original media path or size differs")
    media_source = _hash(results / "unified/source-media" / destination.relative_to(source_root), media["sha256"], 4096)
    require(media_source.stat().st_size == media["sizeBytes"], "Original source size differs")
    evidence = report.setdefault("macosDataRestore", {"passed": False, "producer": manifest,
        "sourceRoot": str(source_root), "sourceRootCreated": False, "mediaCreatedDirectories": [],
        "restoredFiles": [], "ports": ports, "databaseSHA256": database["sha256"]})
    require(evidence["passed"] is False and not evidence["restoredFiles"], "Restore cannot be repeated")
    with contextlib.ExitStack() as sockets:
        for port in ports.values():
            sockets.enter_context(socket.socket()).bind(("127.0.0.1", port))
        # Recheck the current fixture immediately before the first write.
        for app in apps.values(): _fixture(app, report)
        missing, directory = [], destination.parent
        while not directory.exists():
            require(directory.is_relative_to(runner) and directory != runner and not directory.is_symlink(),
                    "Media creation escaped the runner temporary directory")
            missing.append(directory)
            directory = directory.parent
        for directory in reversed(missing):
            directory.mkdir()
            evidence["mediaCreatedDirectories"].append(str(directory))
            if directory == source_root: evidence["sourceRootCreated"] = True
        with destination.open("xb") as target: target.write(media_source.read_bytes())
        evidence["restoredFiles"].append(str(destination))
        target_db = apps["unified"]["data"] / "bakabase_insideworld.db"
        with db.open("rb") as source, target_db.open("xb") as target:
            shutil.copyfileobj(source, target, length=65536)
        evidence["restoredFiles"].append(str(target_db))
        _hash(target_db, database["sha256"], 32 * 1024 * 1024)
        for role, app in apps.items():
            for name, data in configurations[role].items():
                target = _file(app["data"] / name, 256 * 1024)
                target.write_bytes(data)
                _hash(target, hashes[role][name], 256 * 1024)
                evidence["restoredFiles"].append(str(target))
            app["port"] = ports[role]
            if role in report.get("apps", {}): report["apps"][role]["port"] = ports[role]
    evidence["passed"] = True
    return {"seed": seed, "tables": tables, "configuration": selected,
            "clientSettingsBaseline": producer["clientSettingsBaseline"], "producer": manifest,
            "sourceRoot": str(source_root), "configurationHashes": hashes}


def verify_restore(apps, report, phase, baseline):
    """Check candidate APIs, all old SQL fields, selected settings and original files."""
    _guard(apps)
    require(re.fullmatch(r"[a-z][a-z0-9-]{0,48}", phase or ""), "Invalid restore phase")
    require(report.get("macosDataRestore", {}).get("passed") is True
            and baseline["producer"] == report["macosDataRestore"]["producer"], "Verified restore baseline is missing")
    checks = report.setdefault("macosDataRestoreChecks", {})
    require(phase not in checks, "Restore verification phase cannot be repeated")
    checks[phase] = check = {"passed": False}
    observed = {role: lifecycle.observe_app(app) for role, app in apps.items()}
    for role, state in observed.items():
        require(len(state["processIds"]) == 1
                and state["appInfo"]["version" if role == "client" else "coreVersion"] == release.CANDIDATE_CORE,
                "Restored data must be read by the pinned candidate code")
    require(set(observed["client"]["processIds"]).isdisjoint(observed["unified"]["processIds"]), "Product processes overlap")
    destination = apps["unified"]["results"] / ("restored-" + phase + ".sqlite")
    check["backup"] = dict(retention.backup_database(apps["unified"]["data"] / "bakabase_insideworld.db", destination),
                           file=destination.name)
    actual = retention.snapshot_tables(destination, baseline["seed"]["requiredTables"])
    check["retention"] = retention.verify_rows_retained(baseline["tables"], actual)
    require(sorted(row["Id"] for row in actual["ResourcesV2"]["rows"]) == sorted(baseline["seed"]["resourceIds"]),
            "Restored resource inventory differs")
    (apps["unified"]["results"] / ("restored-" + phase + "-tables.json")).write_text(
        json.dumps(actual, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    evidence_count = len(baseline["seed"].get("verificationApiEvidence", []))
    try:
        check["semantics"] = seed_module.verify_semantics(apps["unified"], baseline["seed"])
    finally:
        # Preserve raw responses even when a subsequent semantic assertion fails.
        evidence = baseline["seed"].get("verificationApiEvidence", [])
        check["apiEvidence"] = evidence[evidence_count:]
    require(retention.canonical(check["semantics"]) == retention.canonical(baseline["seed"]["baselineSemantics"]),
            "Restored API meaning differs")
    check["configuration"] = {role: retention.original.config_state(app) for role, app in apps.items()}
    require(check["configuration"] == baseline["configuration"], "Original selected configuration changed")
    check["clientSettings"] = lifecycle.verify_client_state(apps["client"], baseline["clientSettingsBaseline"])
    check.update(sourceMediaRetained=True, passed=True)
    return check
