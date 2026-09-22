#!/usr/bin/env python3
"""Fetch pinned first-party mpv CI builds on disposable native runners only."""
import argparse
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import platform
import subprocess
import tarfile

HERE = Path(__file__).resolve().parent


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


base = module("player_package_base", HERE.parent / "upgrade-tests/run-package-acceptance.py")
inputs = module("player_package_inputs", HERE.parent / "upgrade-tests/prepare-installed-inputs.py")
PINNED = {
    "win-x64": (579992687, 28412973, "3a8c74b0e6d794f6ad70ff49ffe0ac1cdaf0abd17f8a25e95b9446f578e7a40b", "x86_64-pc-windows-msvc"),
    "osx-x64": (579992657, 52294445, "008702680e522b77da8e4705e70424f05fc5f78b550c01187c4e1961a40e3592", "macos-15-intel"),
    "osx-arm64": (579992593, 47535265, "d83a063a1315cd5c241e1c8b6b5c963e03819d696dd363078a497bcfea32aea7", "macos-15-arm"),
}
PINNED_ARTIFACTS = {"win-x64": 10667534204, "osx-x64": 10667436118, "osx-arm64": 10667356116}
UPSTREAM_REPOSITORY_ID = 6201092
UPSTREAM_HEAD_SHA = "c6c4c38d7f4aa82ad2a29a4c3b142f19123f5b9e"
UPSTREAM_RUN = 35659722192


def validate_asset(value, rid):
    identifier, size, digest, suffix = PINNED[rid]
    name = f"mpv-v0.41.0-dev-gc6c4c38d7-35659722192-{suffix}.zip"
    base.require(value.get("id") == identifier and value.get("size") == size and
                 value.get("digest") == "sha256:" + digest and value.get("name") == name,
                 "mpv asset differs from the pinned first-party CI build")
    return name


def github_metadata(endpoint, allow_not_found=False):
    try:
        output = subprocess.check_output(["gh", "api", endpoint], stderr=subprocess.PIPE, timeout=30)
    except subprocess.CalledProcessError as error:
        # A missing rolling release attachment may still have its original
        # immutable Actions artifact. No other status permits a fallback.
        try:
            document = json.loads(error.output or b"")
        except (ValueError, TypeError):
            document = None
        if allow_not_found and isinstance(document, dict) and document.get("message") == "Not Found" and \
                document.get("status") in (404, "404"):
            return None
        raise
    return json.loads(output)


def validate_artifact(value, rid):
    _, size, digest, suffix = PINNED[rid]
    name = f"mpv-v0.41.0-dev-gc6c4c38d7-{UPSTREAM_RUN}-{suffix}"
    workflow = value.get("workflow_run") or {}
    base.require(value.get("id") == PINNED_ARTIFACTS[rid] and value.get("name") == name and
                 value.get("size_in_bytes") == size and value.get("digest") == "sha256:" + digest and
                 value.get("expired") is False and workflow.get("id") == UPSTREAM_RUN and
                 workflow.get("repository_id") == UPSTREAM_REPOSITORY_ID and
                 workflow.get("head_repository_id") == UPSTREAM_REPOSITORY_ID and
                 workflow.get("head_sha") == UPSTREAM_HEAD_SHA,
                 "mpv fallback artifact differs from the original pinned repository/build/bytes")
    return name + ".zip"


def select_source(rid):
    endpoint = f"repos/mpv-player/mpv/releases/assets/{PINNED[rid][0]}"
    metadata = github_metadata(endpoint, allow_not_found=True)
    if metadata is not None:
        return validate_asset(metadata, rid), endpoint, {
            "transport": "release-asset", "id": PINNED[rid][0], "repository": "mpv-player/mpv"}
    identifier = PINNED_ARTIFACTS[rid]
    endpoint = f"repos/mpv-player/mpv/actions/artifacts/{identifier}"
    name = validate_artifact(github_metadata(endpoint), rid)
    return name, endpoint + "/zip", {
        "transport": "original-actions-artifact", "reason": "release-asset-http-404", "id": identifier,
        "repository": "mpv-player/mpv", "repositoryID": UPSTREAM_REPOSITORY_ID,
        "buildRun": UPSTREAM_RUN, "headSHA": UPSTREAM_HEAD_SHA, "identicalPinnedArchive": True}


def download_archive(endpoint, transport, archive):
    base.require(transport in ("release-asset", "original-actions-artifact"), "Unknown pinned mpv transport")
    # Release assets select binary content through Accept; Actions /zip returns
    # its signed download redirect using the normal JSON API representation.
    accept = "application/octet-stream" if transport == "release-asset" else "application/json"
    with archive.open("xb") as output:
        subprocess.run(["gh", "api", "-H", "Accept: " + accept, endpoint],
                       stdout=output, check=True, timeout=120)


def extract_macos_bundle(payload):
    # The pinned upstream workflow uploads mpv.tar.gz inside the artifact ZIP
    # to preserve the app bundle's executable modes and relative dylib links.
    source = payload / "mpv.tar.gz"
    base.require(source.is_file() and not source.is_symlink() and list(payload.iterdir()) == [source],
                 "Expected the pinned macOS artifact's single bundle tarball")
    destination = payload / "bundle"
    base.require(not destination.exists(), "mpv bundle destination must be fresh")
    with tarfile.open(source, "r:gz") as archive:
        members, seen, total = [], set(), 0
        for member in archive:
            name = PurePosixPath(member.name)
            base.require(len(members) < 4096 and not name.is_absolute() and name.parts and
                         name.parts[0] == "mpv.app" and ".." not in name.parts and
                         "\\" not in member.name and "\x00" not in member.name and
                         not any(":" in part for part in name.parts) and member.name not in seen and
                         (member.isfile() or member.isdir() or member.issym()), "Unsafe mpv bundle entry")
            if member.issym():
                base.require(not Path(member.linkname).is_absolute() and "\\" not in member.linkname and
                             (destination / member.name).parent.joinpath(member.linkname).resolve().is_relative_to(
                                 (destination / "mpv.app").resolve()), "Unsafe mpv bundle symlink")
            total += member.size
            base.require(total <= 512 * 1024 ** 2, "mpv bundle exceeds its extraction budget")
            members.append(member)
            seen.add(member.name)
        base.require(members, "mpv bundle is empty")
        # Python's data filter additionally rejects links outside the destination
        # using actual filesystem resolution, including earlier archive symlinks.
        archive.extractall(destination, members=members, filter="data")
    source.unlink()
    return {"members": len(members), "uncompressedBytes": total, "source": "mpv.tar.gz"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=PINNED)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    root = args.output.resolve()
    base.require(not root.exists() and root.is_relative_to(Path(os.environ["RUNNER_TEMP"]).resolve()),
                 "mpv extraction must use a fresh runner temporary directory")
    identifier, size, digest, _ = PINNED[args.rid]
    name, download_endpoint, download_source = select_source(args.rid)
    root.mkdir()
    archive = root / name
    download_archive(download_endpoint, download_source["transport"], archive)
    inputs.verify_archive(archive, {"size_in_bytes": size, "digest": "sha256:" + digest})
    base.unpack(archive, root / "payload")
    archive.unlink()
    bundle = extract_macos_bundle(root / "payload") if args.rid.startswith("osx-") else None
    executable = base.one((p for p in (root / "payload").rglob("mpv.exe" if args.rid == "win-x64" else "mpv")
                           if p.is_file() and not p.is_symlink()), "pinned mpv executable")
    base.require(os.access(executable, os.X_OK), "Extracted mpv is not executable")
    result = {"passed": True, "rid": args.rid, "repository": "mpv-player/mpv", "sourceCommitPrefix": "c6c4c38d7",
              "buildRun": 35659722192, "assetID": identifier, "assetName": name, "assetBytes": size,
              "assetSHA256": digest, "executable": str(executable), "executableSHA256": base.sha256(executable),
              "downloadSource": download_source,
              "bundleExtraction": bundle,
              "version": subprocess.check_output([str(executable), "--version"], timeout=20).decode("utf-8", errors="replace")[:2048],
              "scope": "Pinned first-party development CI build; not all installed player versions"}
    (root / "provenance.json").write_text(json.dumps(result, indent=2) + "\n")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as output:
            output.write(f"executable={executable}\nprovenance={root / 'provenance.json'}\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
