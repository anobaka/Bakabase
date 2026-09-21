#!/usr/bin/env python3
"""Archive and real SQLite evidence boundaries for the native upgrade runner."""
import importlib.util
from pathlib import Path
import sqlite3
import stat
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("upgrade_runner", Path(__file__).with_name("run-velopack-macos.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class FixtureBoundaries(unittest.TestCase):
    def test_resource_response_requires_both_unique_created_ids(self):
        runner.validate_resource_ids([{"id": 2}, {"id": 1}], [1, 2])
        for response in (None, {}, [], [{"id": 1}], [{"id": 1}, {"id": 1}],
                         [{"id": 1}, {"id": 3}], [{"id": 1}, {}], [{"id": 1}, {"id": "2"}],
                         [{"id": 1}, {"id": 2}, {"id": 3}]):
            with self.subTest(response=response), self.assertRaises(AssertionError):
                runner.validate_resource_ids(response, [1, 2])
        with self.assertRaises(AssertionError):
            runner.validate_resource_ids([{"id": 1}, {"id": 2}], [1, 1])

    def test_database_evidence_requires_exactly_two_resource_rows(self):
        runner.validate_database_count({"tables": {"ResourcesV2": 2}})
        for tables in ({}, {"ResourcesV2": 0}, {"ResourcesV2": 1}, {"ResourcesV2": 3}):
            with self.subTest(tables=tables), self.assertRaises(AssertionError):
                runner.validate_database_count({"tables": tables})

    def archive(self, root, entries):
        path = root / "portable.zip"
        with zipfile.ZipFile(path, "w") as archive:
            for name, content, mode in entries:
                info = zipfile.ZipInfo(name)
                info.create_system = 3
                info.external_attr = mode << 16
                archive.writestr(info, content)
        return path

    def test_vpk_relative_manifest_link_and_executable_mode_survive(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = self.archive(root, [
                ("Bakabase.app/Contents/Resources/sq.version", "manifest", stat.S_IFREG | 0o644),
                ("Bakabase.app/Contents/MacOS/sq.version", "../Resources/sq.version", stat.S_IFLNK | 0o777),
                ("Bakabase.app/Contents/MacOS/Bakabase", "app", stat.S_IFREG | 0o755),
                ("__MACOSX/._metadata", "ignored", stat.S_IFREG | 0o644)])
            destination = root / "installed"
            destination.mkdir()
            runner.unpack_portable(archive, destination)
            self.assertEqual("manifest", (destination / "Bakabase.app/Contents/MacOS/sq.version").read_text())
            self.assertTrue((destination / "Bakabase.app/Contents/MacOS/Bakabase").stat().st_mode & stat.S_IXUSR)
            self.assertFalse((destination / "__MACOSX").exists())

    def test_archive_traversal_and_external_symlinks_are_rejected(self):
        for name, content, mode in [
                ("Bakabase.app/../../escape", "bad", stat.S_IFREG | 0o644),
                ("/absolute", "bad", stat.S_IFREG | 0o644),
                ("Other.app/file", "bad", stat.S_IFREG | 0o644),
                ("Bakabase.app/link", "../../escape", stat.S_IFLNK | 0o777)]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                archive = self.archive(root, [(name, content, mode)])
                destination = root / "installed"
                destination.mkdir()
                with self.assertRaises(ValueError):
                    runner.unpack_portable(archive, destination)
                self.assertFalse((root / "escape").exists())

    def test_sqlite_snapshot_includes_committed_wal_and_checks_integrity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            database = root / "actual.db"
            with sqlite3.connect(database) as live:
                live.execute("PRAGMA journal_mode=WAL")
                live.execute("CREATE TABLE Resources (id INTEGER PRIMARY KEY, name TEXT)")
                live.execute("INSERT INTO Resources VALUES (1, 'retained')")
                live.commit()
                result = runner.snapshot_db(database, root / "snapshot.sqlite")
            self.assertEqual("ok", result["integrity"])
            self.assertEqual(1, result["tables"]["Resources"])
            with sqlite3.connect(root / "snapshot.sqlite") as snapshot:
                self.assertEqual((1, "retained"), snapshot.execute("SELECT * FROM Resources").fetchone())


if __name__ == "__main__":
    unittest.main()
