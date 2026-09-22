#!/usr/bin/env python3
"""Fetch one verified ARM old-data artifact for a hosted Intel restore test.

Only evidence is downloaded. The consumer validates the original installation,
SQLite rows, API semantics and configuration before importing any data.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import zipfile


def sibling(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


inputs = sibling("prepare-installed-inputs")
release = sibling("historical-release")
require = inputs.require
ROOT = inputs.ROOT
ARTIFACT = "macos-data-retention-osx-arm64-evidence"
MAX_ZIP_BYTES = 15 * 1024 * 1024
MAX_EXPANDED_BYTES = 160 * 1024 * 1024
MAX_DATABASE_BYTES = 32 * 1024 * 1024
MAX_JSON_BYTES = 4 * 1024 * 1024
SCOPE = "macos-beta-data-retention-original-executable-seed-native-update"


def validate_run(run, repository, run_id):
    require(type(run.get("id")) is int and run["id"] == run_id, "Producer run ID differs")
    require(run.get("status") == "completed" and run.get("conclusion") in ("success", "failure"),
            "Producer run must be completed with a recorded success or failure")
    require(run.get("repository", {}).get("full_name") == repository and
            run.get("head_repository", {}).get("full_name") == repository, "Producer repository differs")
    require(run.get("path") == ".github/workflows/ci.yml", "Unexpected producer workflow")
    sha = run.get("head_sha", "")
    require(isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{40}", sha), "Invalid producer head SHA")
    return sha


def select_artifact(metadata, run_id, head):
    items, count = metadata.get("artifacts"), metadata.get("total_count")
    require(type(count) is int and 0 <= count <= 100 and isinstance(items, list) and len(items) == count,
            "Producer artifact inventory is incomplete or exceeds 100")
    artifact = inputs.select_artifact(items, ARTIFACT)
    require(artifact["size_in_bytes"] <= MAX_ZIP_BYTES, "Producer evidence ZIP exceeds 15 MiB")
    origin = artifact.get("workflow_run", {})
    require(type(origin.get("id")) is int and origin["id"] == run_id and origin.get("head_sha") == head,
            "Producer artifact workflow run or head SHA differs")
    return artifact


def validate_archive(path):
    # The shared unpacker verifies paths. This evidence-only layer also rejects
    # symlinks/special files and limits expanded bytes before extraction.
    with zipfile.ZipFile(path) as archive:
        entries = inputs.package.archive_entries(archive)
        require(0 < len(entries) <= 1000 and sum(e.file_size for e in entries) <= MAX_EXPANDED_BYTES,
                "Producer evidence expansion exceeds its budget")
        names = set()
        for entry in entries:
            normalized = entry.filename.rstrip("/").casefold()
            require(normalized not in names, "Producer evidence paths collide")
            names.add(normalized)
            kind = stat.S_IFMT(entry.external_attr >> 16)
            require(kind in (0, stat.S_IFREG, stat.S_IFDIR) and not (entry.flag_bits & 1),
                    "Producer evidence must contain only unencrypted regular files/directories")
            require(entry.file_size <= MAX_DATABASE_BYTES, "Producer evidence member exceeds 32 MiB")
        require("historical-results/report.json" in archive.namelist() and
                "verified-producer.json" not in archive.namelist(), "Producer report is missing or verification marker was supplied")


def regular(path, limit):
    require(path.is_file() and not path.is_symlink() and 0 < path.stat().st_size <= limit,
            "Missing or oversized producer evidence file: " + path.name)
    return path


def validate_report(evidence, head):
    report_path = regular(evidence / "historical-results/report.json", MAX_JSON_BYTES)
    report = json.loads(report_path.read_text(encoding="utf-8-sig"))
    require(report.get("executionHeadSHA") == head and report.get("rid") == "osx-arm64" and
            report.get("sourceSHA") == release.CANDIDATE_SHA and
            report.get("candidateCoreVersion") == release.CANDIDATE_CORE and report.get("scope") == SCOPE,
            "Producer execution, source, core or scope differs")
    require(report.get("packageVersion") == release.OLD_VERSION and
            report.get("originalVersions") == {role: release.OLD_VERSION for role in ("client", "unified")},
            "Producer did not observe both original 349 products")
    seed = report.get("dataSeed", {})
    require(seed.get("passed") is True and seed.get("oldVersion") == release.OLD_VERSION and
            seed.get("oldSourceSHA") == release.RELEASE_SHA, "Producer original data seed did not pass")
    baseline = report.get("originalDatabaseBackup", {})
    require(baseline.get("file") == "before-update.sqlite" and baseline.get("integrity") == "ok" and
            baseline.get("foreignKeyViolations") == [], "Producer original SQLite baseline is missing or invalid")
    database = regular(evidence / "historical-results/unified/before-update.sqlite", MAX_DATABASE_BYTES)
    require(type(baseline.get("sizeBytes")) is int and database.stat().st_size == baseline["sizeBytes"] and
            inputs.package.sha256(database) == baseline.get("sha256"), "Producer baseline SQLite bytes differ")
    regular(evidence / "historical-results/unified/before-tables.json", MAX_JSON_BYTES)
    require(set(report.get("originalConfigurationBackups", {})) == {"client", "unified"},
            "Producer original configuration backups are missing")
    # Overall failure is allowed: another architecture or a later update may
    # have failed after this original baseline was captured. Never promote it.
    return report, inputs.package.sha256(report_path)


def prepare(repository, run_id, rid, output_directory):
    require(rid == "osx-x64", "Original ARM data restore inputs are only for Intel macOS")
    release.hosted(rid)  # Before filesystem/native/GitHub operations.
    require(repository == release.REPOSITORY, "Unexpected producer repository")
    require(isinstance(run_id, str) and re.fullmatch(r"[1-9][0-9]*", run_id), "Invalid producer run ID")
    output = Path(output_directory)
    runner = Path(os.environ["RUNNER_TEMP"]).resolve()
    require(output.is_absolute() and output == output.resolve() and output.is_relative_to(runner) and output != runner,
            "Restore inputs must be inside RUNNER_TEMP without symlinks")
    require(runner.is_dir() and not output.exists() and not output.is_symlink(), "Restore inputs must use a new directory")
    require(not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=no"], cwd=ROOT),
            "Restore input preparation requires a clean tracked checkout")
    run = inputs.gh_json(f"repos/{repository}/actions/runs/{run_id}")
    head = validate_run(run, repository, int(run_id))
    metadata = inputs.gh_json(f"repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100")
    artifact = select_artifact(metadata, int(run_id), head)
    output.mkdir(parents=True)
    evidence = output / "evidence"
    provenance = {"passed": False, "repository": repository, "runId": int(run_id), "headSHA": head,
                  "runConclusion": run["conclusion"], "producerRid": "osx-arm64", "consumerRid": rid,
                  "productSourceSHA": release.CANDIDATE_SHA, "artifact": {
                      "id": artifact["id"], "name": ARTIFACT, "sizeBytes": artifact["size_in_bytes"],
                      "digest": artifact["digest"]}}
    try:
        archive = output / "producer-evidence.zip"
        inputs.download(repository, artifact, archive)  # One exact artifact, no fallback/retry.
        actual = inputs.verify_archive(archive, artifact)
        validate_archive(archive)
        inputs.package.unpack(archive, evidence)
        report, report_sha = validate_report(evidence, head)
        marker = {"verified": True, "area": "macos-data", "rid": "osx-arm64", "runId": int(run_id),
                  "headSHA": head, "artifactSHA256": actual.removeprefix("sha256:"), "reportSHA256": report_sha}
        with (evidence / "verified-producer.json").open("x") as stream:
            json.dump(marker, stream, indent=2)
            stream.write("\n")
        provenance.update(passed=True, reportSHA256=report_sha, producerDirectory=str(evidence),
                          producerOutcome={"passed": report.get("passed"), "dataRetentionPassed": report.get("dataRetentionPassed"),
                                           "currentStage": report.get("currentStage"), "originalSeedPassed": True})
    finally:
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    return evidence, output / "provenance.json"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--rid", choices=("osx-x64",), required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    args = parser.parse_args()
    evidence, provenance = prepare(args.repository, args.run_id, args.rid, args.output_directory)
    values = {"producer_directory": str(evidence), "provenance": str(provenance)}
    require(all("\n" not in value and "\r" not in value for value in values.values()), "Invalid output path")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as stream:
            for key, value in values.items():
                stream.write(key + "=" + value + "\n")
    print(json.dumps(values))


if __name__ == "__main__":
    main()
