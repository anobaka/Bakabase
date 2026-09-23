"""Pinned public-release identities and read-only payload verification.

These are published assets, never rebuilt historical sources. Native extraction
and downloads are invoked only after the matching hosted-runner guard.
"""
import importlib.util
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET


def sibling(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


repack = sibling("prepare-installed-updates")
base = repack.acceptance
require = base.require
REPOSITORY = "anobaka/Bakabase"
TAG = "v2.4.0-beta.349"
OLD_VERSION = "2.4.0-beta.349"
RELEASE_ID = 392491206
RELEASE_SHA = "86d76392b1f715fa08d952a81d96053ec363e294"
CANDIDATE_SHA = "be16c2635e60cf27331ae9c8a2ba274b7f5121c5"
CANDIDATE_RUN = 35815491996
CANDIDATE_VERSION = "0.0.1-acceptance.35815491996.1"
CANDIDATE_CORE = "2.4.0-beta.395"
RIDS = ("win-x64", "osx-x64", "osx-arm64")
ROLES = ("unified", "client")
PINS = {
    ("unified", "osx-arm64", "installer"): (577122397, 87202847, "5726ffc528672ae01c7873e4684a9402ff3f30995247441c1ab30383d17ebfdd"),
    ("unified", "osx-x64", "installer"): (577122399, 89824912, "370e21b1b15c1f185061787c4d34bc079d42d0cd08e3ecffa67d8106e573404a"),
    ("unified", "win-x64", "installer"): (577122408, 96642296, "725f2ec89a5424f03c98da7dcf2bca1a8c01e8506017e04ad88d332fa6d5a71a"),
    ("unified", "win-x64", "portable"): (577122396, 92167849, "c2abfc5a13c22754653c599b3b36ad798c84fd2671708909609050a209cda738"),
    ("client", "osx-arm64", "installer"): (577122404, 78996064, "055c11a20b8a9719dd562a7000a9972d6629329bc0b6de552124f24fddfa1e36"),
    ("client", "osx-x64", "installer"): (577122400, 81659218, "2cf1994e8dafe7a380edff02036dcf2e539518b71a0b37d4d39a90e59980e0fd"),
    ("client", "win-x64", "installer"): (577122401, 88381846, "157cb481bec48c723b6d9c18e9de193a6d598306d03795a796a285bb67a6e9fa"),
    ("client", "win-x64", "portable"): (577122403, 83907393, "b3dd10df3d2993574776e054d940e03b6dea63b39b4feebf65493b92441f1f76"),
}


def hosted(rid):
    # platform.system() can itself invoke `ver` on Windows. Reject a local
    # invocation before even querying the native platform, not just before install.
    require(os.environ.get("GITHUB_ACTIONS") == "true" and
            os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and os.environ.get("RUNNER_TEMP"),
            "Historical acceptance requires a disposable GitHub-hosted runner")
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), rid)


def pin(role, rid, kind):
    require((role, rid, kind) in PINS, "Unknown historical asset identity")
    asset_id, size, digest = PINS[role, rid, kind]
    title = "Bakabase" if role == "unified" else "Bakabase-Client"
    suffix = "Portable.zip" if kind == "portable" else "Setup.exe" if rid == "win-x64" else "Setup.pkg"
    name = f"{title}-{OLD_VERSION}-{rid}-{suffix}"
    return {"id": asset_id, "file": name, "sizeBytes": size, "sha256": digest,
            "url": f"https://github.com/{REPOSITORY}/releases/download/{TAG}/{name}"}


def validate_release(value, rid):
    require(rid in RIDS, "Unsupported historical RID")
    require(value.get("id") == RELEASE_ID and value.get("tag_name") == TAG and
            value.get("target_commitish") == RELEASE_SHA and value.get("draft") is False and
            value.get("prerelease") is True, "Historical release identity changed")
    assets = value.get("assets", [])
    require(isinstance(assets, list) and len(assets) <= 50, "Unexpected release asset inventory")
    result = {}
    for role in ROLES:
        result[role] = {}
        for kind in (("installer", "portable") if rid == "win-x64" else ("installer",)):
            expected = pin(role, rid, kind)
            matches = [asset for asset in assets if asset.get("name") == expected["file"]]
            require(len(matches) == 1, "Missing or duplicate historical release asset")
            actual = matches[0]
            require(actual.get("id") == expected["id"] and actual.get("size") == expected["sizeBytes"] and
                    actual.get("digest") == "sha256:" + expected["sha256"] and
                    actual.get("browser_download_url") == expected["url"] and actual.get("state") == "uploaded",
                    "Historical release asset differs from the pinned published bytes")
            result[role][kind] = expected
    return result


def verify_file(path, expected):
    require(path.is_file() and not path.is_symlink() and path.name == expected["file"], "Historical asset is not the exact regular file")
    require(path.stat().st_size == expected["sizeBytes"] and base.sha256(path) == expected["sha256"],
            "Historical release asset size or digest differs")
    return {key: expected[key] for key in ("file", "sizeBytes", "sha256")}


def download(expected, path, rid, deadline):
    hosted(rid)
    repack.check_budget(path.parent, deadline)
    with path.open("xb") as stream:
        child = subprocess.Popen(["gh", "api", f"repos/{REPOSITORY}/releases/assets/{expected['id']}",
                                  "-H", "Accept: application/octet-stream"], stdout=stream, stderr=subprocess.DEVNULL)
        try:
            end = min(deadline, time.monotonic() + 240)
            while child.poll() is None:
                require(time.monotonic() < end, "Historical asset download timed out")
                require(path.stat().st_size <= expected["sizeBytes"], "Historical asset exceeded its pinned byte budget")
                repack.check_budget(path.parent, deadline)
                time.sleep(0.25)
            require(child.returncode == 0, "Historical release asset download failed")
        finally:
            if child.poll() is None:
                child.kill()
            child.wait(timeout=10)
    return verify_file(path, expected)


def validate_versions(candidate, target):
    require(candidate == CANDIDATE_VERSION, "Unexpected candidate package version")
    # The numeric patch increment proves ordering above both historical and
    # current code versions without relying on lexical prerelease comparison.
    require(re.fullmatch(r"2\.4\.1-historical\.[1-9]\d*\.[1-9]\d*", target),
            "Historical target must be 2.4.1-historical.RUN.ATTEMPT")


def candidate_provenance(value, rid, version):
    require(repack.provenance_source(value, rid, version) == CANDIDATE_SHA and
            value.get("repository") == REPOSITORY and value.get("runID") == CANDIDATE_RUN and
            value.get("unchangedProductSources") is True, "Candidate is not the verified fixed package source")
    return CANDIDATE_SHA


def check_old_content(content, role, rid):
    product = base.contract.PRODUCTS[role]
    assembly = product["assembly"]
    main = assembly + (".exe" if rid == "win-x64" else "")
    required = (main, assembly + ".dll", assembly + ".deps.json", "Bakabase.Shell.dll",
                "Bakabase.Service.dll" if role == "unified" else "Bakabase.Client.Remoting.dll",
                "coreclr.dll" if rid == "win-x64" else "libcoreclr.dylib")
    require(all((content / name).is_file() for name in required), "Incomplete original self-contained product payload")
    with (content / main).open("rb") as stream:
        signature = stream.read(4)
    require(signature[:2] == b"MZ" if rid == "win-x64" else signature in
            (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"),
            "Historical executable is not native")
    require(isinstance(json.loads((content / (assembly + ".deps.json")).read_text(encoding="utf-8-sig")), dict),
            "Historical dependency manifest is invalid")
    if role == "unified":
        require((content / "web/index.html").is_file() and any((content / "web").rglob("*.js")), "Original unified UI is missing")
    else:
        require(not (content / "web").exists() and not (content / "Bakabase.Service.dll").exists(), "Original client includes a library host")
    return {"passed": True, "role": role, "originalPublishedPayload": True}


def original_bundle_identity(info, role):
    product = base.contract.PRODUCTS[role]
    observed = {key: info.get(key) for key in ("CFBundleIdentifier", "CFBundleExecutable", "CFBundlePackageType",
                                              "CFBundleVersion", "CFBundleShortVersionString")}
    # v349 supplied a custom plist without CFBundleExecutable. Preserve and test
    # those original bytes instead of demanding that the old asset already
    # contains the candidate's packaging fix. The manifest and actual native
    # executable are independently mandatory below; startup must still succeed.
    require(info.get("CFBundleIdentifier") == product["bundle"] and info.get("CFBundlePackageType") == "APPL" and
            ("CFBundleExecutable" not in info or info["CFBundleExecutable"] == product["assembly"]),
            "Historical application bundle identity differs: " + json.dumps(observed, sort_keys=True))
    return {"observed": observed, "legacyMissingExecutableKey": "CFBundleExecutable" not in info, "passed": True}


def original_manifest_identity(manifest, role, rid):
    require(rid in RIDS, "Unsupported historical target architecture")
    original_rid = manifest.get("rid")
    os_family = "win" if rid == "win-x64" else "osx"
    # The old release workflow omitted vpk --runtime, so these exact published
    # assets say win/osx in sq.version. Their architecture remains bound to the
    # per-RID published asset ID, byte length and SHA256 and matching native VM.
    # Do not rewrite the original manifest or relax the candidate's full RID.
    require(original_rid in (os_family, rid), "Historical manifest names another platform or architecture")
    base.validate_manifest(dict(manifest, rid=rid), role, rid, OLD_VERSION)
    return {"manifestRID": original_rid, "verifiedAssetRID": rid, "legacyOSOnlyRID": original_rid == os_family}


def audit_released(packages, role, rid, work, deadline, observations=None):
    """Expand the original pkg or published Win portable; never execute products."""
    hosted(rid)
    artifacts = {kind: verify_file(packages / pin(role, rid, kind)["file"], pin(role, rid, kind))
                 for kind in (("installer", "portable") if rid == "win-x64" else ("installer",))}
    observations = {} if observations is None else observations
    observations.update(passed=False, verifiedPublishedArtifacts=artifacts)
    expanded = work / (role + "-historical-expanded")
    bundle, bundle_identity = None, None
    try:
        if rid == "win-x64":
            base.unpack(packages / artifacts["portable"]["file"], expanded)
            content = base.one((path.parent for path in expanded.rglob("sq.version")
                                if path.parent.name == "current"), "published portable current directory")
        else:
            repack.run_command(["pkgutil", "--expand-full", packages / artifacts["installer"]["file"], expanded],
                               work / (role + "-pkg-expand.log"), deadline)
            infos = list(expanded.rglob("PackageInfo"))
            require(len(infos) == 1 and ET.parse(infos[0]).getroot().get("identifier") == base.contract.PRODUCTS[role]["bundle"],
                    "Historical pkg receipt identity differs")
            assembly = base.contract.PRODUCTS[role]["assembly"]
            content = base.one((path.parent for path in expanded.rglob(assembly)
                               if path.is_file() and path.parent.name == "MacOS" and path.parent.parent.name == "Contents"),
                              "published pkg native payload")
            bundle = content.parent.parent.name
            allowed = ("Bakabase.app",) if role == "unified" else ("Bakabase.Client.app", "Bakabase Client.app")
            require(bundle in allowed, "Historical application bundle name differs")
            info = plistlib.loads((content.parent / "Info.plist").read_bytes())
            bundle_identity = original_bundle_identity(info, role)
            observations.update(bundleName=bundle, bundleIdentity=bundle_identity)
        check_old_content(content, role, rid)
        manifest = base.read_manifest((content / "sq.version").read_bytes())
        observations["manifest"] = {key: manifest.get(key) for key in ("id", "mainExe", "rid", "version", "channel")}
        runtime_identity = original_manifest_identity(manifest, role, rid)
        observations["runtimeIdentity"] = runtime_identity
        require(manifest.get("channel") == "beta", "Historical release must retain its original beta manifest")
        hashes = repack.tree_hashes(content)
        require(repack.MARKER not in hashes, "Historical release unexpectedly contains a test marker")
        observations.update(passed=True, payloadFileCount=len(hashes))
        return {"passed": True, "artifacts": artifacts, "manifest": manifest, "bundleName": bundle,
                "bundleIdentity": bundle_identity,
                "runtimeIdentity": runtime_identity,
                "binaryHashes": {name: hashes[name]["sha256"] for name in
                                  (manifest["mainExe"], base.contract.PRODUCTS[role]["assembly"] + ".dll",
                                   base.contract.PRODUCTS[role]["assembly"] + ".deps.json")},
                "payloadHashes": hashes, "releaseTag": TAG, "releaseID": RELEASE_ID,
                "payloadSource": "original-pkg-expanded" if rid.startswith("osx-") else "original-published-portable"}
    finally:
        if expanded.exists():
            shutil.rmtree(expanded)


def read_manifest(path, rid):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 16 * 1024 * 1024,
            "Historical preparation manifest is missing or oversized")
    value = json.loads(path.read_text())
    return validate_preparation(value, rid)


def validate_preparation(value, rid):
    require(value.get("passed") is True and value.get("format") == "bakabase-historical-upgrade-v1" and
            value.get("rid") == rid and value.get("oldVersion") == OLD_VERSION and
            value.get("sourceSHA") == CANDIDATE_SHA and value.get("historicalRelease", {}).get("id") == RELEASE_ID and
            value.get("historicalRelease", {}).get("tag") == TAG, "Historical preparation identity is invalid")
    validate_versions(value.get("candidateVersion"), value.get("newVersion", ""))
    candidate_provenance(value.get("provenance", {}), rid, value["candidateVersion"])
    require(set(value.get("roles", {})) == set(ROLES), "Both historical products are required")
    for role in ROLES:
        entry = value["roles"][role]
        require(entry.get("role") == role and entry.get("candidatePayloadUnchanged") is True and
                entry.get("expectedRunningVersion") == CANDIDATE_CORE, "Candidate product payload is not verified")
        original_manifest_identity(entry["oldManifest"], role, rid)
        base.validate_manifest(entry["newManifest"], role, rid, value["newVersion"])
        require(entry["oldManifest"].get("channel") == "beta" and
                entry["newManifest"].get("channel") == entry.get("channel") == ("win" if rid == "win-x64" else "osx"),
                "Unexpected original or target update channel")
        marker = entry.get("marker", {})
        require(marker.get("format") == "bakabase-installed-updater-acceptance" and marker.get("version") == 1 and
                marker.get("role") == role and marker.get("oldVersion") == OLD_VERSION and
                marker.get("newVersion") == value["newVersion"] and marker.get("sourceSHA") == CANDIDATE_SHA and
                isinstance(marker.get("nonce"), str) and re.fullmatch(r"[0-9a-f]{32}", marker["nonce"]), "Historical update marker differs")
        require(entry.get("oldFullPackage") is None and "oldFull" not in entry.get("packageChecksums", {}),
                "A historical full package must not be invented from candidate code")
        source = entry.get("candidateSource", {})
        base.validate_manifest(source.get("manifest", {}), role, rid, CANDIDATE_VERSION)
        generated = repack.generated_names(role, rid)
        require(set(entry.get("vendorGenerated", {}).get("allowedNames", [])) == generated,
                "Unexpected generated-file exceptions")
        repack.compare_product_payloads(source.get("payloadHashes", {}), entry.get("newPayloadHashes", {}), generated)
        repack.compare_product_payloads(source.get("payloadHashes", {}), entry.get("newFullPayloadHashes", {}), generated)
        require(source.get("payloadHashes") and entry.get("oldPayloadHashes"), "Prepared product inventory is empty")
    return value
