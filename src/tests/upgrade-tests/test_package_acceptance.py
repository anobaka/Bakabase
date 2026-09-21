#!/usr/bin/env python3
"""Pure package acceptance guards; never launch an application or installer."""
import hashlib
import importlib.util
import os
from pathlib import Path
import stat
import tempfile
from types import SimpleNamespace
import unittest
import xml.etree.ElementTree as ET
import zipfile

spec = importlib.util.spec_from_file_location("package_acceptance", Path(__file__).with_name("run-package-acceptance.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

VERSION = "0.0.1-acceptance.1"
HOSTED = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "/runner-temp"}


def manifest(role="unified", rid="win-x64"):
    assembly = "Bakabase" if role == "unified" else "Bakabase.Client"
    return {"id": assembly, "mainExe": assembly + (".exe" if rid == "win-x64" else ""),
            "rid": rid, "version": VERSION}


def write_zip(path, entries):
    with zipfile.ZipFile(path, "w") as archive:
        for name, content, mode in entries:
            entry = zipfile.ZipInfo(name)
            # Keep deliberately malformed names unchanged on Windows, where
            # ZipInfo's constructor otherwise normalizes backslashes away.
            entry.filename = name
            entry.create_system = 3
            entry.external_attr = mode << 16
            archive.writestr(entry, content)
    return path


def windows_packages(directory, role="unified", full_manifest=None, full_changes=None):
    """Actual ZIP containers with tiny synthetic payloads; none is executable."""
    expected = manifest(role)
    assembly = expected["id"]
    payload = {expected["mainExe"]: b"MZ\x00\x00synthetic-native-header",
               assembly + ".dll": b"synthetic-managed-assembly",
               assembly + ".deps.json": b'{"libraries":{}}', "coreclr.dll": b"synthetic-runtime"}
    for kind, prefix, filename in (("portable", "current/", assembly + "-Portable.zip"),
                                   ("full", "lib/net45/", assembly + "-full.nupkg")):
        identity = full_manifest if kind == "full" and full_manifest is not None else expected
        xml = ET.Element("package")
        metadata = ET.SubElement(xml, "metadata")
        for key, value in identity.items():
            ET.SubElement(metadata, key).text = value
        files = dict(payload)
        if kind == "full":
            for name, content in (full_changes or {}).items():
                if content is None:
                    files.pop(name)
                else:
                    files[name] = content
        files["sq.version"] = ET.tostring(xml)
        write_zip(directory / filename, [(prefix + name, content, stat.S_IFREG | 0o755)
                                         for name, content in files.items()])
    (directory / (assembly + "-Setup.exe")).write_bytes(b"MZ" + b"fixture" * 200)


class HostedRunnerBoundary(unittest.TestCase):
    def test_native_hosted_combinations_are_allowed(self):
        for system, machine, rid in (("Windows", "AMD64", "win-x64"),
                                     ("Windows", "x86_64", "win-x64"),
                                     ("Darwin", "x86_64", "osx-x64"),
                                     ("Darwin", "arm64", "osx-arm64")):
            with self.subTest(system=system, machine=machine, rid=rid):
                runner.require_hosted_runner(HOSTED, system, machine, rid)

    def test_local_self_hosted_and_incomplete_environment_are_rejected(self):
        environments = [{}, dict(HOSTED, GITHUB_ACTIONS="false"),
                        dict(HOSTED, RUNNER_ENVIRONMENT="self-hosted"), dict(HOSTED, RUNNER_TEMP="")]
        environments.extend({key: value for key, value in HOSTED.items() if key != missing} for missing in HOSTED)
        for environment in environments:
            with self.subTest(environment=environment), self.assertRaises(AssertionError):
                runner.require_hosted_runner(environment, "Darwin", "arm64", "osx-arm64")

    def test_foreign_architectures_and_platforms_are_rejected(self):
        for system, machine, rid in (("Darwin", "arm64", "osx-x64"),
                                     ("Darwin", "x86_64", "osx-arm64"),
                                     ("Windows", "AMD64", "osx-x64"),
                                     ("Windows", "ARM64", "win-x64"),
                                     ("Linux", "x86_64", "win-x64")):
            with self.subTest(system=system, machine=machine, rid=rid), self.assertRaises(AssertionError):
                runner.require_hosted_runner(HOSTED, system, machine, rid)


class ManifestBoundary(unittest.TestCase):
    def test_each_product_and_native_rid_have_exact_manifest_identity(self):
        for role in ("unified", "client"):
            for rid in ("win-x64", "osx-x64", "osx-arm64"):
                expected = manifest(role, rid)
                with self.subTest(role=role, rid=rid):
                    runner.validate_manifest(expected, role, rid, VERSION)
                for field, wrong in (("id", "OtherProduct"), ("rid", "linux-x64"),
                                     ("version", "0.0.0"), ("mainExe", "Other.exe")):
                    with self.subTest(role=role, rid=rid, field=field), self.assertRaises(AssertionError):
                        runner.validate_manifest(dict(expected, **{field: wrong}), role, rid, VERSION)
                other_role = "client" if role == "unified" else "unified"
                with self.subTest(role=role, rid=rid, wrong_role=True), self.assertRaises(AssertionError):
                    runner.validate_manifest(expected, other_role, rid, VERSION)


class ArchiveBoundary(unittest.TestCase):
    def test_original_archive_spelling_is_checked_after_windows_normalization(self):
        entry = zipfile.ZipInfo("current/escape")
        entry.orig_filename = "current\\escape"
        entry.file_size = 1
        with self.assertRaisesRegex(AssertionError, "Unsafe archive path"):
            runner.archive_entries(SimpleNamespace(infolist=lambda: [entry]))

    def test_traversal_absolute_drive_and_backslash_paths_are_rejected(self):
        for name in ("../escape", "current/../../escape", "/absolute", "C:/escape",
                     "C:escape", "current\\escape", "current/../escape"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                archive = write_zip(root / "bad.zip", [(name, b"bad", stat.S_IFREG | 0o644)])
                with self.assertRaises(AssertionError):
                    runner.unpack(archive, root / "installed")
                self.assertFalse((root / "escape").exists())

    def test_external_absolute_and_windows_symlink_targets_are_rejected(self):
        for link in ("../../escape", "/absolute", "C:/escape", "C:escape", "..\\escape"):
            with self.subTest(link=link), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                archive = write_zip(root / "bad.zip", [("bundle/link", link, stat.S_IFLNK | 0o777)])
                with self.assertRaises(AssertionError):
                    runner.unpack(archive, root / "installed")
                self.assertFalse((root / "escape").exists())

    @unittest.skipIf(os.name == "nt", "Native macOS relative symlinks and Unix permissions require a Unix host")
    def test_internal_relative_manifest_link_and_executable_mode_survive(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = write_zip(root / "portable.zip", [
                ("Bakabase.app/Contents/Resources/sq.version", "manifest", stat.S_IFREG | 0o644),
                ("Bakabase.app/Contents/MacOS/sq.version", "../Resources/sq.version", stat.S_IFLNK | 0o777),
                ("Bakabase.app/Contents/MacOS/Bakabase", "fixture", stat.S_IFREG | 0o755),
                ("__MACOSX/._metadata", "ignored", stat.S_IFREG | 0o644)])
            destination = root / "installed"
            runner.unpack(archive, destination)
            link = destination / "Bakabase.app/Contents/MacOS/sq.version"
            self.assertTrue(link.is_symlink())
            self.assertEqual("../Resources/sq.version", os.readlink(link))
            self.assertEqual("manifest", link.read_text())
            self.assertEqual(0o755, stat.S_IMODE((link.parent / "Bakabase").stat().st_mode))
            self.assertFalse((destination / "__MACOSX").exists())


class PackageAuditBoundary(unittest.TestCase):
    def test_matching_self_contained_packages_report_content_hashes(self):
        for role in ("unified", "client"):
            with self.subTest(role=role), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                windows_packages(root, role)
                report = runner.audit_packages(root, role, "win-x64", VERSION)
                self.assertTrue(report["passed"])
                self.assertEqual(manifest(role), report["manifest"])
                self.assertEqual("current/", report["portableContent"])
                self.assertEqual({"portable", "full", "installer"}, set(report["artifacts"]))
                for artifact in report["artifacts"].values():
                    content = (root / artifact["file"]).read_bytes()
                    self.assertEqual(len(content), artifact["sizeBytes"])
                    self.assertEqual(hashlib.sha256(content).hexdigest(), artifact["sha256"])

    def test_missing_or_truncated_installer_is_rejected(self):
        for content in (None, b"MZ"):
            with self.subTest(content=content), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                windows_packages(root)
                installer = root / "Bakabase-Setup.exe"
                if content is None:
                    installer.unlink()
                else:
                    installer.write_bytes(content)
                with self.assertRaises(AssertionError):
                    runner.audit_packages(root, "unified", "win-x64", VERSION)

    def test_wrong_full_package_manifest_is_rejected(self):
        for field, wrong in (("id", "Bakabase.Client"), ("rid", "osx-x64"),
                             ("version", "0.0.0"), ("mainExe", "Bakabase.Client.exe")):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                windows_packages(root, full_manifest=dict(manifest(), **{field: wrong}))
                with self.assertRaisesRegex(AssertionError, "Package " + field + " differs"):
                    runner.audit_packages(root, "unified", "win-x64", VERSION)

    def test_portable_and_full_binary_mismatch_is_rejected(self):
        for name, content in (("Bakabase.exe", b"MZdifferent-native-payload"),
                              ("Bakabase.dll", b"different-managed-payload"),
                              ("Bakabase.deps.json", b'{"libraries":{"Other":"1"}}')):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                windows_packages(root, full_changes={name: content})
                with self.assertRaisesRegex(AssertionError, "Portable and update package binaries differ"):
                    runner.audit_packages(root, "unified", "win-x64", VERSION)

    def test_missing_runtime_and_non_native_main_are_rejected(self):
        for changes, message in (({"coreclr.dll": None}, "Missing self-contained payload"),
                                 ({"Bakabase.exe": b"not-a-native-binary"}, "not a native binary")):
            with self.subTest(changes=changes), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                windows_packages(root, full_changes=changes)
                with self.assertRaisesRegex(AssertionError, message):
                    runner.audit_packages(root, "unified", "win-x64", VERSION)


if __name__ == "__main__":
    unittest.main()
