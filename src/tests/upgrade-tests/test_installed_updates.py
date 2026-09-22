#!/usr/bin/env python3
"""Pure updater-package preparation tests: no vpk, native app or installer execution."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
import plistlib
from pathlib import Path
import stat
import subprocess
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


runner = load("installed_updates", "prepare-installed-updates.py")
fixtures = load("installed_update_fixtures", "test_package_acceptance.py")
OLD = "0.0.1-acceptance.35618999687.1"
NEW = "0.0.2-updater.35619999999.1"
SHA = "a" * 40


def provenance():
    return {"passed": True, "packageSourceSHA": SHA, "rid": "win-x64", "version": OLD}


def hashes(value=b"payload"):
    return {"Bakabase.dll": {"kind": "file", "sizeBytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}}


def synthesize(packages, payload, version, role="unified", changes=None):
    assembly = "Bakabase" if role == "unified" else "Bakabase.Client"
    manifest = {"id": assembly, "mainExe": assembly + ".exe", "rid": "win-x64", "version": version, "channel": "win"}
    element = ET.Element("package")
    metadata = ET.SubElement(element, "metadata")
    for key, value in manifest.items():
        ET.SubElement(metadata, key).text = value
    packages.mkdir()
    files = {path.relative_to(payload).as_posix(): path.read_bytes() for path in payload.rglob("*") if path.is_file()}
    files.update(changes or {})
    files["sq.version"] = ET.tostring(element)
    for prefix, suffix in (("current/", "Portable.zip"), ("lib/app/", "full.nupkg")):
        contents = dict(files)
        if suffix == "full.nupkg":
            contents.update({"Squirrel.exe": b"MZgenerated-updater", assembly + "_ExecutionStub.exe": b"MZgenerated-stub"})
        fixtures.write_zip(packages / (assembly + "-" + suffix),
                           [(prefix + name, value, stat.S_IFREG | 0o755) for name, value in contents.items()])
    (packages / (assembly + "-Setup.exe")).write_bytes(b"MZ" + b"installer" * 200)


def synthesize_mac(packages, payload, version, role, info=None, icon=b"original-outer-icon"):
    assembly = "Bakabase" if role == "unified" else "Bakabase.Client"
    title = "Bakabase" if role == "unified" else "Bakabase Client"
    bundle = title + ".app"
    identity = {"id": assembly, "mainExe": assembly, "rid": "osx-arm64", "version": version, "channel": "osx"}
    element = ET.Element("package")
    metadata = ET.SubElement(element, "metadata")
    for key, value in identity.items():
        ET.SubElement(metadata, key).text = value
    info = info or {"CFBundleExecutable": assembly, "CFBundleIdentifier": "com.anobaka.bakabase" + (".client" if role == "client" else ""),
                    "CFBundleName": title, "CFBundleDisplayName": title, "CFBundleVersion": version.split("-")[0],
                    "CFBundleShortVersionString": version.split("-")[0], "CFBundleGetInfoString": title + " " + version,
                    "CFBundleIconFile": "app.icns", "CFBundlePackageType": "APPL"}
    files = {path.relative_to(payload).as_posix(): path.read_bytes() for path in payload.rglob("*") if path.is_file()}
    files["UpdateMac"] = b"vendor-updater-" + version.encode()
    packages.mkdir()
    for prefix, suffix in (("", "Portable.zip"), ("lib/app/", "full.nupkg")):
        contents = prefix + bundle + "/Contents/"
        entries = [(contents + "MacOS/" + name, value, stat.S_IFREG | 0o755) for name, value in files.items()]
        manifest_link = ("sq.version", stat.S_IFLNK | 0o777) if suffix == "Portable.zip" else ("sq.version.__symlink", stat.S_IFREG | 0o644)
        entries += [(contents + "Resources/sq.version", ET.tostring(element), stat.S_IFREG | 0o644),
                    (contents + "MacOS/" + manifest_link[0], "../Resources/sq.version", manifest_link[1]),
                    (contents + "Resources/app.icns", icon, stat.S_IFREG | 0o644),
                    (contents + "Info.plist", plistlib.dumps(info), stat.S_IFREG | 0o644)]
        fixtures.write_zip(packages / (assembly + "-" + suffix), entries)
    (packages / (assembly + "-Setup.pkg")).write_bytes(b"fixture-installer" * 150)


def setup_role(root):
    source = root / "original-payload"
    source.mkdir()
    for name, value in {"Bakabase.exe": b"MZ\x00\x00fixture-native", "Bakabase.dll": b"managed",
                        "Bakabase.deps.json": b'{"libraries":{}}', "coreclr.dll": b"runtime",
                        "web/index.html": b"<html>fixture</html>", "Assets/favicon.ico": b"icon"}.items():
        path = source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
    packages, output = root / "old", root / "output"
    synthesize(packages, source, OLD)
    output.mkdir()
    audit = runner.acceptance.audit_packages(packages, "unified", "win-x64", OLD)
    args = SimpleNamespace(rid="win-x64", old_version=OLD, new_version=NEW, vpk=root / "never-executed-vpk",
                           provenance_data={"roles": {"unified": {"version": OLD, "packageFiles": audit["artifacts"]}}})
    return packages, output, args


class InputGuards(unittest.TestCase):
    def test_old_new_versions_are_deliberate_synthetic_order(self):
        runner.validate_versions(OLD, NEW)
        for old, new in ((OLD, OLD), (NEW, OLD), (OLD, "2.4.0"), (OLD, "0.0.2-updater.1.0"),
                         ("0.0.1-acceptance.1", NEW)):
            with self.subTest(old=old, new=new), self.assertRaises(AssertionError):
                runner.validate_versions(old, new)

    def test_verified_provenance_matches_rid_version_and_source(self):
        self.assertEqual(SHA, runner.provenance_source(provenance(), "win-x64", OLD))
        for changes in ({"passed": False}, {"rid": "osx-arm64"}, {"version": NEW},
                        {"packageSourceSHA": "bad"}, {"headSHA": "b" * 40}):
            with self.subTest(changes=changes), self.assertRaises(AssertionError):
                runner.provenance_source(dict(provenance(), **changes), "win-x64", OLD)

    def test_only_pinned_vpk_stable_version_is_allowed(self):
        for value in ("1.2.0\n", "Velopack 1.2.0+abcd\n",
                      "Description:\n  Velopack CLI 1.2.0, for distributing applications.\n\nUsage:\n  vpk [command] [options]\n"):
            runner.validate_vpk_version(value)
        for value in ("1.2.1", "1.2.0-beta.1", "", "1.2.0\n9.0.100", "11.2.0"):
            with self.subTest(value=value), self.assertRaises(AssertionError):
                runner.validate_vpk_version(value)

    def test_hosted_guard_rejects_local_and_wrong_arch_without_execution(self):
        with patch.object(runner.subprocess, "Popen") as popen:
            for environment, system, machine, rid in (({}, "Darwin", "arm64", "osx-arm64"),
                (fixtures.HOSTED, "Darwin", "arm64", "osx-x64")):
                with self.assertRaises(AssertionError):
                    runner.acceptance.require_hosted_runner(environment, system, machine, rid)
            popen.assert_not_called()

    def test_empty_missing_oversized_package_is_not_a_valid_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "full.nupkg"
            with self.assertRaises(AssertionError): runner.package_info(path)
            path.write_bytes(b"empty")
            with self.assertRaises(AssertionError): runner.package_info(path)
            path.write_bytes(b"x" * 2048)
            result = runner.package_info(path)
            self.assertEqual(hashlib.sha1(path.read_bytes()).hexdigest(), result["sha1"])
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), result["sha256"])
            with patch.object(runner.acceptance, "MAX_ARCHIVE_BYTES", 1024), self.assertRaises(AssertionError):
                runner.package_info(path)


class PayloadGuards(unittest.TestCase):
    def test_product_mismatch_reports_bounded_full_metadata(self):
        original = {f"file-{number}": hashes()["Bakabase.dll"] for number in range(20)}
        changed = {name: hashes(b"changed")["Bakabase.dll"] for name in original}
        with self.assertRaises(runner.PayloadMismatch) as caught:
            runner.compare_product_payloads(original, changed)
        differences = caught.exception.differences
        self.assertEqual(12, len(differences))
        self.assertEqual(hashes()["Bakabase.dll"], differences[0]["before"])
        self.assertEqual(hashes(b"changed")["Bakabase.dll"], differences[0]["after"])

    def test_full_macos_manifest_link_encoding_requires_exact_target_and_no_alias(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            payload = root / "payload"
            payload.mkdir()
            (payload / "Bakabase").write_bytes(b"native")
            packages = root / "packages"
            synthesize_mac(packages, payload, OLD, "unified")
            full = packages / "Bakabase-full.nupkg"
            hashes_read, _, _ = runner.payload_from_full(full, "unified", "osx-arm64", OLD)
            self.assertEqual("symlink", hashes_read["sq.version"]["kind"])
            self.assertEqual("../Resources/sq.version", hashes_read["sq.version"]["target"])
            with zipfile.ZipFile(full) as archive:
                entries = [(entry.filename, archive.read(entry), entry.external_attr >> 16) for entry in archive.infolist()]
            for change in ("wrong-target", "duplicate-before", "duplicate-after"):
                with self.subTest(change=change):
                    modified = list(entries)
                    link = next(entry for entry in entries if entry[0].endswith("sq.version.__symlink"))
                    if change == "wrong-target":
                        modified = [(name, b"../../foreign" if name == link[0] else data, mode) for name, data, mode in modified]
                    else:
                        alias = (link[0].removesuffix(".__symlink"), b"../Resources/sq.version", stat.S_IFLNK | 0o777)
                        modified.insert(0 if change == "duplicate-before" else len(modified), alias)
                    fixture = root / (change + ".nupkg")
                    fixtures.write_zip(fixture, modified)
                    with self.assertRaisesRegex(AssertionError, "symlink target|Duplicate full payload"):
                        runner.payload_from_full(fixture, "unified", "osx-arm64", OLD)

    def test_only_exact_vendor_paths_are_excluded(self):
        original = hashes()
        generated = runner.generated_names("unified", "win-x64")
        updated = {**original, runner.MARKER: hashes()["Bakabase.dll"], "Squirrel.exe": hashes(b"vendor")["Bakabase.dll"]}
        runner.compare_payloads(original, updated, generated)
        for path in ("Bakabase.dll", "web/index.html", "other.exe", "nested/Squirrel.exe", "Bakabase.Client_ExecutionStub.exe"):
            changed = copy.deepcopy(updated)
            changed[path] = hashes(b"changed")["Bakabase.dll"]
            with self.subTest(path=path), self.assertRaises(AssertionError):
                runner.compare_payloads(original, changed, generated)
        with self.assertRaises(AssertionError): runner.compare_payloads(original, original, generated)

    @unittest.skipIf(os.name == "nt", "Symlink fixture needs Unix permissions")
    def test_links_are_hashed_as_links_and_escape_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "payload").mkdir()
            (root / "payload/data").write_text("value")
            (root / "payload/link").symlink_to("data")
            result = runner.tree_hashes(root / "payload")
            self.assertEqual("symlink", result["link"]["kind"])
            self.assertEqual("data", result["link"]["target"])
            (root / "payload/bad").symlink_to("../../outside")
            with self.assertRaisesRegex(AssertionError, "escapes"):
                runner.tree_hashes(root / "payload")

    def test_whole_payload_not_just_main_is_compared_with_full(self):
        with tempfile.TemporaryDirectory() as temporary:
            packages, _, _ = setup_role(Path(temporary))
            full = packages / "Bakabase-full.nupkg"
            payload, manifest, marker = runner.payload_from_full(full, "unified", "win-x64", OLD)
            self.assertIn("web/index.html", payload)
            self.assertIn("coreclr.dll", payload)
            self.assertEqual(OLD, manifest["version"])
            self.assertIsNone(marker)
            with self.assertRaises(AssertionError):
                runner.payload_from_full(full, "client", "win-x64", OLD)


class PreparationFlow(unittest.TestCase):
    @unittest.skipIf(os.name == "nt", "Mac package symlinks require a Unix test host")
    def test_macos_repack_uses_original_outer_plist_icon_and_removes_manifest_link(self):
        for role in ("unified", "client"):
            with self.subTest(role=role), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                payload = root / "base"
                payload.mkdir()
                assembly = "Bakabase" if role == "unified" else "Bakabase.Client"
                for name, value in {assembly: b"\xcf\xfa\xed\xfe-native", assembly + ".dll": b"managed",
                                    assembly + ".deps.json": b'{"libraries":{}}', "libcoreclr.dylib": b"runtime"}.items():
                    (payload / name).write_bytes(value)
                packages, output = root / "old", root / "out"
                synthesize_mac(packages, payload, OLD, role)
                output.mkdir()
                audit = runner.acceptance.audit_packages(packages, role, "osx-arm64", OLD)
                args = SimpleNamespace(rid="osx-arm64", old_version=OLD, new_version=NEW, vpk=root / "unused",
                    provenance_data={"roles": {role: {"version": OLD, "packageFiles": audit["artifacts"]}}})
                def fake_pack(arguments, log, deadline):
                    staged = Path(arguments[arguments.index("--packDir") + 1])
                    self.assertFalse((staged / "sq.version").is_symlink())
                    self.assertFalse((staged / "UpdateMac").exists())
                    info = plistlib.loads(Path(arguments[arguments.index("--plist") + 1]).read_bytes())
                    self.assertEqual(assembly, info["CFBundleExecutable"])
                    self.assertEqual("0.0.2", info["CFBundleVersion"])
                    self.assertEqual(b"original-outer-icon", Path(arguments[arguments.index("--icon") + 1]).read_bytes())
                    synthesize_mac(Path(arguments[arguments.index("--outputDir") + 1]), staged, NEW, role, info)
                    log.write_text("synthetic mac pack")
                with patch.object(runner, "run_command", side_effect=fake_pack), patch.object(runner, "check_budget"), \
                     patch.object(runner.acceptance.contract, "check_publish"), contextlib.redirect_stdout(io.StringIO()):
                    result = runner.prepare_role(role, packages, output, args, SHA, "nonce", time.monotonic() + 10)
                self.assertEqual("symlink", result["newPayloadHashes"]["sq.version"]["kind"])
                self.assertEqual("osx", result["channel"])
                self.assertTrue(result["payloadUnchanged"])

    def test_successful_fake_pack_requires_real_archives_and_identical_product_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            packages, output, args = setup_role(Path(temporary))
            def fake_pack(arguments, log, deadline):
                self.assertIn("--noDefaultExclude", arguments)
                self.assertNotIn("--skipVelopackAppCheck", arguments)
                self.assertEqual("win", arguments[arguments.index("--channel") + 1])
                payload = Path(arguments[arguments.index("--packDir") + 1])
                self.assertFalse((payload / "sq.version").exists())
                synthesize(Path(arguments[arguments.index("--outputDir") + 1]), payload, NEW)
                log.write_text("synthetic pack completed; no process executed")
            with patch.object(runner, "run_command", side_effect=fake_pack), patch.object(runner, "check_budget"), \
                 patch.object(runner.acceptance.contract, "check_publish"), contextlib.redirect_stdout(io.StringIO()):
                result = runner.prepare_role("unified", packages, output, args, SHA, "nonce", time.monotonic() + 10)
            self.assertTrue(result["payloadUnchanged"])
            self.assertEqual(NEW, result["newManifest"]["version"])
            self.assertEqual(SHA, result["marker"]["sourceSHA"])
            self.assertTrue(Path(result["newFullPackage"]).is_file())
            self.assertEqual(runner.MARKER, result["markerPath"])
            self.assertIn("Squirrel.exe", result["vendorGenerated"]["newFull"])
            self.assertFalse((output / "unified/payload").exists())

    def test_success_exit_without_actual_packages_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            packages, output, args = setup_role(Path(temporary))
            with patch.object(runner, "run_command"), patch.object(runner, "check_budget"), \
                 patch.object(runner.acceptance.contract, "check_publish"), contextlib.redirect_stdout(io.StringIO()), \
                 self.assertRaisesRegex(AssertionError, "portable ZIP"):
                runner.prepare_role("unified", packages, output, args, SHA, "nonce", time.monotonic() + 10)

    def test_wrong_verified_input_hash_fails_before_pack(self):
        with tempfile.TemporaryDirectory() as temporary:
            packages, output, args = setup_role(Path(temporary))
            args.provenance_data["roles"]["unified"]["packageFiles"]["full"]["sha256"] = "0" * 64
            with patch.object(runner, "run_command") as command, patch.object(runner, "check_budget"), \
                 self.assertRaisesRegex(AssertionError, "provenance hashes"):
                runner.prepare_role("unified", packages, output, args, SHA, "nonce", time.monotonic() + 10)
            command.assert_not_called()

    def test_corrupt_non_entry_product_file_cannot_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            packages, output, args = setup_role(Path(temporary))
            def fake_pack(arguments, log, deadline):
                synthesize(Path(arguments[arguments.index("--outputDir") + 1]),
                           Path(arguments[arguments.index("--packDir") + 1]), NEW,
                           changes={"web/index.html": b"silently changed UI"})
            with patch.object(runner, "run_command", side_effect=fake_pack), patch.object(runner, "check_budget"), \
                 patch.object(runner.acceptance.contract, "check_publish"), contextlib.redirect_stdout(io.StringIO()), \
                 self.assertRaisesRegex(AssertionError, "Product payload changed"):
                runner.prepare_role("unified", packages, output, args, SHA, "nonce", time.monotonic() + 10)

    def test_failed_pack_writes_failed_report_and_removes_only_temporary_copies(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("unified-input", "client-input"):
                (root / name).mkdir()
            (root / "vpk").write_text("never executed")
            (root / "provenance.json").write_text(json.dumps(provenance()))
            output = root / "prepared"
            argv = ["prepare", "--unified-packages", str(root / "unified-input"),
                    "--client-packages", str(root / "client-input"), "--rid", "win-x64",
                    "--old-version", OLD, "--new-version", NEW, "--vpk", str(root / "vpk"),
                    "--provenance", str(root / "provenance.json"), "--output-directory", str(output)]
            def fail_role(*_):
                (output / "unified/payload").mkdir(parents=True)
                (output / "unified/vpk-pack.log").write_text("failure evidence")
                raise subprocess.TimeoutExpired("vpk", 1)
            def cli_help(arguments, log, deadline):
                self.assertEqual([(root / "vpk").resolve(), "--help"], arguments)
                log.write_text("Description:\n  Velopack CLI 1.2.0, for distributing applications.\n")
            with patch("sys.argv", argv), patch.dict(os.environ, {"RUNNER_TEMP": str(root)}), \
                 patch.object(runner.acceptance, "require_hosted_runner"), patch.object(runner, "check_budget"), \
                 patch.object(runner, "run_command", side_effect=cli_help), \
                 patch.object(runner, "prepare_role", side_effect=fail_role), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(1, runner.main())
            report = json.loads((output / "updates.json").read_text())
            self.assertFalse(report["passed"])
            self.assertEqual("failed", report["status"])
            self.assertEqual("TimeoutExpired", report["error"]["type"])
            self.assertFalse((output / "unified/payload").exists())
            self.assertTrue((output / "unified/vpk-pack.log").is_file())
            self.assertTrue((root / "unified-input").is_dir())

    def test_timeout_is_failure_and_kills_only_its_owned_process(self):
        process = unittest.mock.Mock(pid=321, returncode=None)
        process.wait.side_effect = [subprocess.TimeoutExpired("vpk", 1), 0]
        process.poll.return_value = None
        with tempfile.TemporaryDirectory() as temporary, patch.object(runner.subprocess, "Popen", return_value=process), \
             patch.object(runner.subprocess, "run") as taskkill, patch.object(runner.os, "killpg", create=True) as killpg:
            with self.assertRaises(subprocess.TimeoutExpired):
                runner.run_command(["fake-vpk"], Path(temporary) / "pack.log", time.monotonic() + 1)
            if os.name == "nt":
                self.assertEqual(["taskkill", "/PID", "321", "/T", "/F"], taskkill.call_args.args[0])
                killpg.assert_not_called()
            else:
                killpg.assert_called_once_with(321, runner.signal.SIGKILL)
                taskkill.assert_not_called()


if __name__ == "__main__":
    unittest.main()
