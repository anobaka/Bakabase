#!/usr/bin/env python3
"""Pure package acceptance guards; never launch an application or installer."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error
import xml.etree.ElementTree as ET
import zipfile

spec = importlib.util.spec_from_file_location("package_acceptance", Path(__file__).with_name("run-package-acceptance.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

VERSION = "0.0.1-acceptance.1"
HOSTED = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "/runner-temp"}


def manifest(rid="win-x64"):
    return {"id": "Bakabase", "mainExe": "Bakabase" + (".exe" if rid == "win-x64" else ""),
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


def windows_packages(directory, full_manifest=None, full_changes=None):
    """Actual ZIP containers with tiny synthetic payloads; none is executable."""
    expected = manifest()
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
    def test_each_native_rid_has_exact_manifest_identity(self):
        for rid in ("win-x64", "osx-x64", "osx-arm64"):
            expected = manifest(rid)
            with self.subTest(rid=rid):
                runner.validate_manifest(expected, "unified", rid, VERSION)
            for field, wrong in (("id", "OtherProduct"), ("rid", "linux-x64"),
                                 ("version", "0.0.0"), ("mainExe", "Other.exe")):
                with self.subTest(rid=rid, field=field), self.assertRaises(AssertionError):
                    runner.validate_manifest(dict(expected, **{field: wrong}), "unified", rid, VERSION)

    def test_the_removed_clients_package_is_not_the_app(self):
        # A package the thin client's pipeline once produced must never pass as the app's.
        for rid in ("win-x64", "osx-x64", "osx-arm64"):
            retired = dict(manifest(rid), id="Bakabase.Client",
                           mainExe="Bakabase.Client" + (".exe" if rid == "win-x64" else ""))
            with self.subTest(rid=rid), self.assertRaises(AssertionError):
                runner.validate_manifest(retired, "unified", rid, VERSION)


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
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            windows_packages(root)
            report = runner.audit_packages(root, "unified", "win-x64", VERSION)
            self.assertTrue(report["passed"])
            self.assertEqual(manifest(), report["manifest"])
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


class NativeSetupAcceptance(unittest.TestCase):
    def setup_fixture(self, root):
        data = root / "data"
        runner.prepare_data(data)
        token = "A" * 64
        (data / ".bakabase-server-setup.json").write_text(json.dumps({"token": token}))
        log = root / "portable.log"
        log.write_text("Server setup: http://127.0.0.1:12345/setup#setupToken=" + token + "\n")
        return data, log, token

    def test_new_fixture_is_empty_without_a_fabricated_version_or_sentinel(self):
        with tempfile.TemporaryDirectory() as temporary:
            data = Path(temporary) / "data"
            runner.prepare_data(data)
            self.assertEqual([], list(data.iterdir()))

    def test_setup_endpoint_requires_local_origin_and_matching_owned_capability(self):
        with tempfile.TemporaryDirectory() as temporary:
            data, log, token = self.setup_fixture(Path(temporary))
            self.assertEqual((12345, token), runner.setup_connection(log, data))
            for url in ("http://other.example:12345", "https://127.0.0.1:12345", "http://127.0.0.1:0"):
                log.write_text("Server setup: " + url + "/setup#setupToken=" + token)
                with self.assertRaises(AssertionError):
                    runner.setup_connection(log, data)
            log.write_text("Server setup: http://127.0.0.1:12345/setup#setupToken=" + "B" * 64)
            with self.assertRaisesRegex(AssertionError, "owned data fixture"):
                runner.setup_connection(log, data)

    def test_real_setup_sequence_authenticates_confirms_monitors_and_revokes(self):
        with tempfile.TemporaryDirectory() as temporary:
            data, log, token = self.setup_fixture(Path(temporary))
            monitor = "B" * 64
            submitted, phases, calls = False, iter(("starting", "completed")), []

            def request(port, path, payload=None, headers=None):
                nonlocal submitted
                self.assertEqual(12345, port)
                calls.append((path, payload, headers))
                if path == runner.MONITOR_PATH:
                    self.assertEqual({runner.MONITOR_HEADER: monitor}, headers)
                    return json.dumps({"id": "operation", "phase": next(phases)}).encode()
                if headers != {runner.SETUP_HEADER: token} or submitted:
                    raise urllib.error.HTTPError("http://127.0.0.1/" + path, 401, "Unauthorized", {}, None)
                if path == "/setup/status":
                    return json.dumps({"mode": "first-run", "currentPath": str(data), "isDocker": False,
                                       "canChooseTargetPath": False, "canBrowse": True, "submitted": False}).encode()
                self.assertEqual({"operation": "initialize"}, payload)
                if path == "/setup/validate":
                    return b'{"valid":true}'
                self.assertEqual("/setup/apply", path)
                submitted = True
                return json.dumps({"requiresRestart": False, "monitorToken": monitor,
                                   "progress": {"id": "operation"}}).encode()

            with patch.object(runner, "api", side_effect=request), patch.object(runner.time, "sleep"):
                report = runner.initialize_empty(data, log)
            self.assertTrue(report["passed"])
            self.assertEqual(["starting", "completed"], report["phases"])
            self.assertNotIn(token, json.dumps(report))
            self.assertNotIn(monitor, json.dumps(report))
            self.assertEqual(1, sum(path == "/setup/validate" for path, _, _ in calls))
            self.assertFalse((data / "app.json").exists())

    def test_setup_rejects_configuration_created_before_confirmation(self):
        with tempfile.TemporaryDirectory() as temporary:
            data, log, _ = self.setup_fixture(Path(temporary))
            (data / "app.json").write_text('{}')
            status = {"mode": "first-run", "currentPath": str(data), "isDocker": False,
                      "canChooseTargetPath": False, "canBrowse": True, "submitted": False}
            with patch.object(runner, "api", return_value=json.dumps(status).encode()) as request:
                with self.assertRaisesRegex(AssertionError, "before setup confirmation"):
                    runner.initialize_empty(data, log)
            self.assertEqual(1, request.call_count)

    def test_setup_capabilities_are_removed_from_captured_log_formats(self):
        token = "A" * 64
        text = f'Server setup: http://127.0.0.1:123/setup#setupToken={token}\n' \
               f'Server maintenance: http://127.0.0.1:123/progress#token={token}\n' \
               f'{{"monitorToken":"{token}", "token": "{token}"}}\nX-Bakabase-Setup-Token: {token}'
        sanitized = runner.redact_setup_tokens(text)
        self.assertNotIn(token, sanitized)
        self.assertEqual(5, sanitized.count("[REDACTED]"))

    def test_installer_fixture_copies_initialized_data_without_process_locks(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, target = root / "source", root / "target"
            source.mkdir()
            with self.assertRaisesRegex(AssertionError, "genuinely initialized"):
                runner.copy_initialized_data(source, target)
            (source / "bakabase_insideworld.db").write_bytes(b"owned initialized fixture")
            (source / "app.json").write_text('{"App":{"version":"2.4.0-beta.123"}}')
            for filename in (".bakabase.lock", ".bakabase-setup-process.lock"):
                (source / filename).write_text("owned lock")
            runner.copy_initialized_data(source, target)
            self.assertEqual((source / "app.json").read_bytes(), (target / "app.json").read_bytes())
            self.assertEqual((source / "bakabase_insideworld.db").read_bytes(),
                             (target / "bakabase_insideworld.db").read_bytes())
            self.assertFalse((target / ".bakabase.lock").exists())
            self.assertFalse((target / ".bakabase-setup-process.lock").exists())
            with self.assertRaisesRegex(AssertionError, "new owned directory"):
                runner.copy_initialized_data(source, target)

    def test_existing_library_startup_uses_native_port_memory_without_creating_another_resource(self):
        with tempfile.TemporaryDirectory() as temporary:
            data = Path(temporary)
            (data / "listening-ports.json").write_text('{"preferred":[12344],"lastUsed":[12345]}')
            (data / "app.json").write_text('{"App":{"version":"2.4.0-beta.123"}}')
            paths = []
            def request(port, path, payload=None, headers=None):
                self.assertEqual(12345, port)
                paths.append(path)
                if path == "/app/info":
                    return json.dumps({"code": 0, "data": {"appDataPath": str(data),
                                                          "coreVersion": "2.4.0-beta.123"}}).encode()
                if path == "/":
                    return b"<html><script></script></html>"
                if path == "/app/analytics-info":
                    return b'{"data":{"deviceId":"same-library"}}'
                self.assertEqual("/app/terms", path)
                return b'{}'
            with patch.object(runner, "api", side_effect=request), patch.object(runner, "native_processes", return_value=[42]):
                report = runner.inspect_running(data / "Bakabase", data, create_resource=False)
            self.assertEqual("same-library", report["deviceId"])
            self.assertNotIn("/resource/placeholder", paths)


if __name__ == "__main__":
    unittest.main()
