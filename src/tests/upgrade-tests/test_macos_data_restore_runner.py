"""Pure ownership/cleanup guards; never launch or inspect a native application."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("restore_runner_guards", Path(__file__).with_name("run-macos-data-restore.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class RestoreRunnerGuards(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.source = self.root / "prior-results/unified/source-media"
        self.source.mkdir(parents=True)
        (self.source / "folder").mkdir()
        (self.source / "folder/media.txt").write_bytes(b"original media bytes")
        self.data, self.results = self.root / "data", self.root / "results"
        self.data.mkdir()
        self.results.mkdir()
        (self.data / "app.json").write_text('{"App":{}}')
        self.app = {"exe": self.root / "owned-executable", "data": self.data, "results": self.results}
        self.report = {"macosDataRestore": {"sourceRootCreated": True, "sourceRoot": str(self.source),
            "mediaCreatedDirectories": [str(self.source.parent.parent), str(self.source.parent),
                                        str(self.source), str(self.source / "folder")]}}
        self.addCleanup(patch.stopall)
        patch.dict(os.environ, {"RUNNER_TEMP": str(self.root)}, clear=True).start()
        patch.object(runner.base, "native_processes", return_value=[]).start()

    def test_guard_precedes_input_files_and_native_lifecycle(self):
        with patch.object(runner.release, "hosted", side_effect=AssertionError("not hosted")), \
             patch.object(Path, "read_text") as read, patch.object(runner.lifecycle, "execute") as execute:
            with self.assertRaisesRegex(AssertionError, "not hosted"):
                runner.main(["--unified-packages", "/missing", "--client-packages", "/missing", "--provenance", "/missing",
                    "--updates-manifest", "/missing", "--producer-directory", "/missing", "--results-directory", "/missing",
                    "--rid", "osx-x64", "--version", "old", "--macos-native-authorization"])
        read.assert_not_called()
        execute.assert_not_called()

    def test_preserves_source_bytes_before_removing_only_created_media_directories(self):
        unrelated = self.root / "unrelated.txt"
        unrelated.write_text("keep")
        runner.preserve_and_remove_sources(self.app, self.report, "unified")
        self.assertTrue(self.report["restoredSourceFilesRemoved"])
        self.assertFalse(self.source.parent.parent.exists())
        evidence = self.report["restoredSourceEvidence"]
        self.assertEqual(1, len(evidence))
        self.assertEqual(b"original media bytes", (self.results / evidence[0]["file"]).read_bytes())
        self.assertEqual("keep", unrelated.read_text())
        self.assertTrue(self.data.exists())  # Shared lifecycle owns AppData removal.

    def test_no_creation_proof_never_removes_or_captures_source_path(self):
        self.report["macosDataRestore"]["sourceRootCreated"] = False
        runner.preserve_and_remove_sources(self.app, self.report, "unified")
        self.assertTrue((self.source / "folder/media.txt").exists())
        self.assertNotIn("restoredSourceEvidence", self.report)
        self.assertTrue(self.report["restoredSourceRetainedWithoutCreationProof"])

    def test_failed_source_root_creation_removes_only_recorded_empty_ancestors(self):
        shutil.rmtree(self.source)
        self.report["macosDataRestore"].update(sourceRootCreated=False,
            mediaCreatedDirectories=[str(self.source.parent.parent), str(self.source.parent)])
        unrelated = self.root / "keep-empty"
        unrelated.mkdir()
        with patch.object(runner.base, "remove_owned_tree") as remove:
            runner.preserve_and_remove_sources(self.app, self.report, "unified")
        remove.assert_not_called()
        self.assertFalse(self.source.parent.parent.exists())
        self.assertTrue(unrelated.is_dir())
        self.assertTrue(self.report["restoredSourceParentsRemoved"])
        self.assertNotIn("restoredSourceEvidence", self.report)

    def test_partial_creation_retains_unknown_parent_contents_without_recursion(self):
        shutil.rmtree(self.source)
        unknown = self.source.parent / "unknown.txt"
        unknown.write_text("keep")
        self.report["macosDataRestore"].update(sourceRootCreated=False,
            mediaCreatedDirectories=[str(self.source.parent.parent), str(self.source.parent)])
        with patch.object(runner.base, "remove_owned_tree") as remove:
            with self.assertRaises(OSError):
                runner.preserve_and_remove_sources(self.app, self.report, "unified")
        remove.assert_not_called()
        self.assertEqual("keep", unknown.read_text())
        self.assertNotIn("restoredSourceParentsRemoved", self.report)

    def test_partial_creation_rejects_unrelated_directory_before_removing_any_parent(self):
        shutil.rmtree(self.source)
        unrelated = self.root / "unowned-empty"
        unrelated.mkdir()
        self.report["macosDataRestore"].update(sourceRootCreated=False,
            mediaCreatedDirectories=[str(self.source.parent), str(unrelated)])
        with self.assertRaisesRegex(AssertionError, "ownership"):
            runner.preserve_and_remove_sources(self.app, self.report, "unified")
        self.assertTrue(self.source.parent.is_dir())
        self.assertTrue(unrelated.is_dir())

    def test_live_product_stops_before_any_preservation_or_removal(self):
        with patch.object(runner.base, "native_processes", return_value=[731]), \
             patch.object(runner.base, "remove_owned_tree") as remove:
            with self.assertRaises(AssertionError):
                runner.preserve_and_remove_sources(self.app, self.report, "unified")
        remove.assert_not_called()
        self.assertEqual([], list(self.results.iterdir()))

    def test_outside_source_root_is_rejected_before_removal(self):
        self.report["macosDataRestore"]["sourceRoot"] = str(self.root.parent)
        with patch.object(runner.base, "remove_owned_tree") as remove:
            with self.assertRaisesRegex(AssertionError, "ownership"):
                runner.preserve_and_remove_sources(self.app, self.report, "unified")
        remove.assert_not_called()

    def test_unknown_contents_in_created_parent_are_retained(self):
        (self.source.parent / "unexpected.txt").write_text("keep")
        with self.assertRaises(OSError):
            runner.preserve_and_remove_sources(self.app, self.report, "unified")
        self.assertEqual("keep", (self.source.parent / "unexpected.txt").read_text())
        self.assertNotIn("restoredSourceFilesRemoved", self.report)

    @unittest.skipIf(os.name == "nt", "POSIX symlink fixture; no Windows privilege change")
    def test_symlink_in_source_inventory_prevents_copy_and_removal(self):
        (self.source / "link.txt").symlink_to(self.data / "app.json")
        with patch.object(runner.base, "remove_owned_tree") as remove:
            with self.assertRaisesRegex(AssertionError, "inventory"):
                runner.preserve_and_remove_sources(self.app, self.report, "unified")
        remove.assert_not_called()
        self.assertFalse((self.results / "restored-source-media").exists())


if __name__ == "__main__":
    unittest.main()
