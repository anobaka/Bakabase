#!/usr/bin/env python3
"""Pure lifecycle boundary tests: never launch products or installers."""
import copy
import contextlib
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch, MagicMock

SPEC = importlib.util.spec_from_file_location("installed_lifecycle", Path(__file__).with_name("run-installed-lifecycle.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class RunnerBoundary(unittest.TestCase):
    def test_local_and_wrong_architecture_are_rejected(self):
        with self.assertRaises(AssertionError):
            runner.base.require_hosted_runner({}, "Darwin", "arm64", "osx-arm64")
        hosted = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_TEMP": "fixture"}
        with self.assertRaises(AssertionError):
            runner.base.require_hosted_runner(hosted, "Darwin", "arm64", "osx-x64")

    def test_default_data_locations_are_product_specific_on_both_platforms(self):
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary)
            windows = runner.default_paths("win-x64", home, {"LOCALAPPDATA": str(home / "local")})
            self.assertEqual(home / "local/Bakabase.Client.AppData", windows["data"]["client"])
            self.assertEqual(home / "local/Bakabase.AppData", windows["data"]["unified"])
            for rid in ("osx-x64", "osx-arm64"):
                mac = runner.default_paths(rid, home, {})
                self.assertEqual(home / "Library/Application Support/Bakabase.Client", mac["data"]["client"])
                self.assertEqual(home / "Library/Application Support/Bakabase", mac["data"]["unified"])
                self.assertIn(Path("/Applications/Bakabase Client.app"), mac["absent"])

    def test_existing_data_and_shortcuts_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "existing"
            runner.require_pristine([path], {})
            path.write_text("pre-existing user data")
            with self.assertRaises(AssertionError):
                runner.require_pristine([path], {})
            self.assertEqual("pre-existing user data", path.read_text())

    @unittest.skipIf(os.name == "nt", "Broken symlink fixture requires Unix symlink permissions")
    def test_broken_existing_symlink_is_rejected_without_following_it(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "existing"
            path.symlink_to("missing")
            with self.assertRaises(AssertionError):
                runner.require_pristine([path], {})
            self.assertTrue(path.is_symlink())

    def test_data_overrides_and_startup_hooks_are_forbidden_case_insensitively(self):
        for key in runner.FORBIDDEN_ENV:
            for spelling in (key, key.lower()):
                with self.subTest(key=spelling), self.assertRaises(AssertionError):
                    runner.require_pristine([], {spelling: "/external/data-or-hook"})
        runner.require_pristine([], {"HTTP_PROXY": "http://127.0.0.1:1"})

    def test_failed_authorization_prepare_keeps_cleanup_failure_and_prevents_owned_file_removal(self):
        module = runner.sibling("installed-macos-authorization")
        remaining = [{"pid": 123, "path": "owned authorization writer"}]
        failed_cleanup = {"passed": False, "canRemoveOwnedPaths": False, "remainingProcesses": remaining}
        context = SimpleNamespace(evidence={"preparationFailed": True, "preparationCleanup": failed_cleanup})
        authorization = SimpleNamespace(PreparationFailure=module.PreparationFailure,
            prepare=MagicMock(side_effect=module.PreparationFailure(context)), cleanup=MagicMock(return_value=failed_cleanup))
        with tempfile.TemporaryDirectory() as temporary, contextlib.ExitStack() as stack:
            root = Path(temporary)
            home = root / "home"
            home.mkdir()
            results = root / "results"
            results.mkdir()
            paths = {"absent": [root / "client-data", root / "unified-data"],
                     "data": {role: root / (role + "-data") for role in runner.ROLES}}
            args = SimpleNamespace(rid="osx-arm64", version="old", unified_packages=root / "unified",
                                   client_packages=root / "client", updates_manifest=root / "updates.json", macos_native_authorization=True)
            feed = SimpleNamespace(environment={}, requests=[], deliveries=[], close=MagicMock())
            def sibling(name):
                return {"installed-update-feed": SimpleNamespace(Feed=lambda *_: feed),
                        "installed-macos-authorization": authorization,
                        "installed-lifecycle-diagnostics": SimpleNamespace(capture=lambda *_: {})}[name]
            def prepare_data(path, role):
                path.mkdir()
                (path / "client").mkdir()
                (path / "app.json").write_text('{"App":{}}')
                return 41001 if role == "client" else 41002
            def audit(_packages, role, *_):
                return {"bundleName": "Bakabase Client.app" if role == "client" else "Bakabase.app",
                        "artifacts": {"installer": {"file": "fixture.pkg"}}}
            def command(arguments, *_):
                self.assertEqual(["pkgutil", "--expand-full"], arguments[:2], "Cleanup attempted filesystem mutation despite authorization residuals")
                expanded = arguments[3]
                expanded.mkdir()
                role = Path(arguments[2]).parent.name
                receipt = runner.base.contract.PRODUCTS[role]["bundle"]
                (expanded / "PackageInfo").write_text(f'<pkg-info identifier="{receipt}"/>')
            def updater_cleanup(_apps, report):
                report["updaterCleanup"] = {"passed": True, "remainingProcesses": []}
            stack.enter_context(patch.dict(runner.os.environ, {"RUNNER_TEMP": temporary}, clear=True))
            patches = [(runner.Path, "home", {"return_value": home}),
                       (runner, "default_paths", {"return_value": paths}),
                       (runner, "sibling", {"side_effect": sibling}),
                       (runner.shutil, "disk_usage", {"return_value": SimpleNamespace(free=10 * 1024 ** 3)}),
                       (runner.base, "audit_packages", {"side_effect": audit}),
                       (runner.base, "prepare_data", {"side_effect": prepare_data}),
                       (runner.base, "command", {"side_effect": command}),
                       (runner.base, "remove_owned_tree", {}),
                       (runner.subprocess, "run", {"return_value": SimpleNamespace(returncode=1, stdout="")}),
                       (runner.http.server, "ThreadingHTTPServer", {"return_value": MagicMock(server_port=43111)}),
                       (runner.threading, "Thread", {}), (runner.socket, "socket", {}),
                       (runner, "stop_app", {}), (runner.updates, "cleanup", {"side_effect": updater_cleanup})]
            mocks = {(id(owner), name): stack.enter_context(patch.object(owner, name, **options))
                     for owner, name, options in patches}
            report = {}
            with self.assertRaisesRegex(AssertionError, "Installed lifecycle cleanup failed"):
                runner.execute(args, results, report)
            authorization.cleanup.assert_called_once_with(context)
            self.assertEqual("PreparationFailure", report["failureBeforeCleanup"]["type"])
            self.assertIs(context.evidence, report["nativeAuthorizationSetup"])
            self.assertEqual(failed_cleanup, report["nativeAuthorizationCleanup"])
            self.assertIn("Native authorization fixture cleanup did not pass", report["cleanupErrors"])
            self.assertTrue(all(path.exists() for path in paths["absent"]))
            mocks[(id(runner.Path), "home")].assert_called()
            self.assertEqual([], list(home.iterdir()))
            mocks[(id(runner.base), "remove_owned_tree")].assert_not_called()


class ProductSeparation(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.apps = {role: {"role": role, "rid": "osx-arm64", "data": root / (role + "-data"),
                            "exe": root / role / role, "port": 41001 + i, "environment": {}}
                     for i, role in enumerate(runner.ROLES)}

    def test_separate_products_are_accepted(self):
        runner.require_separate_apps(self.apps)

    def test_same_or_nested_data_and_executables_are_rejected(self):
        for field in ("data", "exe"):
            for nested in (False, True):
                apps = copy.deepcopy(self.apps)
                apps["unified"][field] = apps["client"][field] / "nested" if nested else apps["client"][field]
                with self.subTest(field=field, nested=nested), self.assertRaises(AssertionError):
                    runner.require_separate_apps(apps)

    def test_shared_invalid_ports_and_missing_roles_are_rejected(self):
        for port in (self.apps["client"]["port"], 0, 65536, True, "41002"):
            apps = copy.deepcopy(self.apps)
            apps["unified"]["port"] = port
            with self.subTest(port=port), self.assertRaises(AssertionError):
                runner.require_separate_apps(apps)
        with self.assertRaises(AssertionError):
            runner.require_separate_apps({"client": self.apps["client"]})

    def test_survivor_process_identity_must_remain_unchanged(self):
        runner.require_same_process({"processIds": [123]}, {"processIds": [123]})
        for pids in ([], [124], [123, 124]):
            with self.subTest(pids=pids), self.assertRaises(AssertionError):
                runner.require_same_process({"processIds": [123]}, {"processIds": pids})


class StartupReadiness(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        data = Path(self.temporary.name)
        (data / "app.json").write_text(json.dumps({"App": {"version": "core"}}))
        self.app = {"role": "unified", "data": data, "exe": data / "Bakabase", "port": 41001}
        self.info = json.dumps({"code": 0, "data": {"coreVersion": "core", "appDataPath": str(data),
                               "anchorPath": str(data), "defaultDataPath": str(data), "dataInInstallRoot": False}}).encode()
        self.page = b"<html><script></script></html>"

    def test_initial_ui_timeout_is_retried_within_existing_readiness(self):
        with patch.object(runner.base, "api", side_effect=[self.info, TimeoutError("timed out"), self.info, self.page]) as api, \
                patch.object(runner.base, "native_processes", return_value=[42]), \
                patch.object(runner.time, "sleep"), patch.object(runner.time, "monotonic", return_value=0):
            result = runner.observe_app(self.app, startup=True)
        self.assertTrue(result["passed"])
        self.assertEqual(1, result["startupUiTimeoutRetries"])
        self.assertEqual(["/app/info", "/", "/app/info", "/"], [call.args[1] for call in api.call_args_list])
        self.assertTrue(all(len(call.args) == 2 and not call.kwargs for call in api.call_args_list))

    def test_ui_timeout_cannot_reset_the_original_startup_deadline(self):
        with patch.object(runner.base, "api", side_effect=[self.info, TimeoutError("timed out")]) as api, \
                patch.object(runner.base, "native_processes", return_value=[42]), \
                patch.object(runner.time, "sleep"), patch.object(runner.time, "monotonic", side_effect=[0, 119, 120]):
            with self.assertRaisesRegex(TimeoutError, "installed startup failed: UI GET /"):
                runner.observe_app(self.app, startup=True)
        self.assertEqual(2, api.call_count)

    def test_survivor_ui_timeout_fails_immediately_without_retry(self):
        with patch.object(runner.base, "api", side_effect=[self.info, TimeoutError("timed out")]) as api, \
                patch.object(runner.base, "native_processes", return_value=[42]), patch.object(runner.time, "sleep") as sleep:
            with self.assertRaisesRegex(TimeoutError, "^timed out$"):
                runner.observe_app(self.app)
        self.assertEqual(2, api.call_count)
        sleep.assert_not_called()

    def test_updater_startup_uses_its_existing_deadline(self):
        with patch.object(runner.base, "api") as api, patch.object(runner.time, "monotonic", return_value=120):
            with self.assertRaisesRegex(TimeoutError, "installed startup failed"):
                runner.observe_app(self.app, startup=True, deadline=120)
        api.assert_not_called()

    def test_startup_still_rejects_wrong_html_without_retry(self):
        with patch.object(runner.base, "api", side_effect=[self.info, b"not the application UI"]) as api, \
                patch.object(runner.base, "native_processes", return_value=[42]), patch.object(runner.time, "sleep") as sleep:
            with self.assertRaisesRegex(AssertionError, "actual UI"):
                runner.observe_app(self.app, startup=True)
        self.assertEqual(2, api.call_count)
        sleep.assert_not_called()


class PersistedState(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.data = Path(self.temporary.name)
        (self.data / "client").mkdir()
        (self.data / "client/host.json").write_text(json.dumps({"LoopbackPort": 41001}))
        (self.data / "client/connection.json").write_text(json.dumps({"DeviceName": runner.CLIENT_DEVICE_NAME,
                                                                     "Servers": [], "ActiveServerId": None}))
        self.app = {"data": self.data, "port": 41001}

    def test_real_client_settings_hashes_detect_mutation_or_loss(self):
        original = runner.client_state(self.app)
        self.assertEqual({"host.json", "connection.json"}, set(original))
        self.assertEqual(original, runner.verify_client_state(self.app, original))
        path = self.data / "client/connection.json"
        content = path.read_text()
        path.write_text(content + "\n")
        with self.assertRaisesRegex(AssertionError, "settings or sentinel changed"):
            runner.verify_client_state(self.app, original)
        path.unlink()
        with self.assertRaises(FileNotFoundError):
            runner.verify_client_state(self.app, original)

    def test_client_port_and_persistent_device_name_are_checked(self):
        with self.assertRaises(AssertionError):
            runner.client_state(dict(self.app, port=41002))
        (self.data / "client/connection.json").write_text(json.dumps({"DeviceName": "other", "Servers": []}))
        with self.assertRaises(AssertionError):
            runner.client_state(self.app)

    def test_readonly_database_retention_detects_missing_or_extra_resources(self):
        database = self.data / "bakabase_insideworld.db"
        with self.assertRaises(AssertionError):
            runner.database_state(self.app, 1)
        self.assertFalse(database.exists())
        with contextlib.closing(sqlite3.connect(database)) as db:
            db.execute("CREATE TABLE ResourcesV2 (Id INTEGER PRIMARY KEY)")
            db.execute("INSERT INTO ResourcesV2 VALUES (7)")
            db.commit()
        self.assertEqual({"integrity": "ok", "resourceCount": 1, "resourceIds": [7]}, runner.database_state(self.app, 7))
        for wrong in (1, True, -1):
            with self.subTest(wrong=wrong), self.assertRaises(AssertionError):
                runner.database_state(self.app, wrong)
        with contextlib.closing(sqlite3.connect(database)) as db:
            db.execute("INSERT INTO ResourcesV2 VALUES (8)")
            db.commit()
        with self.assertRaises(AssertionError):
            runner.database_state(self.app, 7)

    def test_both_native_pids_are_required_at_coexistence_checkpoint(self):
        apps = {role: {"role": role, "port": 41001 + index, "data": self.data / role,
                       "exe": self.data / (role + ".exe"), "environment": {}}
                for index, role in enumerate(runner.ROLES)}
        with patch.object(runner, "observe_app", return_value={"processIds": [123]}):
            with self.assertRaisesRegex(AssertionError, "same process"):
                runner.verify_coexistence(apps, "fixture", {}, 7)

    def test_runtime_api_must_read_the_client_persistent_setting(self):
        app = dict(self.app, role="client", exe=self.data / "Bakabase.Client")
        setting = {"deviceName": runner.CLIENT_DEVICE_NAME, "servers": [], "activeServerId": None, "serverReachable": False}
        def api(_port, path):
            if path == "/client/connect-page":
                return b"<html><script></script></html>"
            values = {"/client/app/info": {"dataDirectory": str(self.data), "available": True},
                      "/remote-access/context": {"clientMode": 2, "serverReachable": False},
                      "/client/status": setting}
            return json.dumps({"code": 0, "data": values[path]}).encode()
        with patch.object(runner.base, "api", side_effect=api), patch.object(runner.base, "native_processes", return_value=[42]):
            self.assertEqual(runner.CLIENT_DEVICE_NAME, runner.observe_app(app)["clientSettingsApi"]["deviceName"])
            setting["deviceName"] = "lost settings"
            with self.assertRaisesRegex(AssertionError, "retained persistent settings"):
                runner.observe_app(app)


if __name__ == "__main__":
    unittest.main()
