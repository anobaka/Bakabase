#!/usr/bin/env python3
"""Verify and reuse native packages from one successful acceptance run."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("package_acceptance", Path(__file__).with_name("run-package-acceptance.py"))
package = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(package)
LIMIT = 1024 ** 3
FLOOR = 5 * 1024 ** 3
WORKFLOWS = {".github/workflows/ci.yml", ".github/workflows/_package_acceptance.yml",
             ".github/workflows/_installed_lifecycle.yml"}


def require(value, message):
    if not value:
        raise ValueError(message)


def gh_json(path):
    result = subprocess.run(["gh", "api", path], capture_output=True, timeout=90)
    require(result.returncode == 0, "GitHub metadata request failed")
    return json.loads(result.stdout)


def validate_run(run, repository):
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
            "Package run must have completed successfully")
    require(run.get("head_repository", {}).get("full_name") == repository and
            run.get("repository", {}).get("full_name") == repository, "Package run belongs to a different repository")
    require(run.get("path") == ".github/workflows/ci.yml", "Unexpected source workflow")
    sha = run.get("head_sha", "")
    require(re.fullmatch(r"[0-9a-f]{40}", sha), "Package source commit is invalid")
    return sha


def validate_changes(paths):
    rejected = [path for path in paths if not path.startswith(("docs/", "src/tests/")) and path not in WORKFLOWS]
    require(not rejected, "Product or package sources changed; rebuild native packages before reuse")


def select_artifact(items, name):
    found = [item for item in items if item.get("name") == name]
    require(len(found) == 1, "Expected exactly one artifact: " + name)
    artifact = found[0]
    require(artifact.get("expired") is False, "Required package artifact has expired")
    require(type(artifact.get("id")) is int and artifact["id"] > 0, "Invalid artifact ID")
    require(type(artifact.get("size_in_bytes")) is int and 0 < artifact["size_in_bytes"] <= LIMIT,
            "Artifact exceeds the download budget")
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", artifact.get("digest") or ""), "Artifact digest is missing or invalid")
    return artifact


def verify_archive(path, artifact):
    require(path.stat().st_size == artifact["size_in_bytes"], "Downloaded artifact size differs")
    actual = "sha256:" + package.sha256(path)
    require(actual == artifact["digest"], "Downloaded artifact digest differs")
    return actual


def download(repository, artifact, path):
    require(shutil.disk_usage(path.parent).free > FLOOR + artifact["size_in_bytes"], "Insufficient space for package artifacts")
    with path.open("xb") as stream:
        child = subprocess.Popen(["gh", "api", f'repos/{repository}/actions/artifacts/{artifact["id"]}/zip'],
                                 stdout=stream, stderr=subprocess.DEVNULL)
        deadline = time.monotonic() + 240
        try:
            while child.poll() is None:
                require(time.monotonic() < deadline, "Package artifact download timed out")
                require(path.stat().st_size <= artifact["size_in_bytes"], "Package download exceeded its declared size")
                require(shutil.disk_usage(path.parent).free >= FLOOR, "Disk floor reached during artifact download")
                time.sleep(0.25)
            require(child.returncode == 0, "GitHub artifact download failed")
        finally:
            if child.poll() is None:
                child.kill()
            child.wait(timeout=10)
    return verify_archive(path, artifact)


def validate_report(report, sha, role, rid):
    require(report.get("passed") is True and report.get("headSHA") == sha, "Evidence is failed or from another commit")
    require(report.get("role") == role and report.get("rid") == rid, "Evidence product or architecture differs")
    require(report.get("ownedFilesRemoved") is True and report.get("cleanupErrors") == [], "Source acceptance cleanup did not pass")
    version = report.get("version", "")
    require(re.fullmatch(r"0\.0\.1-acceptance\.[0-9]+\.[0-9]+", version), "Expected a synthetic acceptance package version")
    return version


def verify_package_files(directory, report):
    artifacts = report["packages"]["artifacts"]
    require(set(artifacts) == {"portable", "full", "installer"}, "Incomplete native package evidence")
    checked = {}
    for kind, expected in artifacts.items():
        name = expected.get("file", "")
        require(name and name not in (".", "..") and not any(char in name for char in "/\\:\x00\r\n"),
                "Unsafe package artifact filename")
        path = directory / name
        require(path.is_file() and not path.is_symlink(), "Expected an extracted regular package file")
        require(path.stat().st_size == expected.get("sizeBytes"), "Native package size differs from verified evidence")
        actual = package.sha256(path)
        require(actual == expected.get("sha256"), "Native package hash differs from verified evidence")
        checked[kind] = {"file": name, "sizeBytes": path.stat().st_size, "sha256": actual}
    return checked


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--rid", choices=("win-x64", "osx-x64", "osx-arm64"), required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    args = parser.parse_args()
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", args.repository), "Invalid repository")
    require(re.fullmatch(r"[1-9][0-9]*", args.run_id), "Invalid package run ID")
    output = args.output_directory.resolve()
    require(not output.exists(), "Package inputs must use a new directory")
    require(not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=no"], cwd=ROOT),
            "Package reuse requires a clean tracked checkout")
    run = gh_json(f"repos/{args.repository}/actions/runs/{args.run_id}")
    source = validate_run(run, args.repository)
    require(subprocess.run(["git", "merge-base", "--is-ancestor", source, "HEAD"], cwd=ROOT).returncode == 0,
            "Package commit must be an ancestor of this test checkout")
    changes = subprocess.check_output(["git", "diff", "--name-only", source, "HEAD"], cwd=ROOT, text=True).splitlines()
    validate_changes(changes)
    metadata = gh_json(f"repos/{args.repository}/actions/runs/{args.run_id}/artifacts?per_page=100")
    require(metadata["total_count"] <= 100, "Artifact inventory exceeds the bounded run contract")
    selected = {(role, kind): select_artifact(metadata["artifacts"], f"package-acceptance-{role}-{args.rid}-{kind}")
                for role in ("unified", "client") for kind in ("evidence", "packages")}
    output.mkdir(parents=True)
    provenance = {"passed": False, "repository": args.repository, "runID": int(args.run_id),
                  "packageSourceSHA": source,
                  "testSourceSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                  "unchangedProductSources": True, "changedPaths": changes, "rid": args.rid, "roles": {}}
    try:
        for role in ("unified", "client"):
            entry = {"artifacts": {}}
            provenance["roles"][role] = entry
            for kind in ("evidence", "packages"):
                artifact = selected[role, kind]
                archive = output / f"{role}-{kind}.zip"
                digest = download(args.repository, artifact, archive)
                extracted = output / f"{role}-{kind}"
                package.unpack(archive, extracted)
                archive.unlink()
                entry["artifacts"][kind] = {"id": artifact["id"], "sizeBytes": artifact["size_in_bytes"],
                                            "digest": digest, "directory": str(extracted)}
                if kind == "evidence":
                    reports = list(extracted.rglob("report.json"))
                    require(len(reports) == 1, "Expected one verified native acceptance report")
                    report = json.loads(reports[0].read_text())
                    entry["version"] = validate_report(report, source, role, args.rid)
                else:
                    entry["packageFiles"] = verify_package_files(extracted, report)
            print("Verified native packages: " + role + " / " + args.rid, flush=True)
        require(provenance["roles"]["unified"]["version"] == provenance["roles"]["client"]["version"], "Product fixture versions differ")
        provenance["version"] = provenance["roles"]["unified"]["version"]
        provenance["passed"] = True
    finally:
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    if os.environ.get("GITHUB_OUTPUT"):
        values = {"version": provenance["version"], "provenance": str(output / "provenance.json"),
                  **{role + "_directory": str(output / (role + "-packages")) for role in ("unified", "client")}}
        require(all("\n" not in value and "\r" not in value for value in values.values()), "Invalid output path")
        with open(os.environ["GITHUB_OUTPUT"], "a") as stream:
            for key, value in values.items():
                stream.write(key + "=" + value + "\n")


if __name__ == "__main__":
    main()
