#!/usr/bin/env python3
"""Small SQLite/mocked lifecycle tests. Never install or launch a native product."""
import contextlib
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location(
    "macos_data_retention_tests", Path(__file__).with_name("macos-data-retention.py"))
retention = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(retention)


class DatabaseFixture(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source.sqlite"

    def database(self):
        db = sqlite3.connect(self.source)
        self.addCleanup(db.close)
        return db

    def populated(self):
        db = self.database()
        db.execute("CREATE TABLE Resources (Id INTEGER PRIMARY KEY, Title TEXT, Optional TEXT, Content BLOB)")
        db.executemany("INSERT INTO Resources VALUES (?, ?, ?, ?)",
                       [(1, "published resource", None, b"\x00\xffbinary"),
                        (2, "second resource", "", b"")])
        db.commit()
        return db


class BackupTests(DatabaseFixture):
    def test_online_backup_includes_last_committed_wal_row_and_excludes_uncommitted_row(self):
        db = self.database()
        self.assertEqual("wal", db.execute("PRAGMA journal_mode=WAL").fetchone()[0])
        db.execute("PRAGMA wal_autocheckpoint=0")
        db.execute("CREATE TABLE Resources (Id INTEGER PRIMARY KEY, Title TEXT)")
        db.execute("INSERT INTO Resources VALUES (1, 'checkpointed')")
        db.commit()
        db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        db.execute("INSERT INTO Resources VALUES (2, 'last committed WAL resource')")
        db.commit()
        self.assertGreater(Path(str(self.source) + "-wal").stat().st_size, 0)
        db.execute("INSERT INTO Resources VALUES (3, 'must not retain uncommitted data')")
        target = self.root / "evidence.sqlite"
        evidence = retention.backup_database(self.source, target, timeout=2)
        with contextlib.closing(sqlite3.connect(target)) as backup:
            self.assertEqual([(1, "checkpointed"), (2, "last committed WAL resource")],
                             backup.execute("SELECT * FROM Resources ORDER BY Id").fetchall())
            self.assertEqual([("ok",)], backup.execute("PRAGMA integrity_check").fetchall())
        self.assertEqual("ok", evidence["integrity"])
        self.assertEqual([], evidence["foreignKeyViolations"])
        self.assertEqual(target.stat().st_size, evidence["sizeBytes"])
        self.assertEqual(hashlib.sha256(target.read_bytes()).hexdigest(), evidence["sha256"])
        self.assertEqual(3, db.execute("SELECT COUNT(*) FROM Resources").fetchone()[0])

    def test_missing_source_never_creates_database_or_evidence(self):
        target = self.root / "must-not-exist.sqlite"
        with self.assertRaises((AssertionError, FileNotFoundError, sqlite3.Error)):
            retention.backup_database(self.source, target, timeout=1)
        self.assertFalse(self.source.exists())
        self.assertFalse(target.exists())

    def test_existing_evidence_is_not_overwritten(self):
        self.populated()
        target = self.root / "existing.sqlite"
        original = b"pre-existing evidence must stay intact"
        target.write_bytes(original)
        with self.assertRaises((AssertionError, FileExistsError)):
            retention.backup_database(self.source, target, timeout=1)
        self.assertEqual(original, target.read_bytes())

    def test_same_source_and_target_is_rejected_without_altering_original(self):
        self.populated()
        original = self.source.read_bytes()
        with self.assertRaises((AssertionError, FileExistsError)):
            retention.backup_database(self.source, self.source, timeout=1)
        self.assertEqual(original, self.source.read_bytes())

    def test_invalid_database_does_not_produce_success_evidence(self):
        self.source.write_bytes(b"not a SQLite database")
        with self.assertRaises((AssertionError, sqlite3.Error)):
            retention.backup_database(self.source, self.root / "evidence.sqlite", timeout=1)

    def test_foreign_key_violations_remove_failed_evidence_copy(self):
        db = self.database()
        db.execute("CREATE TABLE Parent (Id INTEGER PRIMARY KEY)")
        db.execute("CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES Parent(Id))")
        db.execute("INSERT INTO Child VALUES (1, 99)")
        db.commit()
        target = self.root / "invalid-fk.sqlite"
        with self.assertRaisesRegex(AssertionError, "foreign-key"):
            retention.backup_database(self.source, target, timeout=1)
        self.assertFalse(target.exists())
        self.assertEqual([(1, 99)], db.execute("SELECT * FROM Child").fetchall())

    def test_backup_deadline_removes_incomplete_copy_without_changing_source(self):
        self.populated()
        original = self.source.read_bytes()
        target = self.root / "timed-out.sqlite"
        with patch.object(retention.time, "monotonic", side_effect=[0, 2]):
            with self.assertRaises(TimeoutError):
                retention.backup_database(self.source, target, timeout=1)
        self.assertFalse(target.exists())
        self.assertEqual(original, self.source.read_bytes())

    def test_invalid_timeouts_reject_before_creating_target(self):
        self.populated()
        for timeout in (0, -1, 31, float("inf"), float("nan"), True):
            target = self.root / "invalid-timeout.sqlite"
            with self.subTest(timeout=timeout), self.assertRaises(AssertionError):
                retention.backup_database(self.source, target, timeout=timeout)
            self.assertFalse(target.exists())

    def test_read_permission_failure_does_not_create_evidence_or_change_source(self):
        self.populated()
        original = self.source.read_bytes()
        target = self.root / "permission-denied.sqlite"
        with patch.object(retention.sqlite3, "connect", side_effect=PermissionError("owned fixture denial")):
            with self.assertRaises(PermissionError):
                retention.backup_database(self.source, target, timeout=1)
        self.assertFalse(target.exists())
        self.assertEqual(original, self.source.read_bytes())

    def test_late_evidence_hash_failure_removes_created_target_only(self):
        self.populated()
        original = self.source.read_bytes()
        target = self.root / "late-denial.sqlite"
        with patch.object(retention.base, "sha256", side_effect=PermissionError("owned evidence read denial")):
            with self.assertRaises(PermissionError):
                retention.backup_database(self.source, target, timeout=1)
        self.assertFalse(target.exists())
        self.assertEqual(original, self.source.read_bytes())

    @unittest.skipIf(os.name == "nt", "POSIX symlink fixture; Windows creation may require privileges")
    def test_source_and_dangling_target_symlinks_are_rejected(self):
        self.populated()
        link = self.root / "source-link.sqlite"
        link.symlink_to(self.source)
        target = self.root / "evidence.sqlite"
        with self.assertRaises(AssertionError):
            retention.backup_database(link, target, timeout=1)
        self.assertFalse(target.exists())
        target.symlink_to(self.root / "must-not-create.sqlite")
        with self.assertRaises(AssertionError):
            retention.backup_database(self.source, target, timeout=1)
        self.assertTrue(target.is_symlink())
        self.assertFalse((self.root / "must-not-create.sqlite").exists())


class SnapshotTests(DatabaseFixture):
    def test_snapshot_preserves_null_empty_blob_and_declared_primary_key(self):
        self.populated()
        before = self.source.read_bytes()
        value = retention.snapshot_tables(self.source, ["Resources"])
        table = value["Resources"]
        self.assertEqual(["Id", "Title", "Optional", "Content"], table["columns"])
        self.assertEqual(["Id"], table["primaryKey"])
        rows = {row["Id"]: row for row in table["rows"]}
        self.assertEqual({1, 2}, set(rows))
        self.assertIsNone(rows[1]["Optional"])
        self.assertEqual("", rows[2]["Optional"])
        self.assertEqual({"blobHex": "00ff62696e617279"}, rows[1]["Content"])
        self.assertEqual({"blobHex": ""}, rows[2]["Content"])
        self.assertEqual(value, json.loads(json.dumps(value)))
        self.assertEqual(before, self.source.read_bytes())

    def test_composite_primary_key_uses_declared_key_order(self):
        db = self.database()
        db.execute("CREATE TABLE Links (ResourceId INTEGER, TagId INTEGER, Value TEXT, PRIMARY KEY(TagId, ResourceId))")
        db.executemany("INSERT INTO Links VALUES (?, ?, ?)", [(1, 2, "one"), (1, 3, "two")])
        db.commit()
        table = retention.snapshot_tables(self.source, ["Links"])["Links"]
        self.assertEqual(["TagId", "ResourceId"], table["primaryKey"])
        self.assertEqual(2, len(table["rows"]))

    def test_missing_empty_and_no_primary_key_tables_fail_closed(self):
        db = self.populated()
        db.execute("CREATE TABLE Empty (Id INTEGER PRIMARY KEY)")
        db.execute("CREATE TABLE NoKey (Title TEXT)")
        db.execute("INSERT INTO NoKey VALUES ('ambiguous identity')")
        db.commit()
        for table in ("Missing", "Empty", "NoKey"):
            with self.subTest(table=table), self.assertRaises(AssertionError):
                retention.snapshot_tables(self.source, [table])

    def test_nullable_duplicate_composite_keys_are_not_usable_row_identity(self):
        db = self.database()
        db.execute("CREATE TABLE Links (A INTEGER, B INTEGER, Value TEXT, PRIMARY KEY (A,B))")
        db.executemany("INSERT INTO Links VALUES (?, ?, ?)", [(1, None, "first"), (1, None, "second")])
        db.commit()
        with self.assertRaises(AssertionError):
            retention.snapshot_tables(self.source, ["Links"])

    def test_untrusted_table_name_cannot_execute_sql(self):
        self.populated()
        with self.assertRaises((AssertionError, ValueError, sqlite3.Error)):
            retention.snapshot_tables(self.source, ['Resources; DROP TABLE Resources; --'])
        with contextlib.closing(sqlite3.connect(self.source)) as db:
            self.assertEqual(2, db.execute("SELECT COUNT(*) FROM Resources").fetchone()[0])

    def test_missing_snapshot_source_is_not_created(self):
        with self.assertRaises((AssertionError, FileNotFoundError, sqlite3.Error)):
            retention.snapshot_tables(self.source, ["Resources"])
        self.assertFalse(self.source.exists())

    def test_snapshot_rejects_row_count_and_per_row_evidence_overflow(self):
        db = self.populated()
        with patch.object(retention, "MAX_ROWS", 1), self.assertRaises(AssertionError):
            retention.snapshot_tables(self.source, ["Resources"])
        db.execute("UPDATE Resources SET Content = ? WHERE Id = 1", (b"x" * 32768,))
        db.commit()
        with self.assertRaisesRegex(AssertionError, "budget"):
            retention.snapshot_tables(self.source, ["Resources"])

    def test_snapshot_canonical_key_sort_is_deterministic(self):
        db = self.populated()
        db.execute("INSERT INTO Resources VALUES (10, 'ten', NULL, NULL)")
        db.commit()
        rows = retention.snapshot_tables(self.source, ["Resources"])["Resources"]["rows"]
        self.assertEqual([10, 1, 2], [row["Id"] for row in rows])


class RetentionTests(DatabaseFixture):
    def setUp(self):
        super().setUp()
        self.populated()
        self.before = retention.snapshot_tables(self.source, ["Resources"])

    def test_reordered_rows_and_new_rows_and_columns_are_allowed(self):
        after = copy.deepcopy(self.before)
        table = after["Resources"]
        table["columns"].append("MigrationVersion")
        for row in table["rows"]:
            row["MigrationVersion"] = 400
        table["rows"].reverse()
        table["rows"].append(dict(table["rows"][0], Id=3, Title="new resource"))
        result = retention.verify_rows_retained(self.before, after)
        self.assertTrue(result["passed"])

    def test_deleted_row_or_same_key_replacement_never_passes(self):
        for operation in ("delete", "replace", "change-key"):
            after = copy.deepcopy(self.before)
            rows = after["Resources"]["rows"]
            if operation == "delete":
                rows.pop()
            elif operation == "replace":
                rows[0]["Title"] = "replacement using the same original Id"
            else:
                rows[0]["Id"] = 99
            with self.subTest(operation=operation), self.assertRaises(AssertionError):
                retention.verify_rows_retained(self.before, after)

    def test_null_empty_and_blob_changes_are_not_equal(self):
        for column, value in (("Optional", ""), ("Optional", 0), ("Content", None),
                              ("Content", {"blobHex": "00fe62696e617279"})):
            after = copy.deepcopy(self.before)
            after["Resources"]["rows"][0][column] = value
            with self.subTest(column=column, value=value), self.assertRaises(AssertionError):
                retention.verify_rows_retained(self.before, after)

    def test_scalar_type_changes_cannot_hide_behind_python_equality(self):
        for changed in (True, 1.0):
            for column in ("Id", "Optional"):
                before = copy.deepcopy(self.before)
                before["Resources"]["rows"][0][column] = 1
                after = copy.deepcopy(before)
                after["Resources"]["rows"][0][column] = changed
                with self.subTest(column=column, value=changed), self.assertRaises(AssertionError):
                    retention.verify_rows_retained(before, after)

    def test_missing_table_column_and_changed_key_schema_are_rejected(self):
        for operation in ("table", "column", "field", "key"):
            after = copy.deepcopy(self.before)
            if operation == "table":
                after.clear()
            elif operation == "column":
                after["Resources"]["columns"].remove("Title")
            elif operation == "field":
                del after["Resources"]["rows"][0]["Title"]
            else:
                after["Resources"]["primaryKey"] = ["Title"]
            with self.subTest(operation=operation), self.assertRaises(AssertionError):
                retention.verify_rows_retained(self.before, after)

    def test_duplicate_original_key_cannot_hide_row_replacement(self):
        after = copy.deepcopy(self.before)
        replacement = dict(after["Resources"]["rows"][0], Title="wrong duplicate")
        after["Resources"]["rows"].append(replacement)
        with self.assertRaises(AssertionError):
            retention.verify_rows_retained(self.before, after)

    def test_malformed_schema_cannot_turn_original_field_comparison_into_vacuous_success(self):
        for change in ("empty-columns", "duplicate-columns", "undeclared-key", "null-key"):
            before = copy.deepcopy(self.before)
            table = before["Resources"]
            if change == "empty-columns": table["columns"] = []
            if change == "duplicate-columns": table["columns"].append("Title")
            if change == "undeclared-key": table["columns"].remove("Id")
            if change == "null-key": table["rows"][0]["Id"] = None
            after = copy.deepcopy(before)
            with self.subTest(change=change), self.assertRaises(AssertionError):
                retention.verify_rows_retained(before, after)

    def test_new_row_with_null_primary_key_is_not_valid_after_state(self):
        after = copy.deepcopy(self.before)
        after["Resources"]["rows"].append(dict(after["Resources"]["rows"][0], Id=None))
        with self.assertRaises(AssertionError):
            retention.verify_rows_retained(self.before, after)


class NativeGuardTests(unittest.TestCase):
    def test_hosted_guard_precedes_native_calls_and_disk_io(self):
        app = {"rid": "osx-arm64"}
        with patch.object(retention.release, "hosted", side_effect=AssertionError("hosted guard")) as guard, \
             patch("subprocess.Popen") as popen, patch("subprocess.run") as run, \
             patch.object(Path, "open") as opened, patch.object(Path, "stat") as stat:
            with self.assertRaisesRegex(AssertionError, "hosted guard"):
                retention.activate_original_for_data(app, {})
        guard.assert_called_once_with("osx-arm64")
        popen.assert_not_called()
        run.assert_not_called()
        opened.assert_not_called()
        stat.assert_not_called()

    def test_non_macos_activation_is_rejected_without_native_or_disk_side_effects(self):
        app = {"rid": "win-x64", "version": retention.release.OLD_VERSION}
        with patch.object(retention.release, "hosted") as guard, \
             patch.object(retention.original, "original_install_audit") as audit, \
             patch("subprocess.Popen") as popen, patch.object(Path, "open") as opened:
            with self.assertRaises(AssertionError):
                retention.activate_original_for_data(app, {})
        guard.assert_called_once_with("win-x64")
        audit.assert_not_called()
        popen.assert_not_called()
        opened.assert_not_called()


class NativeActivationTests(DatabaseFixture):
    def application(self):
        return {"rid": "osx-arm64", "role": "unified", "version": retention.release.OLD_VERSION,
                "installRoot": self.root / "Bakabase.app",
                "exe": self.root / "Bakabase.app/Contents/MacOS/Bakabase",
                "results": self.root, "data": self.root / "owned-default-data",
                "environment": {"OWNED_TEST": "1"}, "children": [], "childLogs": []}

    def observe(self, app):
        return {"appInfo": {"coreVersion": retention.release.OLD_VERSION},
                "processIds": [731], "effectiveDataDirectory": str(app["data"])}

    def test_exact_executable_launch_is_audited_before_and_after_and_registered_for_cleanup(self):
        app, prepared, events = self.application(), {"original": "pinned"}, []
        child = SimpleNamespace(pid=731)
        def audit(current, package):
            self.assertIs(app, current)
            self.assertIs(prepared, package)
            events.append("audit")
            return {"hash": "unchanged"}
        def launch(*args, **kwargs):
            events.append("launch")
            return child
        def observe(current, startup):
            events.append("observe")
            self.assertTrue(startup)
            self.assertIs(child, current["children"][0])
            self.assertEqual(1, len(current["childLogs"]))
            return self.observe(current)
        try:
            with patch.object(retention.release, "hosted") as guard, \
                 patch.object(retention.base, "native_processes", side_effect=[[], [731]]), \
                 patch.object(retention.original, "original_install_audit", side_effect=audit), \
                 patch.object(retention.subprocess, "Popen", side_effect=launch) as popen, \
                 patch.object(retention.lifecycle, "observe_app", side_effect=observe):
                result = retention.activate_original_for_data(app, prepared)
            guard.assert_called_once_with("osx-arm64")
            self.assertEqual(["audit", "launch", "observe", "audit"], events)
            self.assertEqual([str(app["exe"])], popen.call_args.args[0])
            self.assertEqual(app["exe"].parent, popen.call_args.kwargs["cwd"])
            self.assertIs(app["environment"], popen.call_args.kwargs["env"])
            self.assertIs(app["childLogs"][0], popen.call_args.kwargs["stdout"])
            self.assertIs(False, result["initialLaunch"]["normalBundleLaunchVerified"])
            self.assertTrue(result["initialLaunch"]["unmodifiedOriginalPayloadVerified"])
        finally:
            for log in app["childLogs"]:
                log.close()

    def test_wrong_running_version_pid_or_data_path_remains_failure_with_owned_child_registered(self):
        for change in ("version", "pid", "path", "payload", "multiple-pids", "late-pid"):
            app = self.application()
            observed = self.observe(app)
            if change == "version": observed["appInfo"]["coreVersion"] = "2.4.0-beta.400"
            if change == "pid": observed["processIds"] = [732]
            if change == "multiple-pids": observed["processIds"] = [731, 732]
            if change == "path": observed["effectiveDataDirectory"] = str(self.root / "other-data")
            audits = [{"hash": "old"}, {"hash": "changed" if change == "payload" else "old"}]
            child = SimpleNamespace(pid=731)
            try:
                with self.subTest(change=change), patch.object(retention.release, "hosted"), \
                     patch.object(retention.base, "native_processes", side_effect=[[], [732]]), \
                     patch.object(retention.original, "original_install_audit", side_effect=audits), \
                     patch.object(retention.subprocess, "Popen", return_value=child), \
                     patch.object(retention.lifecycle, "observe_app", return_value=observed):
                    with self.assertRaises(AssertionError):
                        retention.activate_original_for_data(app, {})
                self.assertEqual([child], app["children"])
                self.assertEqual(1, len(app["childLogs"]))
            finally:
                for log in app["childLogs"]:
                    log.close()

    def test_running_product_without_current_exercise_proof_never_launches_or_observes(self):
        for token in (None, object(), True, {"installRootAbsent": True}):
            app = self.application()
            # Arbitrary app metadata is not a capability issued by exercise.
            app["preinstallProof"] = {"installRootAbsent": True, "exactExecutableProcessIds": []}
            with self.subTest(token=type(token).__name__), patch.object(retention.release, "hosted"), \
                 patch.object(retention.base, "native_processes", return_value=[731]), \
                 patch.object(retention.original, "original_install_audit", return_value={"hash": "old"}), \
                 patch.object(retention.subprocess, "Popen") as launch, \
                 patch.object(retention.lifecycle, "observe_app") as observe, patch.object(Path, "open") as opened:
                with self.assertRaises(AssertionError):
                    retention.activate_original_for_data(app, {}, token)
            launch.assert_not_called()
            observe.assert_not_called()
            opened.assert_not_called()


class OriginalInstallationProofTests(DatabaseFixture):
    class StopAfterInstall(Exception):
        pass

    def applications(self):
        apps = {}
        for role in ("client", "unified"):
            results = self.root / role
            results.mkdir(exist_ok=True)
            bundle = self.root / (role + ".app")
            apps[role] = {"rid": "osx-arm64", "role": role, "version": retention.release.OLD_VERSION,
                          "installRoot": bundle, "exe": bundle / "Contents/MacOS/Bakabase",
                          "results": results, "data": self.root / (role + "-data"),
                          "environment": {}, "children": [], "childLogs": []}
        return apps

    def test_automatic_and_direct_activation_have_current_proof_and_distinct_mechanisms(self):
        apps, report, callbacks = self.applications(), {}, []
        packages = {role: {"role": role} for role in apps}
        feed = SimpleNamespace(manifest={"roles": packages})
        def install(app, label, audit, macos_initial_activation):
            self.assertFalse(app["installRoot"].exists())
            self.assertEqual([], report["originalDataPreinstallProofs"][app["role"]]["exactExecutableProcessIds"])
            callbacks.append((app, macos_initial_activation))
            return {"startup": macos_initial_activation(app)}
        def observe(app, startup=False):
            if not startup:
                raise self.StopAfterInstall()
            return {"appInfo": {"version" if app["role"] == "client" else "coreVersion": retention.release.OLD_VERSION},
                    "processIds": [731 if app["role"] == "client" else 732],
                    "effectiveDataDirectory": str(app["data"])}
        try:
            with patch.object(retention.release, "hosted"), patch.object(retention.lifecycle, "sibling"), \
                 patch.object(retention.base, "native_processes", side_effect=[[], [731], [731], [], [], [732]]), \
                 patch.object(retention.original, "original_install_audit", return_value={"hash": "old"}), \
                 patch.object(retention.lifecycle, "install_app", side_effect=install), \
                 patch.object(retention.lifecycle, "observe_app", side_effect=observe), \
                 patch.object(retention.subprocess, "Popen", return_value=SimpleNamespace(pid=732)) as launch:
                with self.assertRaises(self.StopAfterInstall):
                    retention.exercise(apps, report, feed)
            launch.assert_called_once()
            installations = report["originalDataSeedInstallations"]
            automatic = installations["client"]["startup"]["initialLaunch"]
            direct = installations["unified"]["startup"]["initialLaunch"]
            self.assertEqual("installer-postinstall-automatic", automatic["method"])
            self.assertFalse(automatic["bypassLaunchServicesForHistoricalDataSeed"])
            self.assertTrue(direct["bypassLaunchServicesForHistoricalDataSeed"])
            self.assertNotEqual(installations["client"]["mechanism"], installations["unified"]["mechanism"])
            for launch_evidence in (automatic, direct):
                self.assertTrue(launch_evidence["preinstallProofVerified"])
                self.assertFalse(launch_evidence["normalBundleLaunchVerified"])
                self.assertTrue(launch_evidence["unmodifiedOriginalPayloadVerified"])
            self.assertEqual([], apps["client"]["children"])
            self.assertFalse((apps["client"]["results"] / "historical-data-seed-executable.log").exists())
            self.assertEqual({}, retention._pending_original_installations)
            # The exact callback cannot be replayed after installation.
            with patch.object(retention.release, "hosted"), patch.object(retention.subprocess, "Popen") as replay_launch:
                with self.assertRaisesRegex(AssertionError, "proof is invalid"):
                    callbacks[0][1](callbacks[0][0])
            replay_launch.assert_not_called()
        finally:
            for app in apps.values():
                for log in app["childLogs"]:
                    log.close()

    def test_existing_install_or_process_blocks_installer_before_proof_is_issued(self):
        for condition in ("directory", "process"):
            apps, report = self.applications(), {}
            if condition == "directory": apps["client"]["installRoot"].mkdir()
            try:
                with self.subTest(condition=condition), patch.object(retention.release, "hosted"), \
                     patch.object(retention.lifecycle, "sibling"), \
                     patch.object(retention.base, "native_processes", return_value=[731] if condition == "process" else []), \
                     patch.object(retention.lifecycle, "install_app") as install:
                    with self.assertRaises(AssertionError):
                        retention.exercise(apps, report, SimpleNamespace(manifest={"roles": {"client": {}}}))
                install.assert_not_called()
                self.assertNotIn("originalDataPreinstallProofs", report)
                self.assertEqual({}, retention._pending_original_installations)
            finally:
                if condition == "directory": apps["client"]["installRoot"].rmdir()

    def test_automatic_pid_identity_payload_and_single_use_fail_closed(self):
        for change in ("multiple", "pid", "version", "path", "payload", "late-pid", "context", "replay", "installer-error"):
            apps, report = self.applications(), {}
            captured = []
            def install(app, label, audit, macos_initial_activation):
                captured.append((app, macos_initial_activation))
                if change == "installer-error": raise self.StopAfterInstall()
                if change == "context": app["data"] = self.root / "changed-data"
                result = macos_initial_activation(app)
                if change == "replay": macos_initial_activation(app)
                return {"startup": result}
            observed = {"appInfo": {"version": "wrong" if change == "version" else retention.release.OLD_VERSION},
                        "processIds": [732 if change == "pid" else 731],
                        "effectiveDataDirectory": str(self.root / "wrong" if change == "path" else apps["client"]["data"])}
            native = [[], [731, 732] if change == "multiple" else [731], [732] if change == "late-pid" else [731]]
            with self.subTest(change=change), patch.object(retention.release, "hosted"), \
                 patch.object(retention.lifecycle, "sibling"), \
                 patch.object(retention.base, "native_processes", side_effect=native), \
                 patch.object(retention.original, "original_install_audit", side_effect=[{"hash": "old"}, {"hash": "new" if change == "payload" else "old"}]), \
                 patch.object(retention.lifecycle, "install_app", side_effect=install), \
                 patch.object(retention.lifecycle, "observe_app", return_value=observed), \
                 patch.object(retention.subprocess, "Popen") as launch:
                with self.assertRaises((AssertionError, self.StopAfterInstall)):
                    retention.exercise(apps, report, SimpleNamespace(manifest={"roles": {"client": {}}}))
            launch.assert_not_called()
            self.assertEqual({}, retention._pending_original_installations)
            self.assertNotIn("originalDataSeedInstallations", report)
            with patch.object(retention.release, "hosted"):
                with self.assertRaisesRegex(AssertionError, "proof is invalid"):
                    captured[0][1](captured[0][0])


class PreservationTests(DatabaseFixture):
    def test_running_process_prevents_database_evidence_io(self):
        app = {"exe": self.root / "owned-executable"}
        with patch.object(retention.base, "native_processes", return_value=[731]), \
             patch.object(retention, "backup_database") as backup, patch.object(Path, "is_file") as is_file:
            with self.assertRaises(AssertionError):
                retention.preserve_data_evidence(app, {}, "unified")
        backup.assert_not_called()
        is_file.assert_not_called()

    def test_stopped_unified_evidence_uses_sqlite_backup_and_retains_committed_wal(self):
        data, results = self.root / "data", self.root / "results"
        data.mkdir()
        results.mkdir()
        source = data / "bakabase_insideworld.db"
        with contextlib.closing(sqlite3.connect(source)) as db:
            db.execute("PRAGMA journal_mode=WAL")
            db.execute("PRAGMA wal_autocheckpoint=0")
            db.execute("CREATE TABLE Resource (Id INTEGER PRIMARY KEY, Title TEXT)")
            db.commit()
            db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
            db.execute("INSERT INTO Resource VALUES (1, 'last committed row')")
            db.commit()
            config = b'{"App":{"language":"en-US"}}'
            (data / "app.json").write_bytes(config)
            app = {"exe": self.root / "owned-executable", "data": data, "results": results}
            report = {}
            with patch.object(retention.base, "native_processes", return_value=[]):
                retention.preserve_data_evidence(app, report, "unified")
            evidence = report["stoppedDataEvidence"]["unified"]
            backup_path = results / evidence["database"]["file"]
            with contextlib.closing(sqlite3.connect(backup_path)) as backup:
                self.assertEqual([(1, "last committed row")], backup.execute("SELECT * FROM Resource").fetchall())
            self.assertEqual(hashlib.sha256(config).hexdigest(), evidence["configurationHashes"]["app.json"])
            saved_config = results / evidence["configurationFiles"]["app.json"]
            self.assertEqual(config, saved_config.read_bytes())
            self.assertTrue(evidence["passed"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
