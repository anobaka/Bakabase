#!/usr/bin/env python3
"""Single embedded tree ownership fixtures; no native UI or process inspection."""
import copy
import importlib.util
import json
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

HERE = Path(__file__).resolve().parent


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fixture = load("embedded_ax_fixture", "test_direct_ax_tree.py")
probe = load("embedded_ax_probe", "probe.py")
pid = load("embedded_ax_identity", "macos_pid_identity.py")
APP = {"role": "unified", "rid": "osx-arm64", "exe": PurePosixPath("/fixture/Bakabase"), "nativeBackend": "macos-direct-ax"}
OWNED = {"pid": 42, "ppid": 1, "uid": 501, "startSeconds": 100, "startMicroseconds": 123, "executable": "/fixture/Bakabase"}
EMBEDDED = {**OWNED, "pid": 900, "startSeconds": 101, "executable": "/fixture/exact-WebContent"}
LEGACY = {"pid": 42, "started": "owned-start", "executable": "/fixture/Bakabase"}
DIAGNOSTIC = {"expectedPid": 42, "actualPid": 900, "observedEpochMs": 102000, "origin": "owned-child-edge", "status": "observed",
              "path": [0, 0], "parentPath": [0], "windowIndex": 0, "childIndex": 0, "parentChildCount": 1,
              **{key: True for key in ("edgeStillMatches", "windowStillMatches", "parentMatches", "windowMatches", "actualPidStable")},
              "parentAXError": 0, "windowAXError": 0}
PAIR = {"code": "ObservedStable", "stable": True, "identity": EMBEDDED, "ownedIdentity": OWNED, "observedEpochMs": 102000}
BINDING = {"schemaVersion": 1, "initialRelationsVerified": True, "rootPath": [0, 0], "parentChildCount": 1,
           "observedEpochMs": 102000, "application": OWNED, "embedded": EMBEDDED}
PROOF = {"verified": True, "rootRole": "AXWebArea", "applicationPid": 42, "embeddedPid": 900, "rootPath": [0, 0],
         "contentRootRole": "AXWebArea", "contentRootPath": [0, 0], "wrapperChain": []}


@unittest.skipUnless(shutil.which("node"), "Node is required for pure AX fixtures")
class ProviderBinding(unittest.TestCase):
    def run_fixture(self, **options):
        result = subprocess.run([shutil.which("node"), "-e", fixture.FIXTURE, str(HERE), json.dumps(options)],
                                capture_output=True, text=True, timeout=5, check=True)
        return json.loads(result.stdout)

    def test_first_unknown_pid_is_diagnostic_only_without_role_or_metadata(self):
        for depth in (0, 1):
            with self.subTest(wrapperDepth=depth):
                result = self.run_fixture(embedded=True, wrapperDepth=depth)
                self.assertTrue(result["output"]["truncated"])
                self.assertEqual("DirectAXProcessMismatch", result["output"]["diagnostic"]["code"])
                self.assertNotIn("wrapper0:AXRole", result["reads"])
                self.assertNotIn("web:AXRole", result["reads"])
                self.assertNotIn("web:AXTitle", result["reads"])
                self.assertTrue(result["output"]["pidMismatch"]["parentMatches"])
                self.assertTrue(result["output"]["pidMismatch"]["windowMatches"])

    def test_exact_reciprocal_root_supports_one_complete_embedded_subtree_and_action(self):
        result = self.run_fixture(embedded=True, binding=True)
        self.assertFalse(result["output"]["truncated"])
        self.assertEqual(PROOF, result["output"]["embeddedAXProof"])
        self.assertIn("web:AXParent", result["reads"])
        self.assertIn("close:AXWindow", result["reads"])
        action = self.run_fixture(embedded=True, binding=True, action=True)
        self.assertTrue(action["output"]["performed"])
        self.assertEqual(1, action["presses"])
        self.assertEqual(PROOF, action["output"]["embeddedAXProof"])

    def test_root_parent_window_role_path_and_second_pid_fail_closed(self):
        for options in ({"embeddedWrongParent": True}, {"embeddedWrongWindow": True},
                        {"embeddedRootRole": "AXGroup"}, {"bindingPath": [0, 1]},
                        {"secondEmbeddedPid": True}, {"bindingCount": 2}, {"missingChildren": True}):
            with self.subTest(options=options):
                result = self.run_fixture(embedded=True, binding=True, **options)
                self.assertTrue(result["output"]["truncated"])
                self.assertFalse(result["output"]["embeddedAXProof"]["verified"])
                if options.get("embeddedWrongParent") or options.get("embeddedWrongWindow"):
                    self.assertNotIn("web:AXRole", result["reads"])
                if options.get("secondEmbeddedPid"):
                    self.assertNotIn("input:AXRole", result["reads"])
                    self.assertNotIn("input:AXParent", result["reads"])
                action = self.run_fixture(embedded=True, binding=True, action=True, **options)
                self.assertFalse(action["output"]["performed"])
                self.assertEqual(0, action["presses"])

    def test_same_pid_outside_bound_root_cannot_supply_metadata(self):
        result = self.run_fixture(embedded=True, binding=True, bindingCount=2, foreignNonWeb=True)
        self.assertTrue(result["output"]["truncated"])
        self.assertNotIn("foreign-static:AXRole", result["reads"])
        self.assertNotIn("OTHER-PROCESS-CONTENT", json.dumps(result["output"]))

    def test_root_detached_during_action_does_not_report_verified_success(self):
        result = self.run_fixture(embedded=True, binding=True, action=True, embeddedMoveAfterAction=True)
        self.assertEqual(1, result["presses"])
        self.assertFalse(result["output"]["performed"])

    def test_hit_descendant_requires_reciprocal_chain_into_the_registered_root(self):
        result = self.run_fixture(embedded=True, binding=True, embeddedDescendantHit=True, action=True)
        self.assertTrue(result["output"]["performed"])
        self.assertEqual(1, result["presses"])
        result = self.run_fixture(embedded=True, binding=True, foreignHit=True, action=True)
        self.assertFalse(result["output"]["performed"])
        self.assertEqual(0, result["presses"])

    def test_static_scroll_uses_exact_display_text_and_never_editable_descendant_values(self):
        result = self.run_fixture(embedded=True, binding=True, operation="scroll", action=True,
                                  target="text", scrollSupported=True, offscreenText=True)
        self.assertTrue(result["output"]["performed"])
        self.assertEqual(1, result["scrolls"])
        result = self.run_fixture(operation="scroll", action=True, target="text", scrollSupported=False)
        self.assertFalse(result["output"]["performed"])
        result = self.run_fixture(embedded=True, binding=True, editableDescendants=True, scrollSupported=True)
        self.assertFalse(result["output"]["truncated"])
        self.assertNotIn("EDITABLE-", json.dumps(result["output"]))
        self.assertNotIn("editable-text:AXValue", result["reads"])

    def test_single_child_wrappers_are_structure_only_and_bind_exact_content_scope(self):
        result = self.run_fixture(embedded=True, binding=True, wrapperDepth=2)
        snapshot = result["output"]
        self.assertFalse(snapshot["truncated"])
        proof = snapshot["embeddedAXProof"]
        self.assertEqual("AXGroup", proof["rootRole"])
        self.assertEqual([0, 0, 0, 0], proof["contentRootPath"])
        self.assertEqual(2, len(proof["wrapperChain"]))
        self.assertIsNotNone(probe.content_scope(probe.embedded_proof(proof)))
        for name in ("wrapper0", "wrapper1"):
            observed = [read.split(":", 1)[1] for read in result["reads"] if read.startswith(name + ":")]
            self.assertTrue(set(observed) <= {"AXRole", "AXChildren", "AXParent", "AXWindow"})
            self.assertNotIn(name + ":actions", result["calls"])
        wrappers = [node for window in snapshot["windows"] for node in window["nodes"] if node["structureOnly"]]
        self.assertEqual(2, len(wrappers))
        self.assertTrue(all(not node["insideWebContent"] and not node["actions"] and not node["name"] for node in wrappers))
        self.assertNotIn("WRAPPER-", json.dumps(snapshot))
        action = self.run_fixture(embedded=True, binding=True, wrapperDepth=2, action=True)
        self.assertTrue(action["output"]["performed"])
        self.assertEqual(1, action["presses"])

    def test_wrapper_never_inherits_owned_ancestor_web_content_permission(self):
        result = self.run_fixture(embedded=True, binding=True, wrapperDepth=1, ownedWebAncestor=True)
        snapshot = result["output"]
        self.assertFalse(snapshot["truncated"])
        wrappers = [node for window in snapshot["windows"] for node in window["nodes"] if node["structureOnly"]]
        self.assertEqual(1, len(wrappers))
        self.assertFalse(wrappers[0]["insideWebContent"])
        content = [node for window in snapshot["windows"] for node in window["nodes"]
                   if node["path"] == snapshot["embeddedAXProof"]["contentRootPath"]]
        self.assertEqual(1, len(content))
        self.assertTrue(content[0]["insideWebContent"])
        action = self.run_fixture(embedded=True, binding=True, wrapperDepth=1, ownedWebAncestor=True, action=True)
        self.assertTrue(action["output"]["performed"])

    def test_unlisted_owner_pid_hit_cannot_enter_embedded_tree_through_one_way_parent(self):
        result = self.run_fixture(embedded=True, binding=True, unlistedOwnedHit=True, action=True)
        self.assertFalse(result["output"]["performed"])
        self.assertEqual("DirectAXEmbeddedStructureChanged", result["output"]["diagnostic"]["code"])
        self.assertEqual(0, result["presses"])
        self.assertNotIn("unlisted-owned-hit:AXParent", result["reads"])
        # The legitimate renderer descendant still reaches its owned window
        # through the already verified cross-PID anchor edge.
        valid = self.run_fixture(embedded=True, binding=True, embeddedDescendantHit=True, action=True)
        self.assertTrue(valid["output"]["performed"])
        self.assertEqual(1, valid["presses"])

    def test_invalid_wrapper_structure_never_reads_content_or_submits_action(self):
        for options, code in (({"wrapperEmpty": True}, "DirectAXEmbeddedWrapperBranch"),
                              ({"wrapperBranch": True}, "DirectAXEmbeddedWrapperBranch"),
                              ({"wrapperRoleChanged": True}, "DirectAXEmbeddedStructureChanged"),
                              ({"wrapperSecondPid": True}, "DirectAXProcessMismatch"),
                              ({"wrapperCycle": True}, "DirectAXTreeCycle")):
            with self.subTest(options=options):
                result = self.run_fixture(embedded=True, binding=True, wrapperDepth=1, **options)
                self.assertTrue(result["output"]["truncated"])
                self.assertEqual(code, result["output"]["diagnostic"]["code"])
                self.assertNotIn("web:AXTitle", result["reads"])
                self.assertNotIn("close:AXTitle", result["reads"])
                self.assertNotIn("wrapper-sibling:AXRole", result["reads"])
                action = self.run_fixture(embedded=True, binding=True, wrapperDepth=1, action=True, **options)
                self.assertFalse(action["output"]["performed"])
                self.assertEqual(0, action["presses"])

    def test_action_cannot_select_a_moved_webarea_or_a_different_scope(self):
        for options in ({"expectedWrapperDepth": 2}, {"missingExpectedContentScope": True},
                        {"wrapperMovedAfterMetadata": True}):
            with self.subTest(options=options):
                result = self.run_fixture(embedded=True, binding=True, wrapperDepth=1, action=True, **options)
                self.assertFalse(result["output"]["performed"])
                self.assertEqual(0, result["presses"])

    def test_hit_operation_rechecks_edges_after_initial_full_chain_proof(self):
        for options in ({"hitEdgeChanged": True}, {"hitRootChanged": True}):
            with self.subTest(options=options):
                result = self.run_fixture(embedded=True, binding=True, action=True, **options)
                self.assertFalse(result["output"]["performed"])
                self.assertEqual("DirectAXEmbeddedStructureChanged", result["output"]["diagnostic"]["code"])
                self.assertEqual(0, result["presses"])


class BindingGuards(unittest.TestCase):
    def setUp(self):
        # This is a synthetic macOS executable, even when the pure test runs
        # on Windows. Only its resolver is mocked; real result directories and
        # the production Unix identity parser retain their normal behavior.
        def paths(value, *parts):
            if not parts and value in (APP["exe"], str(APP["exe"])):
                return SimpleNamespace(resolve=lambda: APP["exe"])
            return Path(value, *parts)
        resolver = patch.object(probe, "Path", side_effect=paths)
        resolver.start()
        self.addCleanup(resolver.stop)

    def test_source_role_requires_specialized_provenance_guard_in_both_native_paths(self):
        app = {**APP, "role": "source-fixture", "exe": Path("/fixture/Bakabase.NativeGui.SourceHost")}
        for module, target in ((probe, probe.hosted), (pid, pid.validate_app)):
            for rejected in (False, True):
                validator = Mock(side_effect=AssertionError("InvalidFixture") if rejected else None)
                fake_module = SimpleNamespace(validate_probe_app=validator)
                spec = SimpleNamespace(loader=SimpleNamespace(exec_module=lambda _: None))
                with self.subTest(module=module.__name__, rejected=rejected), \
                        patch.object(module.base, "require_hosted_runner"), \
                        patch.object(module.importlib.util, "spec_from_file_location", return_value=spec), \
                        patch.object(module.importlib.util, "module_from_spec", return_value=fake_module):
                    if rejected:
                        with self.assertRaisesRegex(AssertionError, "InvalidFixture"):
                            target(app)
                    else:
                        target(app)
                validator.assert_called_once_with(app)

    def test_candidate_requires_all_relations_exact_owned_identity_and_matching_pair(self):
        snapshot = {"ownedOSIdentity": OWNED, "ownedOSIdentityStable": True}
        self.assertEqual(BINDING, probe.binding_from_diagnostic(APP, snapshot, DIAGNOSTIC, PAIR))
        for key in ("edgeStillMatches", "windowStillMatches", "parentMatches", "windowMatches", "actualPidStable"):
            with self.subTest(key=key):
                self.assertIsNone(probe.binding_from_diagnostic(APP, snapshot, {**DIAGNOSTIC, key: False}, PAIR))
        for changed in ({"ownedOSIdentity": {**OWNED, "startMicroseconds": 124}}, {}, {"ownedOSIdentity": EMBEDDED}):
            self.assertIsNone(probe.binding_from_diagnostic(APP, changed, DIAGNOSTIC, PAIR))
        self.assertIsNone(probe.binding_from_diagnostic(APP, snapshot, DIAGNOSTIC, {**PAIR, "observedEpochMs": 102001}))

    def test_pair_observation_checks_both_full_identities_twice_and_rejects_reuse(self):
        with patch.object(pid, "hosted"), patch.object(pid, "sample", side_effect=[EMBEDDED, OWNED, EMBEDDED, OWNED]) as sample:
            result = pid.observe_pair("osx-arm64", 900, 42, 102000)
        self.assertEqual([900, 42, 900, 42], [call.args[0] for call in sample.call_args_list])
        self.assertEqual(OWNED, result["ownedIdentity"])
        for samples in ([EMBEDDED, OWNED, {**EMBEDDED, "startMicroseconds": 124}, OWNED],
                        [EMBEDDED, OWNED, EMBEDDED, {**OWNED, "startMicroseconds": 124}]):
            with patch.object(pid, "hosted"), patch.object(pid, "sample", side_effect=samples):
                with self.assertRaisesRegex(pid.IdentityFailure, "ProcessIdentityChanged"):
                    pid.observe_pair("osx-arm64", 900, 42, 102000)

    def test_read_brackets_native_call_with_exact_pair_and_preserves_deadline(self):
        raw = {"backend": "macos-direct-ax", "readOnly": True, "enabled": True, "truncated": False,
               "windows": [], "embeddedAXProof": PROOF}
        app = {**APP, "embeddedAXBinding": BINDING}
        with patch.object(probe, "hosted"), patch.object(probe, "mac_identity", return_value=LEGACY), \
                patch.object(probe, "collect_pid_identity", return_value=PAIR) as pairs, \
                patch.object(probe, "bounded_command", return_value=copy.deepcopy(raw)) as command, \
                patch.object(probe.time, "monotonic", return_value=100):
            result = probe.native_snapshot(app, 42)
        self.assertEqual(2, pairs.call_count)
        self.assertTrue(all(call.args[2] <= 2 for call in pairs.call_args_list))
        self.assertEqual(25, command.call_args.args[2])
        self.assertIn('"embeddedBinding": {"pid": 900', command.call_args.args[1])
        self.assertEqual(BINDING, result["embeddedAXBinding"])
        self.assertTrue(result["embeddedAXProof"]["osIdentityStable"])

    def test_identity_change_before_read_prevents_native_call_and_after_read_discards_result(self):
        app = {**APP, "embeddedAXBinding": BINDING}
        changed = {**PAIR, "identity": {**EMBEDDED, "startMicroseconds": 124}}
        for sequence, expected_calls in (([changed], 0), ([PAIR, changed], 1)):
            with patch.object(probe, "hosted"), patch.object(probe, "mac_identity", return_value=LEGACY), \
                    patch.object(probe, "collect_pid_identity", side_effect=sequence), \
                    patch.object(probe, "bounded_command", return_value={}) as command:
                with self.assertRaisesRegex(probe.ProbeFailure, "EmbeddedOSIdentityChanged"):
                    probe.native_snapshot(app, 42)
            self.assertEqual(expected_calls, command.call_count)

    def test_binding_action_requires_same_complete_snapshot_and_verified_proof(self):
        snapshot = {"backend": "macos-direct-ax", "enabled": True, "truncated": False, "process": LEGACY,
                    "ownedOSIdentity": OWNED, "ownedOSIdentityStable": True, "embeddedAXBinding": BINDING,
                    "embeddedAXProof": {**PROOF, "osIdentityStable": True}}
        record = {"pid": 42, "operation": "press"}
        with patch.object(probe, "hosted"), patch.object(probe, "direct_execute", return_value={"performed": True}) as execute:
            probe.direct_action({**APP, "embeddedAXBinding": BINDING}, snapshot, record)
        self.assertEqual(15, execute.call_args.args[4])
        self.assertEqual(OWNED, execute.call_args.args[6])
        self.assertEqual({"contentRootRole": "AXWebArea", "contentRootPath": [0, 0], "wrapperChain": []},
                         execute.call_args.args[2]["expectedContentScope"])
        for change in ({"truncated": True}, {"embeddedAXProof": None}, {"embeddedAXBinding": None}):
            with patch.object(probe, "hosted"), patch.object(probe, "direct_execute") as execute:
                with self.assertRaises(probe.ProbeFailure):
                    probe.direct_action({**APP, "embeddedAXBinding": BINDING}, {**snapshot, **change}, record)
            execute.assert_not_called()

    def test_wrapper_diagnostics_are_bounded_and_cannot_turn_partial_proof_into_scope(self):
        raw = {**PROOF, "rootRole": "AXGroup", "contentRootPath": [0, 0, 0], "wrapperChain": [
            {"path": [0, 0], "role": "AXGroup", "childCount": 2, "childrenEvidence": "explicit-array",
             "parentMatches": True, "windowMatches": True, "name": "PRIVATE"}]}
        safe = probe.embedded_proof(raw)
        self.assertNotIn("PRIVATE", json.dumps(safe))
        self.assertIsNone(probe.content_scope(safe))
        raw["wrapperChain"][0]["childCount"] = 1
        self.assertIsNotNone(probe.content_scope(probe.embedded_proof(raw)))
        for change in ({"role": "PRIVATE"}, {"parentMatches": False}, {"windowMatches": False},
                       {"path": [0, 1]}, {"childrenEvidence": "no-value-count-zero"}):
            changed = copy.deepcopy(raw)
            changed["wrapperChain"][0].update(change)
            self.assertIsNone(probe.content_scope(probe.embedded_proof(changed)))
        self.assertEqual({"checks": 16001, "rootProofs": None, "maximumEmbeddedDepth": None},
                         probe.ax_read_counts({"checks": 16001, "rootProofs": "PRIVATE", "maximumEmbeddedDepth": 999}))

    def test_readiness_promotes_binding_only_after_next_complete_read(self):
        app = dict(APP)
        first = {"backend": "macos-direct-ax", "readOnly": True, "enabled": True, "truncated": True, "process": LEGACY,
                 "ownedOSIdentity": OWNED, "ownedOSIdentityStable": True, "windows": [], "pidMismatch": DIAGNOSTIC,
                 "diagnostic": {"code": "DirectAXProcessMismatch"}, "errorStage": "read-tree"}
        second = {**first, "truncated": False, "pidMismatch": None, "diagnostic": None, "errorStage": None,
                  "embeddedAXBinding": BINDING, "embeddedAXProof": {**PROOF, "osIdentityStable": True},
                  "windows": [{"visible": True, "nodes": [{"path": [0, 0, 0], "insideWebContent": True, "visible": True,
                               "enabled": True, "name": "Close", "actions": ["AXPress"]}]}]}
        seen = []
        def read(current, *_args, **_kwargs):
            seen.append(copy.deepcopy(current))
            return first if len(seen) == 1 else second
        with tempfile.TemporaryDirectory() as directory, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", side_effect=read), patch.object(probe, "collect_pid_identity", return_value=PAIR), \
                patch.object(probe.time, "sleep"), patch.object(probe.time, "monotonic", return_value=100):
            result = probe.capture(app, 42, Path(directory)/"report", require_complete=True)
        self.assertNotIn("_embeddedAXCandidate", seen[0])
        self.assertEqual(BINDING, seen[1]["_embeddedAXCandidate"])
        self.assertEqual(BINDING, app["embeddedAXBinding"])
        self.assertNotIn("_embeddedAXCandidate", app)
        self.assertTrue(result["completeTreePassed"])
        self.assertFalse(result["mainFlowPassed"])

    def test_second_unknown_pid_does_not_rebind_and_failed_readiness_discards_candidate(self):
        app = dict(APP)
        first = {"backend": "macos-direct-ax", "readOnly": True, "enabled": True, "truncated": True, "process": LEGACY,
                 "ownedOSIdentity": OWNED, "ownedOSIdentityStable": True, "windows": [], "pidMismatch": DIAGNOSTIC,
                 "diagnostic": {"code": "DirectAXProcessMismatch"}, "errorStage": "read-tree"}
        later = {**first, "pidMismatch": {**DIAGNOSTIC, "actualPid": 901, "origin": "other"}}
        calls = [first] + [later]*9
        with tempfile.TemporaryDirectory() as directory, patch.object(probe, "hosted"), \
                patch.object(probe, "native_snapshot", side_effect=calls), \
                patch.object(probe, "collect_pid_identity", return_value=PAIR) as collect, \
                patch.object(probe.time, "sleep"), patch.object(probe.time, "monotonic", return_value=100):
            result = probe.capture(app, 42, Path(directory)/"report", require_complete=True)
        collect.assert_called_once()
        self.assertNotIn("_embeddedAXCandidate", app)
        self.assertNotIn("embeddedAXBinding", app)
        self.assertFalse(result["completeTreePassed"])

    def test_readiness_absolute_deadline_only_shortens_default(self):
        for deadline, expected_timeout in ((None, 30), (500, 30), (112, 6), (106, None)):
            snapshot = {"backend": "macos-direct-ax", "readOnly": True, "enabled": False, "windows": []}
            with self.subTest(deadline=deadline), tempfile.TemporaryDirectory() as directory, patch.object(probe, "hosted"), \
                    patch.object(probe, "native_snapshot", return_value=snapshot) as read, \
                    patch.object(probe.time, "monotonic", return_value=100):
                result = probe.capture(dict(APP), 42, Path(directory)/"report", require_complete=True, deadline=deadline)
            if expected_timeout is None:
                read.assert_not_called()
                self.assertEqual("NativeUiReadinessTimedOut", result["error"]["code"])
            else:
                self.assertEqual(expected_timeout, read.call_args.kwargs["timeout"])


if __name__ == "__main__":
    unittest.main()
