#!/usr/bin/env python3
"""Read-only exit evidence for an installed product's already bound renderer.

Arming requires the live complete-tree binding. The caller stops the product
normally, then invokes wait_exit before removing either installed fixture.
Unknown renderers are not assumed absent; this module never sends a signal.
"""
import copy
import importlib.util
import math
from pathlib import Path
import time

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("installed_renderer_source_helpers", HERE / "source_fixture.py")
source = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(source)
SCOPE = "installed-product-bound-renderer-exit-only"
MAX_WAIT_SECONDS = 30


class RendererCleanupFailure(AssertionError):
    pass


def require(condition, code):
    if not condition:
        raise RendererCleanupFailure(code)


def binding_for(app):
    module = source.pid_module()
    try:
        # The hosted guard precedes paths, bindings, process or filesystem reads.
        module.hosted(app.get("rid"))
        require(app.get("role") in ("unified", "client") and
                app.get("nativeBackend") == "macos-direct-ax", "RendererCleanupUnsupportedApp")
        module.validate_app(app)
        pid = app.get("nativeGuiObservedPid")
        require(type(pid) is int and 0 < pid <= 2**31-1, "RendererCleanupObservedPidMissing")
        binding = source.verified_embedded_binding(app, pid)
        require(binding is not None and app.get("_embeddedAXCandidate") is None,
                "RendererCleanupBindingMissing")
        return binding
    except RendererCleanupFailure:
        raise
    except Exception:
        raise RendererCleanupFailure("RendererCleanupBindingUnavailable") from None


def arm(app):
    """Before product stop: confirm both exact OS identities are still alive."""
    binding = binding_for(app)
    try:
        observed = source.pid_module().capture(app, {
            "origin": "owned-child-edge", "expectedPid": binding["application"]["pid"],
            "actualPid": binding["embedded"]["pid"], "observedEpochMs": binding["observedEpochMs"]}, 2)
        require(observed.get("code") == "ObservedStable" and observed.get("stable") is True and
                observed.get("identity") == binding["embedded"] and
                observed.get("ownedIdentity") == binding["application"], "RendererCleanupIdentityChangedBeforeStop")
    except RendererCleanupFailure:
        raise
    except Exception:
        raise RendererCleanupFailure("RendererCleanupArmUnavailable") from None
    return {"schemaVersion": 1, "armed": True, "scope": SCOPE, "role": app["role"], "rid": app["rid"],
            "observedPid": app["nativeGuiObservedPid"], "binding": copy.deepcopy(binding)}


def exit_observation(app, original, deadline):
    remaining = deadline - time.monotonic()
    require(remaining > 0, "RendererCleanupDeadlineExceeded")
    try:
        # This existing helper proves absence with two exact-PID ps queries,
        # or returns two stable libproc identity samples. It never signals.
        current = source.capture_embedded_process(app, original["pid"], min(2, remaining))
        require(time.monotonic() < deadline, "RendererCleanupDeadlineExceeded")
        if current.get("code") == "ProcessAbsent":
            require(current.get("identity") is None, "RendererCleanupObservationUncertain")
            return {"exited": True, "outcome": "ProcessAbsent", "pid": original["pid"]}
        require(current.get("code") == "ObservedStable", "RendererCleanupObservationUncertain")
        identity = source.pid_module().identity(current.get("identity"), original["pid"])
        if any(identity[key] != original[key] for key in ("startSeconds", "startMicroseconds")):
            return {"exited": True, "outcome": "PidReused", "pid": original["pid"],
                    "replacementUntouched": True}
        require(source.micro_identity(identity) == source.micro_identity(original), "RendererCleanupIdentityChanged")
        return {"exited": False, "outcome": "OriginalProcessStillPresent", "pid": original["pid"]}
    except RendererCleanupFailure:
        raise
    except Exception:
        raise RendererCleanupFailure("RendererCleanupObservationUncertain") from None


def wait_exit(app, proof, deadline):
    """After product stop: bounded observation, never a process cleanup action."""
    began = time.monotonic()
    binding = binding_for(app)
    require(type(deadline) in (int, float) and math.isfinite(deadline), "RendererCleanupInvalidDeadline")
    deadline = min(deadline, began + MAX_WAIT_SECONDS)
    require(isinstance(proof, dict) and type(proof.get("schemaVersion")) is int and
            proof["schemaVersion"] == 1 and proof.get("armed") is True and proof.get("scope") == SCOPE and
            proof.get("role") == app["role"] and proof.get("rid") == app["rid"] and
            proof.get("observedPid") == app["nativeGuiObservedPid"] and proof.get("binding") == binding,
            "RendererCleanupProofChanged")
    parent = exit_observation(app, binding["application"], deadline)
    require(parent["exited"], "RendererCleanupParentStillPresent")
    observations = 1
    while True:
        renderer = exit_observation(app, binding["embedded"], deadline)
        observations += 1
        if renderer["exited"]:
            require(time.monotonic() < deadline, "RendererCleanupDeadlineExceeded")
            return {"passed": True, "scope": SCOPE, "readOnly": True, "signalSent": False,
                    "applicationExit": parent, "embeddedProcessExit": renderer,
                    "observations": observations, "elapsedSeconds": time.monotonic() - began,
                    "remainingProcesses": []}
        remaining = deadline - time.monotonic()
        require(remaining > 0, "RendererCleanupDeadlineExceeded")
        time.sleep(min(0.2, remaining))
