"""Synthetic exact-PID cleanup evidence only; no native OS/UI observations."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("renderer_cleanup_pure", HERE / "native_renderer_cleanup.py")
cleanup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cleanup)


class RendererCleanupTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        executable = Path(temporary.name).resolve() / "Bakabase"
        executable.write_bytes(b"synthetic fixture executable; never launched")
        self.app = {"role": "unified", "rid": "osx-arm64", "exe": executable,
                    "nativeBackend": "macos-direct-ax", "nativeGuiObservedPid": 101}
        application = {"pid": 101, "ppid": 1, "uid": 501, "executable": str(executable),
                       "startSeconds": 100, "startMicroseconds": 1}
        renderer = dict(application, pid=201, executable="/synthetic/embedded", startMicroseconds=2)
        self.binding = {"schemaVersion": 1, "initialRelationsVerified": True, "rootPath": [0, 0, 1],
                        "parentChildCount": 2, "observedEpochMs": 101000,
                        "application": application, "embedded": renderer}
        self.app["embeddedAXBinding"] = copy.deepcopy(self.binding)
        self.module = cleanup.source.pid_module()
        self.hosted = patch.object(self.module, "hosted")
        self.hosted.start()
        self.addCleanup(self.hosted.stop)
        module = patch.object(cleanup.source, "pid_module", return_value=self.module)
        module.start()
        self.addCleanup(module.stop)
        self.paired = {"code": "ObservedStable", "stable": True,
                       "identity": renderer, "ownedIdentity": application}

    def arm(self):
        with patch.object(self.module, "capture", return_value=self.paired) as captured:
            proof = cleanup.arm(self.app)
        self.assertEqual(2, captured.call_args.args[2])
        return proof

    def test_hosted_guard_precedes_binding_and_native_queries(self):
        with patch.object(self.module, "hosted", side_effect=AssertionError("not hosted")), \
                patch.object(self.module, "capture") as capture, \
                patch.object(cleanup.source, "verified_embedded_binding") as binding:
            with self.assertRaises(cleanup.RendererCleanupFailure): cleanup.arm(self.app)
        capture.assert_not_called()
        binding.assert_not_called()

    def test_live_arm_requires_exact_pair_and_observed_parent_pid(self):
        for key in ("identity", "ownedIdentity"):
            raw = copy.deepcopy(self.paired)
            raw[key]["startMicroseconds"] += 1
            with self.subTest(key=key), patch.object(self.module, "capture", return_value=raw), \
                    self.assertRaisesRegex(cleanup.RendererCleanupFailure, "IdentityChangedBeforeStop"):
                cleanup.arm(self.app)
        self.app["nativeGuiObservedPid"] = 999
        with patch.object(self.module, "capture") as captured, self.assertRaises(cleanup.RendererCleanupFailure):
            cleanup.arm(self.app)
        captured.assert_not_called()

    def test_missing_or_incomplete_binding_never_means_not_applicable(self):
        self.app.pop("embeddedAXBinding")
        for candidate in (None, self.binding):
            self.app["_embeddedAXCandidate"] = candidate
            with self.subTest(candidate=bool(candidate)), patch.object(self.module, "capture") as captured, \
                    self.assertRaises(cleanup.RendererCleanupFailure):
                cleanup.arm(self.app)
            captured.assert_not_called()

    def test_arming_copies_proof_and_failure_never_arms(self):
        proof = self.arm()
        self.app["embeddedAXBinding"]["embedded"]["startMicroseconds"] += 1
        self.assertEqual(2, proof["binding"]["embedded"]["startMicroseconds"])
        for raw in ({"code": "DiagnosticTimedOut", "stable": False}, dict(self.paired, stable=False)):
            with self.subTest(raw=raw), patch.object(self.module, "capture", return_value=raw), \
                    self.assertRaises(cleanup.RendererCleanupFailure): cleanup.arm(self.app)

    def test_absence_reports_only_exact_two_bound_pids_and_never_signals(self):
        proof = self.arm()
        with patch.object(cleanup.source, "capture_embedded_process", return_value={"code": "ProcessAbsent", "identity": None}) as observed, \
                patch.object(cleanup.source.os, "kill") as kill, patch.object(cleanup.time, "monotonic", return_value=10):
            result = cleanup.wait_exit(self.app, proof, 40)
        self.assertTrue(result["passed"])
        self.assertTrue(result["readOnly"])
        self.assertFalse(result["signalSent"])
        self.assertEqual([], result["remainingProcesses"])
        self.assertEqual([101, 201], [call.args[1] for call in observed.call_args_list])
        kill.assert_not_called()

    def test_pid_reuse_proves_original_exit_but_does_not_touch_replacement(self):
        proof = self.arm()
        def replacement(_app, pid, _timeout):
            original = self.binding["application" if pid == 101 else "embedded"]
            return {"code": "ObservedStable", "identity": dict(original, startMicroseconds=900)}
        with patch.object(cleanup.source, "capture_embedded_process", side_effect=replacement), \
                patch.object(cleanup.source.os, "kill") as kill, patch.object(cleanup.time, "monotonic", return_value=10):
            result = cleanup.wait_exit(self.app, proof, 40)
        for item in (result["applicationExit"], result["embeddedProcessExit"]):
            self.assertEqual("PidReused", item["outcome"])
            self.assertTrue(item["replacementUntouched"])
        kill.assert_not_called()

    def test_live_parent_blocks_renderer_wait(self):
        proof = self.arm()
        with patch.object(cleanup.source, "capture_embedded_process", return_value={"code": "ObservedStable", "identity": self.binding["application"]}) as observed, \
                patch.object(cleanup.time, "monotonic", return_value=10), \
                self.assertRaisesRegex(cleanup.RendererCleanupFailure, "ParentStillPresent"):
            cleanup.wait_exit(self.app, proof, 40)
        observed.assert_called_once()

    def test_uncertainty_identity_changes_and_binding_changes_fail_closed(self):
        proof = self.arm()
        absent = {"code": "ProcessAbsent", "identity": None}
        for raw in ({"code": "ObservationUncertain", "identity": None},
                    {"code": "ProcessAbsent", "identity": {}},
                    {"code": "ObservedStable", "identity": dict(self.binding["embedded"], uid=502)}):
            with self.subTest(raw=raw), patch.object(cleanup.source, "capture_embedded_process", side_effect=[absent, raw]), \
                    patch.object(cleanup.time, "monotonic", return_value=10), self.assertRaises(cleanup.RendererCleanupFailure):
                cleanup.wait_exit(self.app, proof, 40)
        self.app["embeddedAXBinding"]["rootPath"] = [0, 0, 0]
        with patch.object(cleanup.source, "capture_embedded_process") as observed, \
                self.assertRaisesRegex(cleanup.RendererCleanupFailure, "ProofChanged"):
            cleanup.wait_exit(self.app, proof, cleanup.time.monotonic()+30)
        observed.assert_not_called()

    def test_reparented_renderer_can_exit_without_being_signalled(self):
        proof = self.arm()
        absent = {"code": "ProcessAbsent", "identity": None}
        running = {"code": "ObservedStable", "identity": dict(self.binding["embedded"], ppid=999)}
        with patch.object(cleanup.source, "capture_embedded_process", side_effect=[absent, running, absent]), \
                patch.object(cleanup.time, "monotonic", return_value=10), patch.object(cleanup.time, "sleep"):
            result = cleanup.wait_exit(self.app, proof, 40)
        self.assertEqual(3, result["observations"])

    def test_wait_respects_both_caller_deadline_and_thirty_second_cap(self):
        proof = self.arm()
        for supplied, expected in ((5.0, 5.0), (300.0, 30.0)):
            clock = [0.0]
            def observe(_app, pid, timeout):
                self.assertGreater(timeout, 0)
                self.assertLessEqual(timeout, 2)
                if pid == 101: return {"code": "ProcessAbsent", "identity": None}
                clock[0] += timeout
                return {"code": "ObservedStable", "identity": self.binding["embedded"]}
            def sleep(seconds): clock[0] += seconds
            with self.subTest(supplied=supplied), patch.object(cleanup.time, "monotonic", side_effect=lambda: clock[0]), \
                    patch.object(cleanup.time, "sleep", side_effect=sleep), \
                    patch.object(cleanup.source, "capture_embedded_process", side_effect=observe), \
                    self.assertRaisesRegex(cleanup.RendererCleanupFailure, "DeadlineExceeded"):
                cleanup.wait_exit(self.app, proof, supplied)
            self.assertAlmostEqual(expected, clock[0])

    def test_late_absence_or_helper_timeout_cannot_pass(self):
        proof = self.arm()
        clock = [0.0]
        def late(*_):
            clock[0] = 31.0
            return {"code": "ProcessAbsent", "identity": None}
        with patch.object(cleanup.time, "monotonic", side_effect=lambda: clock[0]), \
                patch.object(cleanup.source, "capture_embedded_process", side_effect=late), \
                self.assertRaisesRegex(cleanup.RendererCleanupFailure, "DeadlineExceeded"):
            cleanup.wait_exit(self.app, proof, 300)
        with patch.object(cleanup.source, "capture_embedded_process", side_effect=TimeoutError("private error")), \
                self.assertRaisesRegex(cleanup.RendererCleanupFailure, "^RendererCleanupObservationUncertain$"):
            cleanup.wait_exit(self.app, proof, cleanup.time.monotonic()+30)


if __name__ == "__main__":
    unittest.main()
