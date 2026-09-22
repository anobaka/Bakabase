#!/usr/bin/env python3
"""Pure historical-upgrade guards; no assets, products, installers or accounts run."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import plistlib
import sqlite3
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import urllib.error
import urllib.request


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release = load("historical-release")
prepare = load("historical-prepare")
runner = load("historical-installed")
feed = load("historical-feed")
fixtures = load("test_installed_updates")
TARGET = "2.4.1-historical.12345.1"


def provenance(rid="win-x64"):
    return {"passed": True, "rid": rid, "version": release.CANDIDATE_VERSION,
            "packageSourceSHA": release.CANDIDATE_SHA, "repository": release.REPOSITORY,
            "runID": release.CANDIDATE_RUN, "unchangedProductSources": True}


def metadata():
    return {"id": release.RELEASE_ID, "tag_name": release.TAG, "target_commitish": release.RELEASE_SHA,
            "draft": False, "prerelease": True, "assets": [
                {"id": item["id"], "name": item["file"], "size": item["sizeBytes"],
                 "digest": "sha256:" + item["sha256"], "browser_download_url": item["url"], "state": "uploaded"}
                for item in (release.pin(*key) for key in release.PINS)]}


def version_manifest(role, version, rid="win-x64", channel="win"):
    assembly = release.base.contract.PRODUCTS[role]["assembly"]
    return {"id": assembly, "mainExe": assembly + (".exe" if rid == "win-x64" else ""),
            "version": version, "rid": rid, "channel": channel}


def content_hash(value):
    return {"kind": "file", "sha256": hashlib.sha256(value).hexdigest(), "sizeBytes": len(value)}


def manifest(root):
    result = {"format": "bakabase-historical-upgrade-v1", "passed": True, "rid": "win-x64",
              "oldVersion": release.OLD_VERSION, "candidateVersion": release.CANDIDATE_VERSION,
              "newVersion": TARGET, "sourceSHA": release.CANDIDATE_SHA, "provenance": provenance(),
              "historicalRelease": {"id": release.RELEASE_ID, "tag": release.TAG}, "roles": {}}
    for role in release.ROLES:
        assembly = release.base.contract.PRODUCTS[role]["assembly"]
        path = root / f"{assembly}-{TARGET}-full.nupkg"
        path.write_bytes((role + "-verified-candidate").encode() * 100)
        payload = {assembly + ".dll": content_hash(b"candidate")}
        old_payload = {assembly + ".dll": content_hash(b"original-published-code")}
        marker = {"format": "bakabase-installed-updater-acceptance", "version": 1, "role": role,
                  "oldVersion": release.OLD_VERSION, "newVersion": TARGET,
                  "sourceSHA": release.CANDIDATE_SHA, "nonce": "a" * 32}
        result["roles"][role] = {"role": role, "channel": "win", "candidatePayloadUnchanged": True,
            "expectedRunningVersion": release.CANDIDATE_CORE, "newFullPackage": str(path),
            "oldManifest": version_manifest(role, release.OLD_VERSION, channel="beta"),
            "newManifest": version_manifest(role, TARGET), "marker": marker,
            "candidateSource": {"manifest": version_manifest(role, release.CANDIDATE_VERSION), "payloadHashes": payload},
            "oldPayloadHashes": old_payload, "newPayloadHashes": dict(payload, **{release.repack.MARKER: content_hash(json.dumps(marker).encode())}),
            "newFullPayloadHashes": payload, "vendorGenerated": {"allowedNames": sorted(release.repack.generated_names(role, "win-x64"))},
            "packageChecksums": {"newFull": release.repack.package_info(path)}}
    return result


class ReleaseGuards(unittest.TestCase):
    def test_all_pinned_assets_are_distinct_and_every_native_platform_has_both_original_installers(self):
        value = metadata()
        ids, hashes = set(), set()
        for rid in release.RIDS:
            selected = release.validate_release(value, rid)
            self.assertEqual(set(release.ROLES), set(selected))
            for role, assets in selected.items():
                self.assertEqual({"installer", "portable"} if rid == "win-x64" else {"installer"}, set(assets))
                for asset in assets.values():
                    self.assertRegex(asset["sha256"], r"^[a-f0-9]{64}$")
                    ids.add(asset["id"])
                    hashes.add(asset["sha256"])
                    self.assertIn(release.TAG, asset["url"])
        self.assertEqual(8, len(ids))
        self.assertEqual(8, len(hashes))

    def test_release_and_asset_replacements_or_ambiguous_names_fail_closed(self):
        for key, value in (("id", 1), ("tag_name", "v2.4.0-beta.348"), ("target_commitish", "0" * 40),
                           ("draft", True), ("prerelease", False)):
            with self.subTest(key=key), self.assertRaises(AssertionError):
                release.validate_release(dict(metadata(), **{key: value}), "win-x64")
        target = release.pin("unified", "win-x64", "installer")["file"]
        for field, replacement in (("id", 1), ("size", 1), ("digest", "sha256:" + "0" * 64),
                                   ("browser_download_url", "https://example.org/other.exe"), ("state", "new")):
            value = metadata()
            asset = next(item for item in value["assets"] if item["name"] == target)
            asset[field] = replacement
            with self.subTest(field=field), self.assertRaises(AssertionError):
                release.validate_release(value, "win-x64")
        for duplicate in (False, True):
            value = metadata()
            asset = next(item for item in value["assets"] if item["name"] == target)
            value["assets"].append(dict(asset)) if duplicate else value["assets"].remove(asset)
            with self.assertRaises(AssertionError):
                release.validate_release(value, "win-x64")

    def test_actual_asset_size_and_digest_are_checked_not_only_metadata(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "original-Setup.exe"
            path.write_bytes(b"published-installer")
            expected = {"file": path.name, "sizeBytes": path.stat().st_size, "sha256": release.base.sha256(path)}
            self.assertEqual(expected, release.verify_file(path, expected))
            path.write_bytes(b"different-installer")
            with self.assertRaises(AssertionError): release.verify_file(path, expected)
            with self.assertRaises(AssertionError): release.verify_file(path, dict(expected, file="other.exe"))
            path.unlink()
            with self.assertRaises(AssertionError): release.verify_file(path, expected)

    def test_target_patch_is_strictly_above_real_release_and_candidate_versions(self):
        release.validate_versions(release.CANDIDATE_VERSION, TARGET)
        for target in (release.OLD_VERSION, release.CANDIDATE_CORE, "0.0.2-updater.123.1", "2.4.1-historical.1.0",
                       "2.4.1-historical.0.1", "2.4.1", "9.0.0-historical.1.1"):
            with self.subTest(target=target), self.assertRaises(AssertionError):
                release.validate_versions(release.CANDIDATE_VERSION, target)
        with self.assertRaises(AssertionError): release.validate_versions("0.0.1-acceptance.1.1", TARGET)

    def test_candidate_provenance_requires_exact_run_source_repository_and_unchanged_product(self):
        self.assertEqual(release.CANDIDATE_SHA, release.candidate_provenance(provenance(), "win-x64", release.CANDIDATE_VERSION))
        for key, value in (("packageSourceSHA", "a" * 40), ("runID", 1), ("repository", "other/Bakabase"),
                           ("unchangedProductSources", False), ("passed", False), ("rid", "osx-x64"), ("version", release.OLD_VERSION)):
            with self.subTest(key=key), self.assertRaises(AssertionError):
                release.candidate_provenance(dict(provenance(), **{key: value}), "win-x64", release.CANDIDATE_VERSION)

    def test_prepare_and_installed_entrypoints_reject_local_before_io_or_native_execution(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(release.subprocess, "Popen") as popen, \
             patch.object(release.subprocess, "run") as run, patch.object(release.subprocess, "check_output") as check:
            with self.assertRaises(AssertionError):
                prepare.main(["--unified-packages", "unused", "--client-packages", "unused2", "--rid", "win-x64",
                              "--candidate-version", release.CANDIDATE_VERSION, "--new-version", TARGET, "--provenance", "unused",
                              "--vpk", "unused", "--output-directory", "must-not-create"])
            with self.assertRaises(AssertionError):
                runner.main(["--manifest", "unused", "--rid", "win-x64", "--results-directory", "must-not-create"])
            popen.assert_not_called()
            run.assert_not_called()
            check.assert_not_called()

    def test_native_download_and_extraction_helpers_also_reject_local(self):
        with patch.dict(os.environ, {}, clear=True), tempfile.TemporaryDirectory() as temporary, \
             patch.object(release.subprocess, "Popen") as popen, patch.object(release.repack, "run_command") as command:
            root = Path(temporary)
            with self.assertRaises(AssertionError):
                release.download(release.pin("unified", "win-x64", "installer"), root / "unused", "win-x64", time.monotonic() + 5)
            with self.assertRaises(AssertionError):
                release.audit_released(root, "unified", "win-x64", root, time.monotonic() + 5)
            self.assertEqual([], list(root.iterdir()))
            popen.assert_not_called()
            command.assert_not_called()


class PreparationGuards(unittest.TestCase):
    def test_original_pkg_payload_is_read_without_current_federation_requirement_or_installing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            installer = release.pin("unified", "osx-arm64", "installer")
            (root / installer["file"]).write_bytes(b"pure-pkg-fixture")
            def fake_expand(arguments, log, deadline):
                self.assertEqual(["pkgutil", "--expand-full"], arguments[:2])
                expanded = arguments[3]
                content = expanded / "original.pkg/Payload/Bakabase.app/Contents/MacOS"
                content.mkdir(parents=True)
                (expanded / "original.pkg/PackageInfo").write_text('<pkg-info identifier="com.anobaka.bakabase"/>')
                (content.parent / "Info.plist").write_bytes(plistlib.dumps({"CFBundleIdentifier": "com.anobaka.bakabase",
                    "CFBundleExecutable": "Bakabase", "CFBundlePackageType": "APPL"}))
                for name, data in {"Bakabase": b"\xcf\xfa\xed\xfe-native", "Bakabase.dll": b"original",
                    "Bakabase.deps.json": b'{"libraries":{}}', "Bakabase.Shell.dll": b"shell",
                    "Bakabase.Service.dll": b"service", "libcoreclr.dylib": b"runtime"}.items():
                    (content / name).write_bytes(data)
                (content / "web").mkdir()
                (content / "web/index.html").write_text("<html></html>")
                (content / "web/main.js").write_text("original JavaScript")
                tree = ET.Element("package")
                metadata_node = ET.SubElement(tree, "metadata")
                for key, value in version_manifest("unified", release.OLD_VERSION, "osx-arm64", "beta").items():
                    ET.SubElement(metadata_node, key).text = value
                (content / "sq.version").write_bytes(ET.tostring(tree))
                log.write_text("pure pkg expansion fixture")
            with patch.object(release, "hosted"), patch.object(release, "verify_file", return_value=installer), \
                 patch.object(release.repack, "run_command", side_effect=fake_expand) as command, \
                 patch.object(release.base.contract, "check_publish", side_effect=AssertionError("Current federation contract must not audit old code")):
                audit = release.audit_released(root, "unified", "osx-arm64", root, time.monotonic() + 5)
            self.assertEqual("original-pkg-expanded", audit["payloadSource"])
            self.assertIn("web/main.js", audit["payloadHashes"])
            self.assertNotIn("Bakabase.Modules.Federation.dll", audit["payloadHashes"])
            self.assertFalse((root / "unified-historical-expanded").exists())
            command.assert_called_once()

    def test_real_archives_repack_keeps_candidate_bytes_but_marks_actual_historical_installation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            packages, output, args = fixtures.setup_role(root)
            args.original_installed_version = release.OLD_VERSION
            args.new_version = TARGET
            def fake_pack(arguments, log, deadline):
                payload = Path(arguments[arguments.index("--packDir") + 1])
                fixtures.synthesize(Path(arguments[arguments.index("--outputDir") + 1]), payload, TARGET)
                log.write_text("pure synthetic pack")
            with patch.object(prepare.repack, "run_command", side_effect=fake_pack), patch.object(prepare.repack, "check_budget"), \
                 patch.object(prepare.repack.acceptance.contract, "check_publish"), contextlib.redirect_stdout(io.StringIO()):
                prepared = prepare.repack.prepare_role("unified", packages, output, args, release.CANDIDATE_SHA, "a" * 32, time.monotonic() + 10)
            self.assertEqual(release.OLD_VERSION, prepared["marker"]["oldVersion"])
            self.assertEqual(fixtures.OLD, prepared["oldManifest"]["version"])
            original = {"bundleName": None, "manifest": version_manifest("unified", release.OLD_VERSION, channel="beta"),
                        "payloadHashes": {"Bakabase.dll": content_hash(b"real-original-code")}}
            result = prepare.combine(prepared, original, root / "original-packages")
            self.assertNotIn("oldFullPackage", result)
            self.assertNotIn("oldFull", result["packageChecksums"])
            self.assertNotIn("payloadUnchanged", result)
            self.assertEqual(original["payloadHashes"], result["oldPayloadHashes"])
            self.assertEqual(prepared["oldPayloadHashes"], result["candidateSource"]["payloadHashes"])
            self.assertTrue(result["candidatePayloadUnchanged"])

    def test_default_repack_marker_still_identifies_the_original_acceptance_package(self):
        with tempfile.TemporaryDirectory() as temporary:
            packages, output, args = fixtures.setup_role(Path(temporary))
            def fake_pack(arguments, log, deadline):
                fixtures.synthesize(Path(arguments[arguments.index("--outputDir") + 1]),
                                    Path(arguments[arguments.index("--packDir") + 1]), args.new_version)
                log.write_text("pure synthetic pack")
            with patch.object(prepare.repack, "run_command", side_effect=fake_pack), patch.object(prepare.repack, "check_budget"), \
                 patch.object(prepare.repack.acceptance.contract, "check_publish"), contextlib.redirect_stdout(io.StringIO()):
                value = prepare.repack.prepare_role("unified", packages, output, args, release.CANDIDATE_SHA, "a" * 32, time.monotonic() + 10)
            self.assertEqual(args.old_version, value["marker"]["oldVersion"])

    def test_preparation_refuses_changed_candidate_payload_wrong_role_or_fake_historical_full(self):
        with tempfile.TemporaryDirectory() as temporary:
            good = manifest(Path(temporary))
            release.validate_preparation(good, "win-x64")
            mutations = [lambda v: v.update(sourceSHA="a" * 40), lambda v: v.update(newVersion=release.CANDIDATE_CORE),
                lambda v: v["roles"]["unified"].update(oldFullPackage="candidate-repack-full.nupkg"),
                lambda v: v["roles"]["unified"]["packageChecksums"].update(oldFull={}),
                lambda v: v["roles"]["unified"].update(role="client"),
                lambda v: v["roles"]["unified"].update(expectedRunningVersion=release.OLD_VERSION),
                lambda v: v["roles"]["unified"]["newPayloadHashes"].update({"Bakabase.dll": content_hash(b"changed")}),
                lambda v: v["roles"]["unified"]["newFullPayloadHashes"].update({"web/new.js": content_hash(b"extra")}),
                lambda v: v["roles"]["unified"]["marker"].update(oldVersion=release.CANDIDATE_VERSION),
                lambda v: v["roles"]["unified"]["vendorGenerated"]["allowedNames"].append("Bakabase.dll"),
                lambda v: v["roles"].pop("client")]
            for mutation in mutations:
                value = copy.deepcopy(good)
                mutation(value)
                with self.assertRaises(AssertionError): release.validate_preparation(value, "win-x64")


class FeedGuards(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.manifest = manifest(self.root)
        self.path = self.root / "historical.json"
        self.path.write_text(json.dumps(self.manifest))
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def start(self):
        server = feed.Feed(self.path, "win-x64", release.OLD_VERSION)
        self.addCleanup(server.close)
        return server

    def get(self, url):
        with self.opener.open(url, timeout=3) as response:
            return response.read()

    def test_no_fabricated_old_package_and_independent_candidate_activation(self):
        server = self.start()
        unified = server.environment["BAKABASE_UPDATE_URL"] + "/win-x64/"
        client = server.environment["BAKABASE_CLIENT_UPDATE_URL"] + "/win-x64/"
        self.assertEqual({"Assets": []}, json.loads(self.get(unified + "releases.win.json")))
        name = Path(self.manifest["roles"]["unified"]["newFullPackage"]).name
        with self.assertRaises(urllib.error.HTTPError): self.get(unified + name)
        server.activate("unified")
        assets = json.loads(self.get(unified + "releases.win.json?cache-buster=1"))["Assets"]
        self.assertEqual(TARGET, assets[0]["Version"])
        self.assertEqual({"Assets": []}, json.loads(self.get(client + "releases.win.json")))
        data = self.get(unified + name)
        self.assertEqual(hashlib.sha256(data).hexdigest().upper(), assets[0]["SHA256"])
        end = time.monotonic() + 2
        while not server.deliveries[-1]["completed"] and time.monotonic() < end:
            time.sleep(0.01)
        self.assertEqual({"role": "unified", "kind": "new", "file": name, "bytes": len(data), "completed": True}, server.deliveries[-1])
        server.deactivate("unified")
        self.assertEqual({"Assets": []}, json.loads(self.get(unified + "releases.win.json")))

    def test_other_product_paths_wrong_rid_and_expired_budget_are_not_served(self):
        server = self.start()
        base = server.environment["BAKABASE_UPDATE_URL"]
        server.activate("unified")
        other = Path(self.manifest["roles"]["client"]["newFullPackage"]).name
        for path in ("/win-x64/" + other, "/osx-x64/releases.osx.json", "/win-x64/../../historical.json"):
            with self.subTest(path=path), self.assertRaises(urllib.error.HTTPError): self.get(base + path)
        server._expires = time.monotonic() - 1
        with self.assertRaises(urllib.error.HTTPError): self.get(base + "/win-x64/releases.win.json")

    def test_changed_new_package_is_refused_before_listening(self):
        Path(self.manifest["roles"]["client"]["newFullPackage"]).write_bytes(b"tampered")
        with patch.object(feed.http.server, "ThreadingHTTPServer") as server, self.assertRaises(ValueError): self.start()
        server.assert_not_called()


class RetentionAndCoreGuards(unittest.TestCase):
    def test_historical_flow_seeds_original_code_before_native_updates_and_never_reinstalls_old_code(self):
        apps = {role: {"role": role, "port": 41001 if role == "client" else 41002} for role in release.ROLES}
        report, order = {}, []
        prepared = SimpleNamespace(manifest={"roles": {role: {"channel": "win"} for role in release.ROLES}})
        original = {"database": {"resourceIds": [7]}, "fields": {"Id": 7, "Title": runner.RESOURCE_TITLE}}
        def install(app, label, audit=None):
            self.assertTrue(callable(audit))
            order.append("install-" + app["role"])
            return {"passed": True}
        def api(port, path, payload):
            if path == "/resource/placeholder":
                self.assertEqual(runner.RESOURCE_TITLE, payload["items"][0]["title"])
                order.append("create-in-original")
                return json.dumps({"code": 0, "data": [{"resourceId": 7, "created": True, "name": runner.RESOURCE_TITLE}]}).encode()
            self.assertEqual("/app/terms", path)
            return b'{"code":0}'
        def update(_apps, report, _feed, _lifecycle):
            self.assertEqual(["install-client", "install-unified", "create-in-original"], order)
            self.assertIn("originalConfiguration", report)
            self.assertIn("originalResource", report)
            order.append("real-updates-hook")
            report["automaticUpdates"] = {"passed": True}
        with contextlib.ExitStack() as stack:
            for module, name, value in ((runner.lifecycle, "install_app", install),
                (runner.lifecycle, "observe_app", lambda app: {"appInfo": {"version": release.OLD_VERSION, "coreVersion": release.OLD_VERSION}, "processIds": [1]}),
                (runner.lifecycle, "client_state", lambda _: {"connection.json": "hash"}),
                (runner.lifecycle, "verify_coexistence", lambda _a, label, *_: {"label": label, "passed": True}),
                (runner.base, "api", api), (runner, "config_state", lambda app: {"retained": app["role"]}),
                (runner, "resource_state", lambda *_: original), (runner.updates, "run", update),
                (runner.updates, "api", lambda *_: {"code": 0, "data": {"installedVersion": release.OLD_VERSION,
                    "runningVersion": release.OLD_VERSION, "channel": "win", "updateCheckUnavailable": False}})):
                stack.enter_context(patch.object(module, name, side_effect=value))
            for name in ("start_app", "remove_app", "stop_app"):
                stack.enter_context(patch.object(runner.lifecycle, name, side_effect=AssertionError("No manual replacement or old-code reinstall")))
            runner.exercise(apps, report, prepared)
        self.assertTrue(report["historicalMigrationPassed"])
        self.assertTrue(report["resourceRetention"]["passed"])
        self.assertEqual(runner.SCOPE, report["automaticUpdates"]["scope"])
        self.assertNotIn("passed", report, "Overall pass belongs to successful shared cleanup in main")

    def test_cleanup_failure_cannot_turn_successful_migration_into_overall_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            value = manifest(root)
            for role in release.ROLES:
                value["roles"][role]["historicalPackages"] = str(root / role)
            path = root / "historical.json"
            path.write_text(json.dumps(value))
            results = root / "results"
            def fail_cleanup(_args, _results, report, **hooks):
                self.assertEqual(runner.exercise, hooks["exercise"])
                self.assertEqual(runner.feed_module.Feed, hooks["feed_factory"])
                report["historicalMigrationPassed"] = True
                report["cleanupErrors"] = ["owned process remains"]
                raise AssertionError("Installed lifecycle cleanup failed")
            with patch.object(runner.release, "hosted"), patch.dict(os.environ, {"RUNNER_TEMP": temporary}), \
                 patch.object(runner.subprocess, "check_output", return_value="a" * 40), \
                 patch.object(runner.lifecycle, "execute", side_effect=fail_cleanup), contextlib.redirect_stdout(io.StringIO()):
                result = runner.main(["--manifest", str(path), "--rid", "win-x64", "--results-directory", str(results)])
            self.assertEqual(1, result)
            report = json.loads((results / "report.json").read_text())
            self.assertFalse(report["passed"])
            self.assertTrue(report["historicalMigrationPassed"])
            self.assertEqual(["owned process remains"], report["cleanupErrors"])

    def test_same_code_default_and_historical_changed_core_are_both_strict(self):
        check = runner.updates.validate_running_core
        self.assertEqual("original", check({}, "original", "original", "original"))
        prepared = {"expectedRunningVersion": release.CANDIDATE_CORE}
        self.assertEqual(release.CANDIDATE_CORE, check(prepared, release.OLD_VERSION, release.CANDIDATE_CORE, release.CANDIDATE_CORE))
        for configured, actual, persisted in (({}, "changed", "changed"), (prepared, release.OLD_VERSION, release.OLD_VERSION),
            (prepared, TARGET, TARGET), (prepared, release.CANDIDATE_CORE, TARGET), ({"expectedRunningVersion": None}, None, None)):
            with self.subTest(configured=configured, actual=actual), self.assertRaises(AssertionError):
                check(configured, release.OLD_VERSION, actual, persisted)

    def test_resource_id_content_and_integrity_survive_added_schema_columns(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "bakabase_insideworld.db"
            with sqlite3.connect(path) as db:
                db.execute("CREATE TABLE ResourcesV2 (Id INTEGER PRIMARY KEY, Title TEXT, Payload BLOB)")
                db.execute("INSERT INTO ResourcesV2 VALUES (7, ?, ?)", (runner.RESOURCE_TITLE, b"fixture"))
            before = runner.resource_state({"data": root}, 7)
            with sqlite3.connect(path) as db:
                db.execute("ALTER TABLE ResourcesV2 ADD COLUMN NewField INTEGER DEFAULT 0")
            after = runner.resource_state({"data": root}, 7)
            self.assertEqual(["NewField"], runner.verify_resource_retained(before, after)["addedColumns"])
            with sqlite3.connect(path) as db:
                db.execute("UPDATE ResourcesV2 SET Title = 'lost original title'")
            with self.assertRaises(AssertionError): runner.verify_resource_retained(before, runner.resource_state({"data": root}, 7))
            with sqlite3.connect(path) as db:
                db.execute("DELETE FROM ResourcesV2")
            with self.assertRaises(AssertionError): runner.resource_state({"data": root}, 7)

    def test_configuration_retention_ignores_version_migration_but_not_user_settings(self):
        with tempfile.TemporaryDirectory() as temporary:
            app = {"data": Path(temporary), "port": 41000}
            options = {"language": "en-US", "enableAnonymousDataTracking": False, "listeningPorts": [41000],
                       "autoListeningPortCount": 0, "maxParallelism": 1, "enablePreReleaseChannel": False, "version": release.OLD_VERSION}
            path = app["data"] / "app.json"
            path.write_text(json.dumps({"App": options}))
            (app["data"] / "acceptance-sentinel.txt").write_text("installer acceptance owned external AppData\n")
            before = runner.config_state(app)
            options["version"] = release.CANDIDATE_CORE
            path.write_text(json.dumps({"App": options}))
            self.assertEqual(before, runner.config_state(app))
            options["maxParallelism"] = 4
            path.write_text(json.dumps({"App": options}))
            with self.assertRaises(AssertionError): runner.config_state(app)


if __name__ == "__main__":
    unittest.main()
