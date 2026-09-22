#!/usr/bin/env python3
"""Synthetic files/SQLite only; no product, account, GUI or native authentication."""
import contextlib
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import socket
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("restore_tests", Path(__file__).with_name("macos-data-restore.py"))
restore = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(restore)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


class RestoreTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name).resolve()
        self.home, self.runner, self.producer_dir = (self.root / x for x in ("home", "runner", "producer"))
        self.runner.mkdir()
        self.results = self.producer_dir / "historical-results"
        self.source_root = self.runner / "historical-results/unified/source-media"
        self.media_path = self.source_root / "旧版 媒体库/样本 一.txt"
        self.apps, self.report = {}, {"preflightAbsentPaths": []}
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(Path, "home", return_value=self.home))
        self.stack.enter_context(patch.dict(os.environ, {"RUNNER_TEMP": str(self.runner)}))
        self.stack.enter_context(patch.object(restore.release, "hosted"))
        self.stack.enter_context(patch.object(restore.base, "native_processes", return_value=[]))
        self.ports = {"client": free_port(), "unified": free_port()}
        while self.ports["client"] == self.ports["unified"]: self.ports["unified"] = free_port()
        defaults = restore.lifecycle.default_paths("osx-x64", self.home, os.environ)["data"]
        old = {"rid": "osx-arm64", "scope": restore.retention.SCOPE, "executionHeadSHA": "a" * 40,
               "apps": {}, "originalDataSeedInstallations": {}, "originalDataPreinstallProofs": {},
               "originalVersions": {}, "originalConfiguration": {}, "originalConfigurationBackups": {}}
        for role in restore.ROLES:
            app = {"role": role, "rid": "osx-x64", "environment": {}, "data": defaults[role],
                   "installRoot": self.root / "Applications" / (role + ".app"),
                   "exe": self.root / "Applications" / (role + ".app/Contents/MacOS/" + role),
                   "results": self.root / "results" / role, "version": restore.release.CANDIDATE_VERSION}
            app["results"].mkdir(parents=True)
            app["port"] = restore.base.prepare_data(app["data"], role)
            fixture = json.loads((app["data"] / "app.json").read_text())
            fixture["App"]["enablePreReleaseChannel"] = False
            write_json(app["data"] / "app.json", fixture)
            if role == "client":
                write_json(app["data"] / "client/connection.json", {"DeviceName": restore.lifecycle.CLIENT_DEVICE_NAME,
                           "Servers": [], "ActiveServerId": None})
            self.apps[role] = app
            self.report["preflightAbsentPaths"] += [str(app["data"]), str(app["installRoot"])]
            old["apps"][role] = {"role": role, "rid": "osx-arm64", "executable": str(app["exe"]),
                                  "defaultData": str(app["data"]), "port": self.ports[role]}
            old["originalVersions"][role] = restore.release.OLD_VERSION
            old["originalDataPreinstallProofs"][role] = dict(old["apps"][role], installRoot=str(app["installRoot"]),
                dataDirectory=str(app["data"]), version=restore.release.OLD_VERSION, installRootAbsent=True,
                exactExecutableProcessIds=[])
            product = restore.base.contract.PRODUCTS[role]["assembly"]
            old["originalDataSeedInstallations"][role] = {"passed": True, "target": str(app["installRoot"]),
                "contentAudit": {"passed": True, "productHashesVerified": True, "productFileCount": 500,
                    "manifest": {"id": product, "mainExe": product, "version": restore.release.OLD_VERSION}, "marker": None},
                "startup": {"passed": True, "role": role, "executable": str(app["exe"]), "processIds": [731],
                    "effectiveDataDirectory": str(app["data"]),
                    "appInfo": {"version" if role == "client" else "coreVersion": restore.release.OLD_VERSION},
                    "initialLaunch": {"preinstallProofVerified": True, "unmodifiedOriginalPayloadVerified": True,
                        "normalBundleLaunchVerified": False, "method": "installer-postinstall-automatic"}}}
            pinned = restore.release.pin(role, "osx-arm64", "installer")
            old["apps"][role]["packageAudit"] = {"passed": True, "releaseID": restore.release.RELEASE_ID,
                "releaseTag": restore.release.TAG, "payloadSource": "original-pkg-expanded",
                "artifacts": {"installer": {key: pinned[key] for key in ("file", "sizeBytes", "sha256")}},
                "manifest": old["originalDataSeedInstallations"][role]["contentAudit"]["manifest"],
                "runtimeIdentity": {"verifiedAssetRID": "osx-arm64"}}
            options = dict(fixture["App"], listeningPorts=[self.ports[role]])
            config = {"app.json": json.dumps({"App": dict(options, version=restore.release.OLD_VERSION)}).encode(),
                      "acceptance-sentinel.txt": restore.SENTINEL}
            if role == "client":
                config.update({"client/host.json": json.dumps({"LoopbackPort": self.ports[role]}).encode(),
                               "client/connection.json": (app["data"] / "client/connection.json").read_bytes()})
            evidence = {"configurationHashes": {}, "configurationFiles": {}}
            for name, value in config.items():
                relative = "before-update-configuration/" + name
                path = self.results / role / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(value)
                evidence["configurationHashes"][name] = digest(path)
                evidence["configurationFiles"][name] = relative
            old["originalConfigurationBackups"][role] = evidence
            old["originalConfiguration"][role] = {"options": options,
                "sentinelSHA256": evidence["configurationHashes"]["acceptance-sentinel.txt"]}
        old["clientSettingsBaseline"] = {name: old["originalConfigurationBackups"]["client"]["configurationHashes"]["client/" + name]
                                         for name in ("host.json", "connection.json")}
        source = self.results / "unified/source-media/旧版 媒体库/样本 一.txt"
        source.parent.mkdir(parents=True)
        source.write_bytes(restore.seed_module.FILE_BYTES)
        seed = {"passed": True, "oldVersion": restore.release.OLD_VERSION, "oldSourceSHA": restore.release.RELEASE_SHA,
                "requiredTables": list(restore.seed_module.REQUIRED_COUNTS),
                "minimumTableRows": restore.seed_module.REQUIRED_COUNTS.copy(), "resourceIds": [11, 12, 13],
                "sourceRoot": str(self.source_root), "directoryPath": str(self.source_root / "旧版 媒体库"),
                "mediaFiles": [{"path": str(self.media_path), "sizeBytes": source.stat().st_size, "sha256": digest(source)}],
                "baselineSemantics": {"resources": [{"id": i} for i in (11, 12, 13)]}}
        old.update(macosDataSeed=seed, dataSeed=copy.deepcopy(seed), originalSemantics=seed["baselineSemantics"])
        self.db = self.results / "unified/before-update.sqlite"
        with contextlib.closing(sqlite3.connect(self.db)) as db:
            for name, count in restore.seed_module.REQUIRED_COUNTS.items():
                db.execute('CREATE TABLE "' + name + '" (Id INTEGER PRIMARY KEY, Value TEXT)')
                db.executemany('INSERT INTO "' + name + '" VALUES (?, ?)', [(11 + i, "旧值 Ω") for i in range(count)])
            db.commit()
        tables = restore.retention.snapshot_tables(self.db, seed["requiredTables"])
        write_json(self.results / "unified/before-tables.json", tables)
        old["originalDatabaseBackup"] = {"file": "before-update.sqlite", "sha256": digest(self.db),
            "sizeBytes": self.db.stat().st_size, "integrity": "ok", "foreignKeyViolations": []}
        self.old = old
        self.persist()

    def persist(self):
        path = self.results / "report.json"
        write_json(path, self.old)
        write_json(self.producer_dir / "verified-producer.json", {"verified": True, "area": "macos-data", "rid": "osx-arm64",
            "runId": 123, "headSHA": "a" * 40, "artifactSHA256": "b" * 64, "reportSHA256": digest(path)})

    def prepare(self):
        return restore.prepare_restore(self.apps, self.producer_dir, self.report)

    def assert_untouched(self):
        self.assertFalse(self.source_root.exists())
        self.assertFalse((self.apps["unified"]["data"] / "bakabase_insideworld.db").exists())
        for app in self.apps.values():
            self.assertNotIn("version", json.loads((app["data"] / "app.json").read_text())["App"])

    def test_restore_preserves_database_configuration_and_media_bytes(self):
        baseline = self.prepare()
        self.assertEqual(digest(self.db), digest(self.apps["unified"]["data"] / "bakabase_insideworld.db"))
        self.assertEqual(restore.seed_module.FILE_BYTES, self.media_path.read_bytes())
        for role, app in self.apps.items():
            self.assertEqual(self.ports[role], app["port"])
            for name in restore.CONFIG_NAMES[role]:
                self.assertEqual((self.results / role / "before-update-configuration" / name).read_bytes(),
                                 (app["data"] / name).read_bytes())
        proof = self.report["macosDataRestore"]
        self.assertTrue(proof["passed"])
        self.assertTrue(proof["sourceRootCreated"])
        self.assertIn(str(self.source_root), proof["mediaCreatedDirectories"])
        self.assertEqual(self.old["originalSemantics"], baseline["seed"]["baselineSemantics"])
        with self.assertRaises(AssertionError): self.prepare()

    def test_hosted_guard_precedes_all_file_reads_and_process_queries(self):
        with patch.object(restore.release, "hosted", side_effect=AssertionError("hosted")), \
             patch.object(restore.base, "native_processes") as native, patch.object(restore, "_producer") as producer:
            with self.assertRaisesRegex(AssertionError, "hosted"): self.prepare()
        native.assert_not_called()
        producer.assert_not_called()

    def test_old_proof_failures_never_write_consumer_data(self):
        original = copy.deepcopy(self.old)
        for failure in ("seed", "baseline", "version", "payload", "preinstall", "tables", "stopped-config", "empty-options",
                        "release", "asset", "asset-rid"):
            self.old = copy.deepcopy(original)
            if failure == "seed": self.old["macosDataSeed"]["passed"] = False
            if failure == "baseline": self.old["originalSemantics"] = {}
            if failure == "version": self.old["originalVersions"]["client"] = "new"
            if failure == "payload": self.old["originalDataSeedInstallations"]["client"]["contentAudit"]["productHashesVerified"] = False
            if failure == "preinstall": self.old["originalDataPreinstallProofs"]["client"]["exactExecutableProcessIds"] = [1]
            if failure == "tables": self.old["macosDataSeed"]["requiredTables"] = []
            if failure == "stopped-config": self.old["originalConfigurationBackups"]["client"]["configurationFiles"]["app.json"] = "stopped-configuration/app.json"
            if failure == "empty-options": self.old["originalConfiguration"]["client"]["options"] = {}
            if failure == "release": self.old["apps"]["client"]["packageAudit"]["releaseID"] += 1
            if failure == "asset": self.old["apps"]["unified"]["packageAudit"]["artifacts"]["installer"]["sha256"] = "0" * 64
            if failure == "asset-rid": self.old["apps"]["client"]["packageAudit"]["runtimeIdentity"]["verifiedAssetRID"] = "osx-x64"
            self.persist()
            with self.subTest(failure=failure), self.assertRaises(AssertionError): self.prepare()
            self.assert_untouched()

    def test_manifest_or_original_bytes_mismatch_never_writes(self):
        for path in (self.producer_dir / "verified-producer.json", self.results / "report.json", self.db,
                     self.results / "client/before-update-configuration/app.json", self.results / "unified/source-media/旧版 媒体库/样本 一.txt"):
            original = path.read_bytes()
            path.write_bytes(b"{}" if path.suffix == ".json" else b"damaged")
            try:
                with self.subTest(path=path), self.assertRaises((AssertionError, KeyError)): self.prepare()
                self.assert_untouched()
            finally: path.write_bytes(original)

    def test_existing_data_process_or_media_path_is_never_overwritten(self):
        db = self.apps["unified"]["data"] / "bakabase_insideworld.db"
        db.write_bytes(b"unowned")
        with self.assertRaises(AssertionError): self.prepare()
        self.assertEqual(b"unowned", db.read_bytes())
        db.unlink()
        with patch.object(restore.base, "native_processes", return_value=[731]):
            with self.assertRaises(AssertionError): self.prepare()
        self.source_root.mkdir(parents=True)
        with self.assertRaises(AssertionError): self.prepare()
        self.assertFalse(self.media_path.exists())

    def test_nondefault_data_or_missing_pristine_proof_fails(self):
        self.report["preflightAbsentPaths"] = []
        with self.assertRaises(AssertionError): self.prepare()
        self.assert_untouched()
        self.apps["client"]["data"] = self.root / "unowned"
        with self.assertRaises(AssertionError): self.prepare()

    def test_occupied_original_port_fails_before_any_write(self):
        with socket.socket() as blocker:
            blocker.bind(("127.0.0.1", self.ports["client"]))
            with self.assertRaises(OSError): self.prepare()
        self.assert_untouched()

    def test_nonempty_wal_is_rejected_but_empty_sidecars_are_not_imported(self):
        wal, shm = Path(str(self.db) + "-wal"), Path(str(self.db) + "-shm")
        wal.write_bytes(b"not covered by the main database hash")
        with self.assertRaisesRegex(AssertionError, "nonempty WAL"): self.prepare()
        self.assert_untouched()
        wal.write_bytes(b"")
        shm.write_bytes(b"unused when main database is immutable")
        self.prepare()
        target = self.apps["unified"]["data"] / "bakabase_insideworld.db"
        self.assertFalse(Path(str(target) + "-wal").exists())
        self.assertFalse(Path(str(target) + "-shm").exists())
        self.assertEqual(digest(self.db), digest(target))

    def test_outside_or_rebased_media_path_is_rejected(self):
        seed = self.old["macosDataSeed"]
        for path in (self.root / "outside", self.source_root / "changed"):
            seed["mediaFiles"][0]["path"] = str(path)
            self.old["dataSeed"] = copy.deepcopy(seed)
            self.persist()
            with self.subTest(path=path), self.assertRaises(AssertionError): self.prepare()
            self.assert_untouched()

    def test_partial_copy_failure_records_owned_media_cleanup_paths(self):
        with patch.object(restore.shutil, "copyfileobj", side_effect=OSError("bounded fixture failure")):
            with self.assertRaises(OSError): self.prepare()
        proof = self.report["macosDataRestore"]
        self.assertFalse(proof["passed"])
        self.assertTrue(proof["sourceRootCreated"])
        self.assertIn(str(self.source_root), proof["mediaCreatedDirectories"])
        self.assertIn(str(self.media_path), proof["restoredFiles"])

    def test_verify_preserves_original_rows_and_rejects_changes(self):
        baseline = self.prepare()
        def observe(app):
            return {"processIds": [731 if app["role"] == "client" else 732],
                    "appInfo": {"version" if app["role"] == "client" else "coreVersion": restore.release.CANDIDATE_CORE}}
        with patch.object(restore.lifecycle, "observe_app", side_effect=observe), \
             patch.object(restore.seed_module, "verify_semantics", return_value=baseline["seed"]["baselineSemantics"]):
            self.assertTrue(restore.verify_restore(self.apps, self.report, "after-initial-install", baseline)["passed"])
            with contextlib.closing(sqlite3.connect(self.apps["unified"]["data"] / "bakabase_insideworld.db")) as db:
                db.execute("UPDATE ResourcesV2 SET Value='lost' WHERE Id=11")
                db.commit()
            with self.assertRaises(AssertionError):
                restore.verify_restore(self.apps, self.report, "after-native-updates", baseline)
            self.assertFalse(self.report["macosDataRestoreChecks"]["after-native-updates"]["passed"])

    def test_failed_semantics_preserves_raw_candidate_response_evidence(self):
        baseline = self.prepare()
        evidence = [{"method": "GET", "path": "/resource/keys", "response": {"code": 0, "data": []}}]
        def observe(app):
            return {"processIds": [731 if app["role"] == "client" else 732],
                    "appInfo": {"version" if app["role"] == "client" else "coreVersion": restore.release.CANDIDATE_CORE}}
        def semantics(app, seed):
            seed.setdefault("verificationApiEvidence", []).append(evidence)
            raise AssertionError("missing resources")
        with patch.object(restore.lifecycle, "observe_app", side_effect=observe), \
             patch.object(restore.seed_module, "verify_semantics", side_effect=semantics):
            with self.assertRaisesRegex(AssertionError, "missing resources"):
                restore.verify_restore(self.apps, self.report, "after-initial-install", baseline)
        check = self.report["macosDataRestoreChecks"]["after-initial-install"]
        self.assertFalse(check["passed"])
        self.assertEqual([evidence], check["apiEvidence"])


if __name__ == "__main__":
    unittest.main()
