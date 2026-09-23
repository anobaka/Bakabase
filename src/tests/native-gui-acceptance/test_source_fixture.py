#!/usr/bin/env python3
"""Pure source fixture ownership/regression tests. No native app is launched."""
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from unittest.mock import Mock

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("source_fixture_under_test", HERE / "source_fixture.py")
source = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(source)


def state(sharing=False, peers=None):
    return {"identity": {"nodeId": "source-node", "libraryEpoch": "source-epoch", "name": "source"},
            "sharingEnabled": sharing, "browsingEnabled": False, "peers": peers or [], "requests": []}


class Process:
    def __init__(self, pid):
        self.pid, self.returncode = pid, None
        self.stdout, self.stderr = io.BytesIO(), io.BytesIO()
        self.terminated, self.killed = False, False

    def poll(self):
        return self.returncode

    def terminate(self):
        self.terminated, self.returncode = True, -15

    def kill(self):
        self.killed, self.returncode = True, -9

    def wait(self, timeout):
        return self.returncode


class FixtureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.home = self.root / "fake-home"
        self.home.mkdir()
        self.fake_home = patch.object(source.Path, "home", return_value=self.home)
        self.fake_home.start()
        self.addCleanup(self.fake_home.stop)
        self.environment = patch.dict(os.environ, RUNNER_TEMP=str(self.root), GITHUB_REPOSITORY="anobaka/Bakabase", GITHUB_SHA="b" * 40)
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.guard = patch.object(source, "hosted")
        self.guard.start()
        self.addCleanup(self.guard.stop)
        self.publish, self.web = self.root / "publish", self.root / "audited-web"
        self.publish.mkdir()
        self.web.mkdir()
        for name in (source.NAME, source.NAME + ".dll", "Bakabase.Shell.dll", "Bakabase.Service.dll"):
            (self.publish / name).write_bytes(name.encode())
        (self.web / "index.html").write_text("<title>candidate production web</title>")
        self.candidate = {"passed": True, "unchangedProductSources": True, "rid": "osx-arm64",
                          "packageSourceSHA": "a" * 40, "testSourceSHA": "b" * 40,
                          "runID": 1234, "version": "0.0.1-candidate.1", "repository": "anobaka/Bakabase"}

    def create(self, **overrides):
        arguments = dict(parent=self.root, rid="osx-arm64", published_directory=self.publish, web_root=self.web,
                         candidate_provenance=self.candidate, results=self.root / "evidence",
                         expected_web_inventory=source.inventory(self.web))
        arguments.update(overrides)
        return source.SourceFixture.create(**arguments)

    def attach(self, fixture, pid=101):
        process = Process(pid)
        identity = {"pid": pid, "executable": str(fixture.app["exe"]), "started": "start-" + str(pid)}
        fixture.process, fixture.identity = process, identity
        fixture.spawn_count = 1
        binding = self.binding(fixture, parent_pid=pid)
        module = source.pid_module()
        for mocked in (patch.object(source, "pid_module", return_value=module),
                       patch.object(module, "capture", return_value=self.paired_observation(binding)),
                       patch.object(source, "capture_embedded_process", return_value={"code": "ProcessAbsent", "identity": None})):
            mocked.start()
            self.addCleanup(mocked.stop)
        return process, identity

    def write_state(self, fixture, *, key="retained-key"):
        folder = fixture.app["data"] / "federation"
        folder.mkdir(exist_ok=True)
        stored = {"nodeId": "source-node", "libraryEpoch": "source-epoch", "sharingEnabled": True,
                  "browsingEnabled": False, "peers": {}, "inboundGrants": {"grant": {"credentials": {"key": key}}},
                  "outboundGrants": {}, "incomingRequests": []}
        (folder / "state.json").write_text(json.dumps(stored))

    def binding(self, fixture, parent_pid=101, embedded_pid=301):
        application = {"pid": parent_pid, "ppid": 99, "uid": 501, "executable": str(fixture.app["exe"]),
                       "startSeconds": 100, "startMicroseconds": 100}
        embedded = {"pid": embedded_pid, "ppid": 1, "uid": 501, "executable": "/System/Library/Frameworks/WebKit.framework/WebKit",
                    "startSeconds": 100, "startMicroseconds": 101}
        binding = {"schemaVersion": 1, "initialRelationsVerified": True, "rootPath": [0, 0, 1],
                   "parentChildCount": 2, "observedEpochMs": 100002, "application": application, "embedded": embedded}
        fixture.app["embeddedAXBinding"] = binding
        return binding

    def paired_observation(self, binding):
        return {"code": "ObservedStable", "stable": True, "identity": binding["embedded"],
                "ownedIdentity": binding["application"]}

    def test_hosted_guard_precedes_every_create_disk_read(self):
        with patch.object(source, "hosted", side_effect=source.FixtureFailure("not-hosted")), \
                patch.object(source, "inventory") as inventory, patch.object(source, "unlinked") as path:
            with self.assertRaisesRegex(source.FixtureFailure, "not-hosted"):
                source.SourceFixture.create("/missing", "osx-arm64", "/publish", "/web", {}, "/results", expected_web_inventory={})
        inventory.assert_not_called()
        path.assert_not_called()

    def test_probe_guard_precedes_native_or_disk_access(self):
        with patch.object(source, "hosted", side_effect=source.FixtureFailure("not-hosted")), patch.object(source, "beneath") as read:
            with self.assertRaisesRegex(source.FixtureFailure, "not-hosted"):
                source.validate_probe_app({"rid": "osx-arm64"})
        read.assert_not_called()

    def test_candidate_false_wrong_rid_repo_or_source_is_rejected(self):
        for key, value in (("passed", False), ("unchangedProductSources", False), ("rid", "win-x64"),
                           ("repository", "other/repo"), ("testSourceSHA", "x" * 40), ("runID", True)):
            candidate = dict(self.candidate, **{key: value})
            with self.subTest(key=key), self.assertRaises(source.FixtureFailure):
                self.create(candidate_provenance=candidate)
        self.assertFalse((self.root / "evidence").exists())

    def test_only_fully_audited_candidate_web_can_be_served(self):
        expected = source.inventory(self.web)
        (self.web / "index.html").write_text("different build")
        with self.assertRaisesRegex(source.FixtureFailure, "SourceWebPayloadNotAudited"):
            self.create(expected_web_inventory=expected)
        self.assertFalse((self.root / "evidence").exists())

    def test_symlink_publish_is_not_trusted(self):
        link = self.root / "publish-alias"
        link.symlink_to(self.publish, target_is_directory=True)
        with self.assertRaisesRegex(source.FixtureFailure, "TraversesLink"):
            self.create(published_directory=link)

    def test_inventory_rejects_link_to_outside_and_size_overflow(self):
        (self.web / "escape").symlink_to(self.publish / source.NAME)
        with self.assertRaisesRegex(source.FixtureFailure, "TraversesLink"):
            source.inventory(self.web)
        (self.web / "escape").unlink()
        with patch.object(source, "MAX_TREE_BYTES", 3), self.assertRaisesRegex(source.FixtureFailure, "BudgetExceeded"):
            source.inventory(self.web)

    def test_results_and_existing_paths_cannot_be_overwritten(self):
        results = self.root / "evidence"
        results.mkdir()
        sentinel = results / "foreign"
        sentinel.write_text("retain")
        with self.assertRaisesRegex(source.FixtureFailure, "SourceResultsAlreadyExist"):
            self.create()
        self.assertEqual("retain", sentinel.read_text())

    def test_existing_macos_bundle_domain_blocks_launch_and_fixture_writes(self):
        domain = self.home / "Library/WebKit" / source.BUNDLE
        domain.mkdir(parents=True)
        sentinel = domain / "preexisting"
        sentinel.write_text("keep")
        with self.assertRaisesRegex(source.FixtureFailure, "SourceBundleDomainAlreadyExists"):
            self.create()
        self.assertEqual("keep", sentinel.read_text())
        self.assertFalse((self.root / "evidence").exists())
        self.assertFalse(list(self.root.glob("native-gui-source-*")))

    def test_external_cache_parent_symlink_is_rejected_without_following_it(self):
        library = self.home / "Library"
        library.mkdir()
        (library / "WebKit").symlink_to(self.publish, target_is_directory=True)
        with self.assertRaisesRegex(source.FixtureFailure, "SourcePathTraversesLink"):
            self.create()

    def test_create_separates_runtime_provenance_from_candidate_web(self):
        fixture = self.create()
        proof = source.validate_probe_app(fixture.app)
        self.assertFalse(proof["sourceIsInstalledCandidate"])
        self.assertTrue(proof["sourceRuntimeFromExecutionHead"])
        self.assertEqual("a" * 40, proof["candidatePackageSourceSHA"])
        self.assertEqual("b" * 40, proof["sourceHeadSHA"])
        self.assertTrue(proof["privateInstanceId"].startswith(source.NAME + "."))
        self.assertEqual(source.NAME, fixture.app["exe"].name)
        self.assertTrue(fixture.app["exe"].is_relative_to(fixture.root))
        self.assertEqual(source.TITLES, fixture.resource_names)
        config = json.loads((fixture.app["data"] / "app.json").read_text())
        self.assertNotIn("sharing", json.dumps(config).lower())
        self.assertNotIn("browsing", json.dumps(config).lower())

    def test_probe_rejects_binary_tamper_and_product_role_confusion(self):
        fixture = self.create()
        wrong = dict(fixture.app, role="unified")
        with self.assertRaisesRegex(source.FixtureFailure, "SourceRoleInvalid"):
            source.validate_probe_app(wrong)
        (fixture.app["exe"].parent / "Bakabase.Service.dll").write_bytes(b"changed")
        with self.assertRaisesRegex(source.FixtureFailure, "SourceBinaryChanged"):
            source.validate_probe_app(fixture.app)

    def test_probe_rejects_edited_marker_and_forged_provenance(self):
        fixture = self.create()
        original = copy.deepcopy(fixture.app["sourceProvenance"])
        fixture.app["sourceProvenance"]["sourceHeadSHA"] = "c" * 40
        with self.assertRaisesRegex(source.FixtureFailure, "SourceProvenanceChanged"):
            source.validate_probe_app(fixture.app)
        fixture.app["sourceProvenance"] = original
        marker = fixture.root / "source-owner.json"
        marker.write_text(json.dumps({"scope": source.SCOPE, "token": "0" * 32, "port": fixture.app["port"]}))
        with self.assertRaisesRegex(source.FixtureFailure, "SourceOwnershipProofInvalid"):
            source.validate_probe_app(fixture.app)

    def test_marker_symlink_is_rejected_before_reading_target(self):
        fixture = self.create()
        marker = fixture.root / "source-owner.json"
        target = self.root / "external-marker"
        marker.rename(target)
        marker.symlink_to(target)
        with self.assertRaisesRegex(source.FixtureFailure, "SourcePathTraversesLink"):
            source.validate_probe_app(fixture.app)

    def test_api_cannot_enable_browsing_issue_code_connect_or_approve(self):
        fixture = self.create()
        with patch.object(source.http.client, "HTTPConnection") as connection:
            for method, path in (("PUT", "/federation/local/peers/browsing"), ("POST", "/federation/local/peers/invite"),
                                 ("POST", "/federation/local/peers/connect"), ("POST", "/federation/local/peers/requests/1/approve")):
                with self.subTest(path=path), self.assertRaisesRegex(source.FixtureFailure, "SourceUiActionCannotUseApi"):
                    fixture._api(method, path, {})
        connection.assert_not_called()

    def test_start_uses_unique_executable_same_origin_and_never_mutates_sharing(self):
        fixture = self.create()
        process = Process(101)
        identity = {"pid": 101, "executable": str(fixture.app["exe"]), "started": "start-101"}
        def launch(arguments, **kwargs):
            self.assertEqual(str(fixture.app["exe"]), arguments[0])
            self.assertEqual(fixture.root, kwargs["cwd"])
            self.assertTrue(kwargs["start_new_session"])
            self.assertEqual(source.subprocess.DEVNULL, kwargs["stdin"])
            (fixture.app["data"] / "native-source-ready.json").write_text(json.dumps({"pid": 101, "Port": fixture.app["port"],
                "dataDirectory": str(fixture.app["data"]), "privateInstanceId": fixture.app["sourceProvenance"]["privateInstanceId"],
                "NodeId": "source-node", "LibraryEpoch": "source-epoch"}))
            return process
        def api(method, path, **_):
            self.assertEqual("GET", method)
            self.assertEqual("/app/info", path)
            return {"appDataPath": str(fixture.app["data"]), "coreVersion": "source-runtime"}
        with patch.object(source.subprocess, "Popen", side_effect=launch), patch.object(source, "_native_identity", return_value=identity), \
                patch.object(fixture, "_api", side_effect=api), patch.object(fixture, "status", return_value=state()):
            result = fixture.start()
        self.assertEqual(101, result["pid"])
        self.assertIs(result["app"], fixture.app)
        self.assertFalse(result["status"]["sharingEnabled"])
        process.stdout.close()
        process.stderr.close()

    def test_started_source_may_not_already_have_sharing_or_browsing_enabled(self):
        initial = state()
        initial["sharingEnabled"] = True
        # Exercise the real start checks with mocked native/HTTP edges only.
        fixture = self.create()
        process = Process(101)
        identity = {"pid": 101, "executable": str(fixture.app["exe"]), "started": "start-101"}
        def launch(*_, **__):
            (fixture.app["data"] / "native-source-ready.json").write_text(json.dumps({"pid": 101, "Port": fixture.app["port"],
                "dataDirectory": str(fixture.app["data"]), "privateInstanceId": fixture.app["sourceProvenance"]["privateInstanceId"],
                "NodeId": "source-node", "LibraryEpoch": "source-epoch"}))
            return process
        with patch.object(source.subprocess, "Popen", side_effect=launch), patch.object(source, "_native_identity", return_value=identity), \
                patch.object(fixture, "_api", return_value={"appDataPath": str(fixture.app["data"])}), patch.object(fixture, "status", return_value=initial):
            with self.assertRaisesRegex(source.FixtureFailure, "SourceFixtureChangedDefaultSharing"):
                fixture.start()
        self.assertFalse(fixture.report["passed"])
        process.stdout.close()
        process.stderr.close()

    def test_reused_pid_is_never_signalled_and_fixture_is_retained(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        with patch.object(source, "_native_identity", return_value=dict(identity, started="different-process")):
            with self.assertRaisesRegex(source.FixtureFailure, "SourceProcessIdentityChanged"):
                fixture.close()
        self.assertFalse(process.terminated)
        self.assertFalse(process.killed)
        self.assertTrue(fixture.root.exists())
        self.assertFalse(fixture.report["cleanup"]["passed"])
        process.stdout.close()
        process.stderr.close()

    def test_stop_verifies_grants_and_records_exact_owned_descendants(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        self.write_state(fixture)
        child = {"pid": 102, "executable": "/fixture/owned-helper", "started": "child-start"}
        def native(pid, _rid, **_):
            if process.returncode is not None:
                return None
            return identity if pid == 101 else child
        with patch.object(source, "_native_identity", side_effect=native), patch.object(source, "_descendant_ids", return_value=[102]), \
                patch.object(fixture, "status", return_value=state(True)), patch.object(source.os, "kill") as kill:
            stopped = fixture.stop()
        self.assertTrue(stopped["passed"])
        self.assertTrue(process.terminated)
        self.assertEqual([child], stopped["descendants"])
        self.assertEqual("source-node", fixture.last_stopped["status"]["identity"]["nodeId"])
        self.assertNotIn("retained-key", json.dumps(fixture.report))
        kill.assert_not_called()  # It exited with its parent, not killed by name.

    def test_embedded_binding_schema_root_and_micro_identity_are_required_before_stop(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        original = self.binding(fixture)
        bad = []
        for key, value in (("schemaVersion", True), ("initialRelationsVerified", False), ("parentChildCount", 1),
                           ("rootPath", [True, 1]), ("observedEpochMs", 0), ("unrecognized", True)):
            bad.append(dict(original, **{key: value}))
        for key, value in (("pid", 102), ("executable", "/unrelated"), ("uid", 502)):
            bad.append(dict(original, application=dict(original["application"], **{key: value})))
        bad.append(dict(original, embedded=dict(original["embedded"], startMicroseconds=True)))
        bad.append(dict(original, embedded=dict(original["embedded"], unrecognized=True)))
        with patch.object(source, "_native_identity", return_value=identity), patch.object(source, "pid_module", wraps=source.pid_module) as module:
            for value in bad:
                fixture.app["embeddedAXBinding"] = value
                with self.subTest(value=value), self.assertRaises(source.FixtureFailure):
                    fixture.stop(retain_state=False)
        self.assertFalse(process.terminated)
        self.assertFalse(fixture.renderer_exit_proven)
        process.stdout.close()
        process.stderr.close()

    def test_embedded_identity_must_still_match_before_parent_termination(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        binding = self.binding(fixture)
        module = source.pid_module()
        result = self.paired_observation(binding)
        result["identity"] = dict(binding["embedded"], startMicroseconds=102)
        with patch.object(source, "_native_identity", return_value=identity), patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "capture", return_value=result):
            with self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedIdentityChangedBeforeStop"):
                fixture.stop(retain_state=False)
        self.assertFalse(process.terminated)
        self.assertTrue(fixture.root.exists())
        process.stdout.close()
        process.stderr.close()

    def test_embedded_exit_wait_is_read_only_even_when_renderer_is_a_descendant(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        binding = self.binding(fixture)
        module = source.pid_module()
        # A ppid change alone is the same live process, and must not end waiting.
        samples = [{"code": "ObservedStable", "identity": dict(binding["embedded"], ppid=101)},
                   {"code": "ProcessAbsent", "identity": None}]
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[301]), patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "capture", return_value=self.paired_observation(binding)), \
                patch.object(source, "capture_embedded_process", side_effect=samples) as capture, \
                patch.object(source.os, "kill") as kill, patch.object(source.time, "sleep"):
            stopped = fixture.stop(retain_state=False)
        self.assertTrue(stopped["passed"])
        self.assertEqual([], stopped["descendants"])
        self.assertEqual(2, stopped["embeddedProcessExit"]["observations"])
        self.assertEqual("ProcessAbsent", stopped["embeddedProcessExit"]["outcome"])
        self.assertTrue(fixture.renderer_exit_proven)
        self.assertFalse(stopped["embeddedProcessExit"]["signalSent"])
        self.assertEqual([301, 301], [call.args[1] for call in capture.call_args_list])
        kill.assert_not_called()

    def test_reused_embedded_pid_proves_old_exit_without_touching_replacement(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        binding = self.binding(fixture)
        module = source.pid_module()
        replacement = dict(binding["embedded"], startMicroseconds=102)
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "capture", return_value=self.paired_observation(binding)), \
                patch.object(source, "capture_embedded_process", return_value={"code": "ObservedStable", "identity": replacement}), \
                patch.object(source.os, "kill") as kill:
            stopped = fixture.stop(retain_state=False)
        self.assertTrue(stopped["embeddedProcessExit"]["replacementUntouched"])
        self.assertEqual("PidReused", stopped["embeddedProcessExit"]["outcome"])
        self.assertEqual(replacement, stopped["embeddedProcessExit"]["replacementObserved"])
        kill.assert_not_called()

    def test_same_birth_token_with_changed_executable_is_not_claimed_as_pid_reuse(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        binding = self.binding(fixture)
        module = source.pid_module()
        changed = dict(binding["embedded"], executable="/unexpected/executable")
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "capture", return_value=self.paired_observation(binding)), \
                patch.object(source, "capture_embedded_process", return_value={"code": "ObservedStable", "identity": changed}), \
                patch.object(source.os, "kill") as kill:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedIdentityChanged"):
                fixture.stop(retain_state=False)
        self.assertFalse(fixture.renderer_exit_proven)
        self.assertNotIn("outcome", fixture.report["stops"][0]["embeddedProcessExit"])
        kill.assert_not_called()
        process.stdout.close()
        process.stderr.close()

    def test_uncertain_embedded_exit_retains_bundle_cache_root_and_blocks_restart(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        binding = self.binding(fixture)
        cache = self.home / "Library/WebKit" / source.BUNDLE
        cache.mkdir(parents=True)
        (cache / "evidence").write_bytes(b"keep")
        module = source.pid_module()
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "capture", return_value=self.paired_observation(binding)), \
                patch.object(source, "capture_embedded_process", side_effect=source.FixtureFailure("SourceEmbeddedObservationUncertain")), \
                patch.object(source.os, "kill") as kill:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedObservationUncertain"):
                fixture.close()
        self.assertTrue(process.terminated)
        self.assertTrue(fixture.root.exists())
        self.assertEqual(b"keep", (cache / "evidence").read_bytes())
        self.assertFalse(fixture.report["cleanup"]["passed"])
        self.assertFalse(fixture.report["stops"][0]["passed"])
        self.assertFalse(fixture.renderer_exit_proven)
        with patch.object(source.subprocess, "Popen") as launch, self.assertRaises(source.FixtureFailure):
            fixture.restart()
        launch.assert_not_called()
        kill.assert_not_called()
        process.stdout.close()
        process.stderr.close()

    def test_embedded_wait_uses_existing_stop_deadline(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        binding = self.binding(fixture)
        module = source.pid_module()
        now = [0]
        def still_running(*_):
            now[0] = 46
            return {"code": "ObservedStable", "identity": binding["embedded"]}
        with patch.object(source.time, "monotonic", side_effect=lambda: now[0]), \
                patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "capture", return_value=self.paired_observation(binding)), \
                patch.object(source, "capture_embedded_process", side_effect=still_running) as capture:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedProcessDidNotExit"):
                fixture.stop(retain_state=False)
        capture.assert_called_once()
        self.assertFalse(fixture.renderer_exit_proven)
        process.stdout.close()
        process.stderr.close()

    def test_embedded_native_helper_hosted_guard_precedes_all_os_reads(self):
        with patch.object(source, "hosted", side_effect=source.FixtureFailure("not-hosted")), \
                patch.object(source, "pid_module") as module, patch.object(source.subprocess, "run") as run:
            with self.assertRaisesRegex(source.FixtureFailure, "not-hosted"):
                source.observe_embedded_process("osx-arm64", 301)
            with self.assertRaisesRegex(source.FixtureFailure, "not-hosted"):
                source.capture_embedded_process({"rid": "osx-arm64"}, 301, 1)
        module.assert_not_called()
        run.assert_not_called()

    def test_unreadable_process_is_never_assumed_absent(self):
        module = source.pid_module()
        missing = SimpleNamespace(returncode=1, stdout=b"", stderr=b"")
        live = SimpleNamespace(returncode=0, stdout=b"301\n", stderr=b"")
        denied = SimpleNamespace(returncode=1, stdout=b"", stderr=b"permission denied")
        with patch.object(source, "pid_module", return_value=module), \
                patch.object(module, "sample", side_effect=module.IdentityFailure("ProcessUnavailable")):
            with patch.object(source.subprocess, "run", side_effect=[missing, missing]) as run:
                result = source.observe_embedded_process("osx-arm64", 301)
            self.assertEqual({"code": "ProcessAbsent", "identity": None}, result)
            self.assertEqual(2, run.call_count)
            self.assertEqual(["/bin/ps", "-p", "301", "-o", "pid="], run.call_args.args[0])
            for observations in ([live], [denied], [missing, live]):
                with self.subTest(observations=observations), patch.object(source.subprocess, "run", side_effect=observations), \
                        self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedObservationUncertain"):
                    source.observe_embedded_process("osx-arm64", 301)

    def test_embedded_capture_rejects_timeout_incomplete_or_malformed_child_result(self):
        app = {"rid": "osx-arm64"}
        for raw in ({"code": "ProcessAbsent", "identity": {}}, {"code": "ObservedStable", "identity": {}},
                    {"code": "ObservationUncertain", "identity": None}, {"code": "ProcessAbsent", "identity": None, "extra": True}):
            child = SimpleNamespace(returncode=0, stdout=json.dumps(raw).encode(), stderr=b"")
            with self.subTest(raw=raw), patch.object(source.subprocess, "run", return_value=child), \
                    self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedObservationUncertain"):
                source.capture_embedded_process(app, 301, 1)
        with patch.object(source.subprocess, "run", side_effect=source.subprocess.TimeoutExpired("fixed-helper", 1)), \
                self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedObservationUncertain"):
            source.capture_embedded_process(app, 301, 1)

    def test_output_failure_does_not_prevent_exact_owned_stop(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        fixture.log_errors.add("SourceOutputBudgetExceeded")
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]):
            result = fixture.stop(retain_state=False)
        self.assertTrue(result["passed"])
        self.assertTrue(process.terminated)

    def test_reused_descendant_is_never_killed(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        child = {"pid": 102, "executable": "/fixture/helper", "started": "first"}
        def native(pid, _rid, **_):
            if pid == 101:
                return identity if process.returncode is None else None
            return child if process.returncode is None else dict(child, started="replacement")
        with patch.object(source, "_native_identity", side_effect=native), patch.object(source, "_descendant_ids", return_value=[102]), \
                patch.object(source.os, "kill") as kill:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceDescendantIdentityChanged"):
                fixture.stop(retain_state=False)
        kill.assert_not_called()
        self.assertFalse(fixture.report["stops"][0]["passed"])
        process.stdout.close()
        process.stderr.close()

    def test_descendant_edge_must_still_belong_to_live_source(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        child = {"pid": 102, "executable": "/fixture/helper", "started": "child-start"}
        with patch.object(source, "_native_identity", side_effect=lambda pid, _, **__: identity if pid == 101 else child), \
                patch.object(source, "_descendant_ids", side_effect=[[102], []]), patch.object(source.os, "kill") as kill:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceDescendantOwnershipChanged"):
                fixture.stop(retain_state=False)
        self.assertFalse(process.terminated)
        kill.assert_not_called()
        process.stdout.close()
        process.stderr.close()

    def test_restart_requires_prior_verified_stop(self):
        fixture = self.create()
        with patch.object(source.subprocess, "Popen") as launch, self.assertRaisesRegex(source.FixtureFailure, "SourceRestartRequiresVerifiedStop"):
            fixture.restart()
        launch.assert_not_called()

    def test_start_cannot_discard_unproven_previous_renderer(self):
        fixture = self.create()
        binding = self.binding(fixture)
        fixture.renderer_exit_proven = False
        with patch.object(source.subprocess, "Popen") as launch, self.assertRaisesRegex(source.FixtureFailure, "SourcePreviousEmbeddedExitNotProven"):
            fixture.start()
        launch.assert_not_called()
        self.assertEqual(binding, fixture.app["embeddedAXBinding"])

    def test_candidate_without_complete_binding_cannot_authorize_cleanup(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        fixture.app["_embeddedAXCandidate"] = self.binding(fixture)
        fixture.app.pop("embeddedAXBinding")
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), \
                self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedExitCannotBeProven"):
            fixture.close()
        self.assertTrue(process.terminated)
        self.assertTrue(fixture.root.exists())
        process.stdout.close()
        process.stderr.close()

    def test_unbound_macos_source_stops_known_process_but_retains_all_owned_data(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        fixture.app.pop("embeddedAXBinding")
        cache = self.home / "Library/WebKit" / source.BUNDLE
        cache.mkdir(parents=True)
        (cache / "still-owned").write_bytes(b"unproven renderer may write here")
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]):
            with self.assertRaisesRegex(source.FixtureFailure, "SourceEmbeddedExitCannotBeProven"):
                fixture.close()
        self.assertTrue(process.terminated)
        self.assertIsNone(fixture.process)
        self.assertFalse(fixture.renderer_exit_proven)
        self.assertFalse(fixture.report["cleanup"]["passed"])
        stopped = fixture.report["stops"][0]
        self.assertTrue(stopped["ownedProcessesStopped"])
        self.assertFalse(stopped["passed"])
        self.assertEqual("NoVerifiedEmbeddedBinding", stopped["embeddedProcessExit"]["outcome"])
        self.assertTrue(fixture.root.exists())
        self.assertTrue(cache.exists())
        with self.assertRaisesRegex(source.FixtureFailure, "SourcePreviousEmbeddedExitNotProven"):
            fixture.close()  # A second cleanup cannot discard the earlier uncertainty.
        self.assertTrue(cache.exists())
        with patch.object(source.subprocess, "Popen") as launch, self.assertRaises(source.FixtureFailure):
            fixture.restart()
        launch.assert_not_called()

    def test_shared_deadline_never_extends_original_limits_and_rejects_invalid_values(self):
        with patch.object(source.time, "monotonic", return_value=100):
            for limit in (3, 45, 90):
                self.assertEqual(100 + limit, source.limited_deadline(None, limit))
                self.assertEqual(100 + limit, source.limited_deadline(1000, limit))
                self.assertEqual(100.25, source.limited_deadline(100.25, limit))
            for value in (True, "101", float("inf"), float("nan"), 100, 99):
                with self.subTest(value=value), self.assertRaises(source.FixtureFailure):
                    source.limited_deadline(value, 3)

    def test_expired_public_deadlines_prevent_native_api_and_seed_side_effects(self):
        fixture = self.create()
        with patch.object(source.time, "monotonic", return_value=100), \
                patch.object(source.subprocess, "Popen") as launch, patch.object(source, "_native_identity") as native, \
                patch.object(source.http.client, "HTTPConnection") as connection:
            for method in (fixture.start, fixture.stop, fixture.restart, fixture.status, fixture.seed, fixture.read_only_baseline):
                with self.subTest(method=method.__name__), self.assertRaisesRegex(source.FixtureFailure, "SourceOperationDeadlineExceeded"):
                    method(deadline=100)
        launch.assert_not_called()
        native.assert_not_called()
        connection.assert_not_called()
        self.assertNotIn("seed", fixture.report)
        self.assertFalse((fixture.root / "media").exists())

    def test_http_socket_and_every_response_chunk_use_same_shortened_deadline(self):
        fixture = self.create()
        for absolute, maximum in ((101, 1), (1000, 3), (None, 3)):
            now = [100.0]
            body = [b'{"code":0,"data":{"passed":true}}', b'']
            def request(*_, **__):
                now[0] += 0.2
            def response():
                now[0] += 0.1
                return SimpleNamespace(status=200, read1=read, isclosed=lambda: False)
            def read(_):
                now[0] += 0.1
                return body.pop(0)
            connection = Mock()
            connection.request.side_effect = request
            connection.getresponse.side_effect = response
            with self.subTest(absolute=absolute), patch.object(source.time, "monotonic", side_effect=lambda: now[0]), \
                    patch.object(source.http.client, "HTTPConnection", return_value=connection) as make:
                result = fixture._api("GET", "/fixture-read", deadline=absolute)
            self.assertEqual({"passed": True}, result)
            self.assertEqual(maximum, make.call_args.kwargs["timeout"])
            actual = [call.args[0] for call in connection.sock.settimeout.call_args_list]
            self.assertEqual(3, len(actual))
            self.assertTrue(all(0 < value < maximum for value in actual))
            self.assertEqual(sorted(actual, reverse=True), actual)
            connection.close.assert_called_once()

    def test_response_arriving_after_deadline_cannot_be_recorded_as_api_success(self):
        fixture = self.create()
        now = [100]
        connection = Mock()
        def read(_):
            now[0] = 102
            return b'{"code":0,"data":true}'
        connection.getresponse.return_value = SimpleNamespace(status=200, read1=read, isclosed=lambda: False)
        with patch.object(source.time, "monotonic", side_effect=lambda: now[0]), \
                patch.object(source.http.client, "HTTPConnection", return_value=connection), \
                self.assertRaisesRegex(source.FixtureFailure, "SourceOperationDeadlineExceeded"):
            fixture._api("GET", "/fixture-read", deadline=101)
        self.assertEqual([], fixture.report["fixtureApi"])
        connection.close.assert_called_once()

    def test_response_closing_socket_after_last_content_length_byte_is_accepted(self):
        fixture = self.create()
        connection = Mock()
        closed = [False]
        def read(_):
            closed[0] = True
            return b'{"code":0,"data":true}'
        def timeout(_):
            if closed[0]:
                raise OSError("closed socket must not be reused")
        connection.sock.settimeout.side_effect = timeout
        connection.getresponse.return_value = SimpleNamespace(status=200, read1=read, isclosed=lambda: closed[0])
        with patch.object(source.http.client, "HTTPConnection", return_value=connection):
            self.assertTrue(fixture._api("GET", "/fixture-read"))
        self.assertEqual(1, len(fixture.report["fixtureApi"]))
        connection.close.assert_called_once()

    def test_start_readiness_sleep_is_shortened_by_absolute_deadline(self):
        fixture = self.create()
        process = Process(101)
        identity = {"pid": 101, "executable": str(fixture.app["exe"]), "started": "first"}
        now = [100.0]
        def sleep(duration):
            now[0] += duration
        with patch.object(source.time, "monotonic", side_effect=lambda: now[0]), patch.object(source.time, "sleep", side_effect=sleep) as pause, \
                patch.object(source.subprocess, "Popen", return_value=process), patch.object(source, "_native_identity", return_value=identity) as native:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceStartupTimedOut"):
                fixture.start(deadline=100.1)
        self.assertAlmostEqual(0.1, pause.call_args.args[0])
        self.assertTrue(all(call.kwargs["timeout"] <= 0.1 + 1e-10 for call in native.call_args_list))
        self.assertEqual([], fixture.report["starts"])
        self.assertFalse(fixture.report["passed"])
        process.stdout.close()
        process.stderr.close()

    def test_stop_late_renderer_observation_does_not_extend_original_45_second_limit(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        now = [100]
        def observe(*_):
            now[0] = 146
            return {"code": "ProcessAbsent", "identity": None}
        with patch.object(source.time, "monotonic", side_effect=lambda: now[0]), \
                patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), patch.object(source, "capture_embedded_process", side_effect=observe):
            with self.assertRaisesRegex(source.FixtureFailure, "SourceOperationDeadlineExceeded"):
                fixture.stop(retain_state=False, deadline=1000)
        self.assertFalse(fixture.report["stops"][0]["passed"])

    def test_restart_forwards_absolute_deadline_without_reset(self):
        fixture = self.create()
        fixture.last_stopped, fixture.spawn_count = {"process": {"pid": 101}}, 1
        with patch.object(source.time, "monotonic", return_value=100), patch.object(fixture, "start", return_value={"passed": True}) as start:
            fixture.restart(deadline=110)
        start.assert_called_once_with(deadline=110)

    def test_restart_preserves_data_port_and_grants_with_new_pid(self):
        fixture = self.create()
        old_process, old_identity = self.attach(fixture)
        self.write_state(fixture)
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: old_identity if old_process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]), patch.object(fixture, "status", return_value=state(True)):
            fixture.stop()
        self.binding(fixture)
        fixture.app["_embeddedAXCandidate"] = copy.deepcopy(fixture.app["embeddedAXBinding"])
        process = Process(201)
        identity = {"pid": 201, "executable": str(fixture.app["exe"]), "started": "second-start"}
        def launch(arguments, **kwargs):
            self.assertNotIn("embeddedAXBinding", fixture.app)
            self.assertNotIn("_embeddedAXCandidate", fixture.app)
            self.assertEqual(str(fixture.app["port"]), arguments[6])
            self.assertEqual(str(fixture.app["data"]), arguments[2])
            (fixture.app["data"] / "native-source-ready.json").write_text(json.dumps({"pid": 201, "Port": fixture.app["port"],
                "dataDirectory": str(fixture.app["data"]), "privateInstanceId": fixture.app["sourceProvenance"]["privateInstanceId"],
                "NodeId": "source-node", "LibraryEpoch": "source-epoch"}))
            return process
        with patch.object(source.subprocess, "Popen", side_effect=launch), patch.object(source, "_native_identity", return_value=identity), \
                patch.object(fixture, "_api", return_value={"appDataPath": str(fixture.app["data"])}), patch.object(fixture, "status", return_value=state(True)):
            result = fixture.restart()
        self.assertTrue(result["identityAndGrantsRetained"])
        self.assertTrue(result["restart"])
        self.assertEqual(201, result["pid"])
        self.assertEqual(101, fixture.last_stopped["process"]["pid"])
        process.stdout.close()
        process.stderr.close()

    def test_restart_rejects_silently_replaced_grant(self):
        fixture = self.create()
        self.write_state(fixture, key="before")
        fixture.spawn_count = 1
        fixture.last_stopped = {"process": {"pid": 101}, "status": source.stable_status(state(True)),
                                "grantSHA256": source.grant_digest(fixture.app["data"])}
        self.write_state(fixture, key="different-after")
        process = Process(201)
        identity = {"pid": 201, "executable": str(fixture.app["exe"]), "started": "second-start"}
        def launch(*_, **__):
            (fixture.app["data"] / "native-source-ready.json").write_text(json.dumps({"pid": 201, "Port": fixture.app["port"],
                "dataDirectory": str(fixture.app["data"]), "privateInstanceId": fixture.app["sourceProvenance"]["privateInstanceId"],
                "NodeId": "source-node", "LibraryEpoch": "source-epoch"}))
            return process
        with patch.object(source.subprocess, "Popen", side_effect=launch), patch.object(source, "_native_identity", return_value=identity), \
                patch.object(fixture, "_api", return_value={"appDataPath": str(fixture.app["data"])}), patch.object(fixture, "status", return_value=state(True)):
            with self.assertRaisesRegex(source.FixtureFailure, "SourceRestartLostIdentityOrGrant"):
                fixture.restart()
        self.assertEqual([], fixture.report["starts"])
        self.assertFalse(fixture.report["passed"])
        process.stdout.close()
        process.stderr.close()

    def test_grant_digest_detects_key_replacement_without_disclosing_secret(self):
        fixture = self.create()
        self.write_state(fixture, key="first-secret")
        first = source.grant_digest(fixture.app["data"])
        self.write_state(fixture, key="replaced-secret")
        self.assertNotEqual(first, source.grant_digest(fixture.app["data"]))
        self.assertEqual(64, len(first))

    def test_stable_status_ignores_liveness_but_not_grant_or_epoch(self):
        peer = {"nodeId": "reader", "enabled": True, "inboundGrant": {"grantId": "one", "revision": 1},
                "outboundGrant": None, "address": None, "pathMappings": [], "connectionState": "online"}
        before = state(True, [peer])
        after = copy.deepcopy(before)
        after["peers"][0]["connectionState"] = "offline"
        self.assertEqual(source.stable_status(before), source.stable_status(after))
        after["peers"][0]["inboundGrant"]["revision"] = 2
        self.assertNotEqual(source.stable_status(before), source.stable_status(after))
        after = copy.deepcopy(before)
        after["identity"]["libraryEpoch"] = "new"
        self.assertNotEqual(source.stable_status(before), source.stable_status(after))

    def test_status_rejects_embedded_credentials_in_public_grant(self):
        peer = {"nodeId": "reader", "enabled": True, "inboundGrant": {"grantId": "one", "revision": 1, "key": "must-not-log"},
                "outboundGrant": None, "address": None, "pathMappings": []}
        with self.assertRaisesRegex(source.FixtureFailure, "SourceGrantSummaryInvalid"):
            source.stable_status(state(True, [peer]))

    def test_seed_uses_production_resource_calls_and_readback(self):
        fixture = self.create()
        process, _ = self.attach(fixture)
        resources = [{"id": i, "displayName": title, "path": str(fixture.root / "media" / title) if i == 1 else None,
                      "isFile": i == 1, "playedAt": None} for i, title in enumerate(source.TITLES, 1)]
        resources[0]["properties"] = {"2": {"12": {"values": [{"scope": 0, "bizValue": source.INTRODUCTION}]}}}
        calls = []
        def api(method, path, payload=None, **_):
            calls.append((method, path))
            if path == "/resource/placeholder":
                self.assertFalse(payload["acquireImmediately"])
                return [{"resourceId": i, "created": True} for i in (1, 2, 3)]
            if path.endswith("/materialize"):
                return {"materialized": True, "merged": False, "path": payload["path"]}
            if path.endswith("/property-value"):
                return None
            return resources
        with patch.object(fixture, "_verify_live"), patch.object(fixture, "_api", side_effect=api):
            seed = fixture.seed()
            baseline = fixture.read_only_baseline()
            self.assertTrue(baseline["noPlayedAtWrite"])
            resources[0]["playedAt"] = "2026-09-23T00:00:00Z"
            with self.assertRaisesRegex(source.FixtureFailure, "SourceBaselineSemanticsChanged"):
                fixture.read_only_baseline()
        self.assertEqual([1, 2, 3], seed["resourceIds"])
        self.assertTrue(all(path.startswith("/resource/") for _, path in calls))
        process.stdout.close()
        process.stderr.close()

    def test_partial_seed_cannot_be_retried_to_manufacture_success(self):
        fixture = self.create()
        process, _ = self.attach(fixture)
        with patch.object(fixture, "_verify_live"), patch.object(fixture, "_api", return_value=[{"resourceId": 1, "created": True}]) as api:
            with self.assertRaisesRegex(source.FixtureFailure, "SourceResourcesNotCreated"):
                fixture.seed()
            with self.assertRaisesRegex(source.FixtureFailure, "SourceSeedCannotRepeat"):
                fixture.seed()
        self.assertEqual(1, api.call_count)
        self.assertFalse(fixture.report["seed"]["passed"])
        process.stdout.close()
        process.stderr.close()

    def test_successful_cleanup_removes_only_private_root_and_preserves_evidence(self):
        fixture = self.create()
        fixture.close()
        self.assertFalse(fixture.root.exists())
        self.assertTrue(self.publish.exists())
        self.assertTrue(self.web.exists())
        self.assertTrue((fixture.results / "source-report.json").is_file())
        self.assertTrue(fixture.report["cleanup"]["passed"])

    def test_external_bundle_data_removed_only_after_verified_owned_stop(self):
        fixture = self.create()
        process, identity = self.attach(fixture)
        cache = self.home / "Library/WebKit" / source.BUNDLE
        cache.mkdir(parents=True)
        (cache / "owned-database").write_bytes(b"synthetic fixture data")
        preferences = self.home / "Library/Preferences" / (source.BUNDLE + ".plist")
        preferences.parent.mkdir(parents=True)
        preferences.write_bytes(b"synthetic plist")
        unrelated = self.home / "Library/WebKit/unrelated-app"
        unrelated.mkdir()
        with patch.object(source, "_native_identity", side_effect=lambda *_, **__: identity if process.returncode is None else None), \
                patch.object(source, "_descendant_ids", return_value=[]):
            fixture.close()
        self.assertTrue(process.terminated)
        self.assertFalse(cache.exists())
        self.assertFalse(preferences.exists())
        self.assertTrue(unrelated.exists())
        evidence = fixture.report["externalBundleCleanup"]
        self.assertTrue(evidence["passed"])
        self.assertTrue(evidence["afterOwnedProcessesStopped"])
        self.assertEqual(2, len([item for item in evidence["paths"] if item["removed"]]))
        self.assertTrue(fixture.report["cleanup"]["externalBundlePathsRemoved"])

    def test_external_domain_created_without_launch_proof_is_retained(self):
        fixture = self.create()
        cache = self.home / "Library/Caches" / source.BUNDLE
        cache.mkdir(parents=True)
        (cache / "unknown").write_text("keep")
        with self.assertRaisesRegex(source.FixtureFailure, "SourceBundleAppearedWithoutVerifiedLaunchAndStop"):
            fixture.close()
        self.assertTrue(fixture.root.exists())
        self.assertEqual("keep", (cache / "unknown").read_text())

    def test_external_bundle_symlink_preserves_all_domains_before_deleting_any(self):
        fixture = self.create()
        fixture.spawn_count = 1
        fixture.report["stops"] = [{"passed": True, "remainingProcesses": []}]
        first = self.home / "Library/Caches" / source.BUNDLE
        first.mkdir(parents=True)
        (first / "owned").write_text("keep until all domains validated")
        linked = self.home / "Library/WebKit" / source.BUNDLE
        linked.parent.mkdir(parents=True)
        linked.symlink_to(self.publish, target_is_directory=True)
        with self.assertRaisesRegex(source.FixtureFailure, "SourcePathTraversesLink"):
            fixture.close()
        self.assertTrue(first.exists())
        self.assertTrue(fixture.root.exists())
        self.assertTrue(self.publish.exists())

    def test_changed_home_or_unexpected_domain_type_cannot_authorize_removal(self):
        fixture = self.create()
        fixture.spawn_count = 1
        fixture.report["stops"] = [{"passed": True, "remainingProcesses": []}]
        path = self.home / "Library/Preferences" / (source.BUNDLE + ".plist")
        path.mkdir(parents=True)  # A directory in the exact file-only slot.
        with self.assertRaisesRegex(source.FixtureFailure, "SourceBundleDomainTypeChanged"):
            fixture.close()
        self.assertTrue(path.exists())
        self.assertTrue(fixture.root.exists())

    def test_cleanup_link_attack_retains_root_and_foreign_target(self):
        fixture = self.create()
        target = self.root / "foreign"
        target.mkdir()
        (target / "precious").write_text("keep")
        (fixture.root / "unexpected-link").symlink_to(target, target_is_directory=True)
        with self.assertRaisesRegex(source.FixtureFailure, "SourcePathTraversesLink"):
            fixture.close()
        self.assertTrue(fixture.root.exists())
        self.assertEqual("keep", (target / "precious").read_text())
        self.assertFalse(fixture.report["cleanup"]["passed"])

    def test_log_summary_does_not_retain_pairing_code_or_body(self):
        fixture = self.create()
        payload = b'POST approve {"code":"secret-code","key":"private-key"}\nSystem.InvalidOperationException: secret-code\n'
        fixture._drain(io.BytesIO(payload), "example")
        encoded = json.dumps(fixture.report)
        self.assertNotIn("secret-code", encoded)
        self.assertNotIn("private-key", encoded)
        self.assertIn("System.InvalidOperationException", encoded)


if __name__ == "__main__":
    unittest.main()
