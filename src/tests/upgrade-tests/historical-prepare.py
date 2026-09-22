#!/usr/bin/env python3
"""Verify published v349 installers and repack unchanged candidate code above them.

Only disposable matching GitHub-hosted native runners may download or expand
the original assets. No installer, application or authorization UI is run here.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import time
import uuid

SPEC = importlib.util.spec_from_file_location("historical_release", Path(__file__).with_name("historical-release.py"))
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
repack, require = release.repack, release.require


def combine(prepared, old_audit, packages):
    """Keep candidate provenance distinct from the actual original installation."""
    require(prepared["payloadUnchanged"] is True, "Candidate repack payload changed")
    require(prepared["bundleName"] == old_audit["bundleName"], "Historical and candidate application bundle names differ")
    require(prepared["marker"]["oldVersion"] == release.OLD_VERSION, "Marker must identify the real installed version")
    result = dict(prepared)
    result["candidateSource"] = {"manifest": prepared["oldManifest"], "payloadHashes": prepared["oldPayloadHashes"],
                                 "audit": prepared["sourceAudit"], "fullPackage": prepared["oldFullPackage"],
                                 "checksums": {kind: prepared["packageChecksums"][kind] for kind in ("oldFull", "oldPortable")}}
    result["candidatePayloadUnchanged"] = result.pop("payloadUnchanged")
    result.pop("sourceAudit")
    result.pop("oldFullPackage")
    result["packageChecksums"] = {key: value for key, value in prepared["packageChecksums"].items() if key.startswith("new")}
    result["oldManifest"] = old_audit["manifest"]
    result["oldPayloadHashes"] = old_audit["payloadHashes"]
    result["historicalAudit"] = old_audit
    result["historicalPackages"] = str(packages.resolve())
    result["expectedRunningVersion"] = release.CANDIDATE_CORE
    generated = set(prepared["vendorGenerated"]["allowedNames"])
    result["vendorGenerated"] = dict(prepared["vendorGenerated"],
                                     old={k: v for k, v in old_audit["payloadHashes"].items() if k in generated})
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unified-packages", type=Path, required=True)
    parser.add_argument("--client-packages", type=Path, required=True)
    parser.add_argument("--rid", choices=release.RIDS, required=True)
    parser.add_argument("--candidate-version", required=True)
    parser.add_argument("--new-version", required=True)
    parser.add_argument("--provenance", type=Path, required=True)
    parser.add_argument("--vpk", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=1200)
    args = parser.parse_args(argv)
    release.hosted(args.rid)
    release.validate_versions(args.candidate_version, args.new_version)
    require(0 < args.timeout <= 1800, "Preparation deadline must be within 1..1800 seconds")
    output = args.output_directory.resolve()
    require(not output.exists() and not args.output_directory.is_symlink() and
            output.is_relative_to(Path(os.environ["RUNNER_TEMP"]).resolve()), "Output must be a new directory under RUNNER_TEMP")
    packages = {role: getattr(args, role + "_packages").resolve() for role in release.ROLES}
    require(all(path.is_dir() for path in packages.values()) and len(set(packages.values())) == 2 and
            not any(output.is_relative_to(path) or path.is_relative_to(output) for path in packages.values()),
            "Candidate product directories must exist, be distinct and not overlap output")
    require(args.provenance.is_file() and args.provenance.stat().st_size <= 1024 * 1024, "Candidate provenance is missing or oversized")
    provenance = json.loads(args.provenance.read_text())
    source = release.candidate_provenance(provenance, args.rid, args.candidate_version)
    args.vpk = args.vpk.resolve()
    require(args.vpk.is_file(), "Pinned vpk tool is missing")
    args.old_version, args.original_installed_version = args.candidate_version, release.OLD_VERSION
    args.provenance_data = provenance
    output.mkdir(parents=True)
    candidate = output / "candidate"
    candidate.mkdir()
    started, deadline, nonce = time.monotonic(), time.monotonic() + args.timeout, uuid.uuid4().hex
    report = {"format": "bakabase-historical-upgrade-v1", "passed": False, "status": "running", "rid": args.rid,
              "oldVersion": release.OLD_VERSION, "candidateVersion": args.candidate_version, "newVersion": args.new_version,
              "sourceSHA": source, "provenance": provenance, "roles": {},
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repack.acceptance.ROOT, text=True).strip(),
              "historicalRelease": {"id": release.RELEASE_ID, "tag": release.TAG, "commit": release.RELEASE_SHA},
              "scope": "Original published installer and original updater to changed candidate product code; synthetic target package version"}
    try:
        repack.check_budget(output, deadline)
        metadata = subprocess.run(["gh", "api", f"repos/{release.REPOSITORY}/releases/tags/{release.TAG}"],
                                  capture_output=True, timeout=90, check=True).stdout
        require(len(metadata) <= 1024 * 1024, "Release metadata exceeds budget")
        value = json.loads(metadata)
        selected = release.validate_release(value, args.rid)
        # Keep only identity and asset metadata, never HTTP credentials or headers.
        (output / "release-metadata.json").write_text(json.dumps(value, indent=2) + "\n")
        log = output / "vpk-version.log"
        repack.run_command([args.vpk, "--help"], log, deadline)
        require(log.stat().st_size < 65536, "Unexpectedly large vpk help output")
        report["vpkVersion"] = repack.validate_vpk_version(log.read_text(encoding="utf-8-sig"))
        for role in release.ROLES:
            report["currentStage"] = "download-original-" + role
            original = output / (role + "-historical-packages")
            original.mkdir()
            for expected in selected[role].values():
                release.download(expected, original / expected["file"], args.rid, deadline)
            report["currentStage"] = "audit-original-" + role
            old_audit = release.audit_released(original, role, args.rid, output, deadline)
            report["currentStage"] = "repack-candidate-" + role
            prepared = repack.prepare_role(role, packages[role], candidate, args, source, nonce, deadline)
            report["roles"][role] = combine(prepared, old_audit, original)
        repack.check_budget(output, deadline)
        report.update(passed=True, status="passed", currentStage="prepared")
        release.validate_preparation(report, args.rid)
    except (Exception, KeyboardInterrupt) as error:
        report.update(passed=False, status="failed", error={"type": type(error).__name__, "message": str(error)[:2000]})
        if isinstance(error, repack.PayloadMismatch):
            report["error"]["payloadDifferences"] = error.differences
    finally:
        cleanup_errors = []
        for role in release.ROLES:
            for path in (output / (role + "-historical-expanded"), *(candidate / role / name for name in ("source", "payload", "verified"))):
                try:
                    if path.exists():
                        shutil.rmtree(path)
                except OSError as error:
                    cleanup_errors.append({"path": str(path), "type": type(error).__name__})
        report["temporaryCopyCleanupErrors"] = cleanup_errors
        if cleanup_errors:
            report.update(passed=False, status="failed")
        report["elapsedSeconds"] = round(time.monotonic() - started, 2)
        manifest = output / "historical.json"
        manifest.write_text(json.dumps(report, indent=2) + "\n")
        print(("PASS" if report["passed"] else "FAIL") + ": " + str(manifest), flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f"Interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
