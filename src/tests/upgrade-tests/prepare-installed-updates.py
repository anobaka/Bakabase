#!/usr/bin/env python3
"""Repack existing native payloads for installed-updater acceptance; never install apps.

Execution is restricted to the matching disposable GitHub-hosted runner. Product
assemblies and web assets are copied, never rebuilt. The higher manifest version
and data-only marker distinguish updater mechanics from historical-code migration.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import platform
import plistlib
import re
import shutil
import signal
import stat
import subprocess
import time
import uuid
import zipfile


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


HERE = Path(__file__).resolve().parent
acceptance = load_module("installed_update_acceptance", HERE / "run-package-acceptance.py")
plist_preparer = load_module("installed_update_plist", HERE.parents[1] / "scripts/prepare-macos-plist.py")
require = acceptance.require
MARKER = "updater-acceptance.json"
GENERATED = frozenset(("sq.version", "UpdateMac", "Update.exe"))
MAX_FILES = 25000
MINIMUM_FREE_BYTES = 5 * 1024 ** 3


def validate_versions(old, new):
    require(re.fullmatch(r"0\.0\.1-acceptance\.[1-9]\d*\.[1-9]\d*", old),
            "Old version must identify the original acceptance run and attempt")
    require(re.fullmatch(r"0\.0\.2-updater\.[1-9]\d*\.[1-9]\d*", new),
            "New version must be 0.0.2-updater.RUN.ATTEMPT")


def provenance_source(value, rid, version):
    require(isinstance(value, dict), "Provenance must be a JSON object")
    require(value.get("passed") is True and value.get("rid") == rid and value.get("version") == version,
            "Provenance must be passed and match the requested RID/version")
    sha = value.get("packageSourceSHA", value.get("sourceSHA", value.get("headSHA")))
    require(isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{40}", sha),
            "Provenance requires a full packageSourceSHA")
    require(value.get("headSHA", sha) == sha and value.get("sourceSHA", sha) == sha,
            "Conflicting provenance source identities")
    return sha


def package_info(path):
    require(path.is_file() and 1024 < path.stat().st_size <= acceptance.MAX_ARCHIVE_BYTES,
            "Package is missing, truncated or exceeds the fixture budget")
    hashes = {name: hashlib.new(name) for name in ("sha1", "sha256")}
    with path.open("rb") as source:
        for block in iter(lambda: source.read(65536), b""):
            for value in hashes.values():
                value.update(block)
    return {"path": str(path.resolve()), "fileName": path.name, "sizeBytes": path.stat().st_size,
            **{name: value.hexdigest() for name, value in hashes.items()}}


def tree_hashes(directory):
    """Hash every regular file and the spelling of internal relative symlinks."""
    result, total = {}, 0
    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            relative = path.relative_to(directory).as_posix()
            link = os.readlink(path)
            # The old macOS generated manifest link deliberately leaves MacOS/;
            # it is separately removed before packing, not a product dependency.
            if relative not in GENERATED:
                require(not Path(link).is_absolute() and "\\" not in link and ":" not in link and
                        (path.parent / link).resolve().is_relative_to(directory.resolve()),
                        "Payload symlink escapes the copied application content")
            result[relative] = {"kind": "symlink", "target": link,
                                "sha256": hashlib.sha256(link.encode()).hexdigest()}
        elif path.is_file():
            total += path.stat().st_size
            require(total <= acceptance.MAX_ARCHIVE_BYTES, "Payload exceeds the fixture budget")
            result[path.relative_to(directory).as_posix()] = {"kind": "file", "sizeBytes": path.stat().st_size,
                                                           "sha256": acceptance.sha256(path)}
        else:
            require(path.is_dir(), "Unsupported application payload entry")
        require(len(result) <= MAX_FILES, "Payload file count exceeds the fixture budget")
    return result


def generated_names(role, rid):
    assembly = acceptance.contract.PRODUCTS[role]["assembly"]
    return frozenset(("sq.version", "UpdateMac")) if rid.startswith("osx-") else frozenset(
        ("sq.version", "Update.exe", "Squirrel.exe", assembly + "_ExecutionStub.exe"))


def product_hashes(hashes, generated=GENERATED):
    return {name: value for name, value in hashes.items() if name not in generated and name != MARKER}


class PayloadMismatch(AssertionError):
    def __init__(self, message, differences):
        self.differences = differences
        super().__init__(message + ": " + json.dumps(differences, sort_keys=True))


def compare_product_payloads(old, new, generated=GENERATED, message="Product payload changed during repacking"):
    before, after = product_hashes(old, generated), product_hashes(new, generated)
    differences = [{"path": name, "before": before.get(name), "after": after.get(name)}
                   for name in sorted(before.keys() | after.keys()) if before.get(name) != after.get(name)][:12]
    if differences:
        raise PayloadMismatch(message, differences)


def compare_payloads(old, new, generated=GENERATED):
    compare_product_payloads(old, new, generated)
    require(MARKER in new and new[MARKER]["kind"] == "file", "Repacked payload lacks its data-only marker")


def payload_from_full(path, role, rid, version):
    """Read all installed payload entries, not just the executable audited upstream."""
    assembly = acceptance.contract.PRODUCTS[role]["assembly"]
    main = assembly + (".exe" if rid == "win-x64" else "")
    result, marker = {}, None
    with zipfile.ZipFile(path) as archive:
        entries = acceptance.archive_entries(archive)
        suffix = "/Contents/Resources/sq.version" if rid.startswith("osx-") else "/sq.version"
        manifest_name = acceptance.one((e.filename for e in entries if e.filename.endswith(suffix)), "full manifest")
        manifest = acceptance.read_manifest(archive.read(manifest_name))
        acceptance.validate_manifest(manifest, role, rid, version)
        main_name = acceptance.one((e.filename for e in entries if e.filename.endswith("/" + main)), "full main executable")
        prefix = main_name[:-len(main)]
        for entry in entries:
            if not entry.filename.startswith(prefix) or entry.is_dir():
                continue
            relative = entry.filename[len(prefix):]
            data = archive.read(entry)
            # Velopack full packages serialize this generated macOS link as a
            # regular .__symlink entry; portable ZIPs contain the native link.
            encoded_manifest_link = rid.startswith("osx-") and relative == "sq.version.__symlink"
            if encoded_manifest_link:
                require(data == b"../Resources/sq.version", "Unexpected full manifest symlink target")
                relative = "sq.version"
            require(relative not in result, "Duplicate full payload path after manifest symlink decoding")
            if encoded_manifest_link or stat.S_ISLNK(entry.external_attr >> 16):
                result[relative] = {"kind": "symlink", "target": data.decode(), "sha256": hashlib.sha256(data).hexdigest()}
            else:
                result[relative] = {"kind": "file", "sizeBytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
            if relative == MARKER:
                marker = json.loads(data)
    return result, manifest, marker


def validate_vpk_version(output):
    versions = re.findall(r"(?<![\w.])\d+\.\d+\.\d+(?:[-+][\w.-]+)?", output)
    require(len(versions) == 1 and versions[0].split("+")[0] == "1.2.0", "Expected exactly Velopack CLI 1.2.0")
    return versions[0]


def run_command(arguments, log, deadline, environment=None):
    remaining = deadline - time.monotonic()
    require(remaining > 0, "Repack deadline exceeded")
    with log.open("wb") as output:
        child = subprocess.Popen([str(value) for value in arguments], stdout=output, stderr=subprocess.STDOUT,
                                 env=environment, start_new_session=os.name != "nt")
        try:
            code = child.wait(timeout=min(420, remaining))
        except BaseException:
            if child.poll() is None:
                if os.name == "nt":
                    subprocess.run(["taskkill", "/PID", str(child.pid), "/T", "/F"],
                                   capture_output=True, timeout=20)
                else:
                    os.killpg(child.pid, signal.SIGKILL)
                child.wait(timeout=20)
            raise
    require(code == 0, f"Repack command failed ({code}); see {log.name}")


def check_budget(output, deadline):
    require(time.monotonic() < deadline, "Repack deadline exceeded")
    require(shutil.disk_usage(output).free >= MINIMUM_FREE_BYTES, "Repacking requires 5 GiB free space")


def prepare_role(role, packages, output, args, source_sha, nonce, deadline):
    check_budget(output, deadline)
    old_audit = acceptance.audit_packages(packages, role, args.rid, args.old_version)
    expected = args.provenance_data.get("roles", {}).get(role, {})
    require(expected.get("version") == args.old_version, "Provenance role version differs")
    require(expected.get("packageFiles") == old_audit["artifacts"], "Input packages differ from verified provenance hashes")
    generated = generated_names(role, args.rid)
    role_root = output / role
    role_root.mkdir()
    original = role_root / "source"
    acceptance.unpack(packages / old_audit["artifacts"]["portable"]["file"], original)
    source_payload = original / old_audit["portableContent"]
    old_hashes = tree_hashes(source_payload)
    require(MARKER not in old_hashes, "Original package already contains an update acceptance marker")
    old_full = packages / old_audit["artifacts"]["full"]["file"]
    full_hashes, _, _ = payload_from_full(old_full, role, args.rid, args.old_version)
    compare_product_payloads(old_hashes, full_hashes, generated, "Original portable/full product payloads differ")
    acceptance.contract.check_publish(source_payload, role, require_web=role == "unified")
    staged = role_root / "payload"
    shutil.copytree(source_payload, staged, symlinks=True)
    for name in GENERATED:
        path = staged / name
        if path.exists() or path.is_symlink():
            require(not path.is_dir(), "Generated payload name unexpectedly names a directory")
            path.unlink()
    marker = {"format": "bakabase-installed-updater-acceptance", "version": 1, "role": role,
              "oldVersion": args.old_version, "newVersion": args.new_version,
              "sourceSHA": source_sha, "nonce": nonce}
    (staged / MARKER).write_text(json.dumps(marker, indent=2) + "\n", encoding="utf-8")
    assembly = acceptance.contract.PRODUCTS[role]["assembly"]
    main = old_audit["manifest"]["mainExe"]
    title = "Bakabase" if role == "unified" else "Bakabase Client"
    channel = "win" if args.rid == "win-x64" else "osx"
    output_packages = role_root / "packages"
    arguments = [args.vpk, "pack", "--yes", "true", "--skip-updates", "true", "--legacyConsole", "true",
                 "--noDefaultExclude", "true",
                 "--packId", assembly, "--packTitle", title, "--packVersion", args.new_version,
                 "--packDir", staged, "--mainExe", main, "--runtime", args.rid,
                 "--channel", channel, "--outputDir", output_packages]
    if args.rid.startswith("osx-"):
        outer_plist = original / old_audit["bundleName"] / "Contents/Info.plist"
        copied_plist = role_root / "Info.plist"
        plist_preparer.prepare(outer_plist, copied_plist, args.new_version)
        original_info = plistlib.loads(outer_plist.read_bytes())
        icon_name = original_info.get("CFBundleIconFile", "")
        require(icon_name and Path(icon_name).name == icon_name and ":" not in icon_name and "\\" not in icon_name,
                "Original bundle icon path is not a file name")
        original_icon = outer_plist.parent / "Resources" / icon_name
        require(original_icon.is_file(), "Original outer bundle icon is missing")
        arguments += ["--icon", original_icon, "--plist", copied_plist]
    else:
        arguments += ["--icon", staged / "Assets/favicon.ico"]
    log = role_root / "vpk-pack.log"
    print(f"REPACK: {role} {args.rid}", flush=True)
    run_command(arguments, log, deadline)
    check_budget(output, deadline)
    new_audit = acceptance.audit_packages(output_packages, role, args.rid, args.new_version)
    require(new_audit["manifest"].get("channel") == channel, "Generated package channel differs")
    require(new_audit["bundleName"] == old_audit["bundleName"], "Generated bundle identity differs")
    verified = role_root / "verified"
    acceptance.unpack(output_packages / new_audit["artifacts"]["portable"]["file"], verified)
    new_payload = verified / new_audit["portableContent"]
    new_hashes = tree_hashes(new_payload)
    compare_payloads(old_hashes, new_hashes, generated)
    require(json.loads((new_payload / MARKER).read_text()) == marker, "Portable marker differs")
    acceptance.contract.check_publish(new_payload, role, require_web=role == "unified")
    new_full = output_packages / new_audit["artifacts"]["full"]["file"]
    new_full_hashes, new_manifest, new_marker = payload_from_full(new_full, role, args.rid, args.new_version)
    compare_payloads(old_hashes, new_full_hashes, generated)
    require(product_hashes(new_hashes, generated) == product_hashes(new_full_hashes, generated) and new_marker == marker,
            "Generated full package differs from verified portable")
    require(new_manifest == new_audit["manifest"], "Generated full/portable manifests differ")
    if args.rid.startswith("osx-"):
        actual_plist = verified / new_audit["bundleName"] / "Contents/Info.plist"
        require(plistlib.loads(actual_plist.read_bytes()) == plistlib.loads(copied_plist.read_bytes()),
                "Generated outer plist differs from explicitly prepared original identity")
        def extra_bundle_hashes(contents):
            return {name: value for name, value in tree_hashes(contents).items()
                    if not name.startswith("MacOS/") and name not in ("Info.plist", "Resources/sq.version")}
        require(extra_bundle_hashes(actual_plist.parent) == extra_bundle_hashes(outer_plist.parent),
                "Non-generated outer bundle resources changed or disappeared")
    result = {"role": role, "oldFullPackage": str(old_full.resolve()), "newFullPackage": str(new_full.resolve()),
              "oldManifest": old_audit["manifest"], "newManifest": new_manifest, "marker": marker, "markerPath": MARKER,
              "mainExe": main, "bundleName": new_audit["bundleName"], "channel": channel,
              "oldPayloadHashes": old_hashes, "newPayloadHashes": new_hashes, "newFullPayloadHashes": new_full_hashes,
              "vendorGenerated": {"allowedNames": sorted(generated),
                  "old": {k: v for k, v in old_hashes.items() if k in generated},
                  "new": {k: v for k, v in new_hashes.items() if k in generated},
                  "newFull": {k: v for k, v in new_full_hashes.items() if k in generated}},
              "packageChecksums": {"oldFull": package_info(old_full), "newFull": package_info(new_full),
                  "oldPortable": package_info(packages / old_audit["artifacts"]["portable"]["file"]),
                  "newPortable": package_info(output_packages / new_audit["artifacts"]["portable"]["file"])},
              "packLog": str(log.resolve()), "sourceAudit": old_audit, "newAudit": new_audit, "payloadUnchanged": True}
    # Evidence and actual packages remain; copies used solely for auditing do not.
    for directory in (original, staged, verified):
        shutil.rmtree(directory)
    print(f"VERIFIED: {role} complete payload hashes, marker and full package", flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unified-packages", required=True, type=Path)
    parser.add_argument("--client-packages", required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=("win-x64", "osx-x64", "osx-arm64"))
    parser.add_argument("--old-version", required=True)
    parser.add_argument("--new-version", required=True)
    parser.add_argument("--vpk", required=True, type=Path)
    parser.add_argument("--provenance", required=True, type=Path)
    parser.add_argument("--output-directory", required=True, type=Path)
    parser.add_argument("--timeout", type=int, default=900)
    args = parser.parse_args()
    acceptance.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    validate_versions(args.old_version, args.new_version)
    require(0 < args.timeout <= 1800, "Repack timeout must be within 1..1800 seconds")
    output = args.output_directory.resolve()
    require(not output.exists() and not args.output_directory.is_symlink(), "Output directory must be new")
    require(output.is_relative_to(Path(os.environ["RUNNER_TEMP"]).resolve()), "Output must be inside RUNNER_TEMP")
    args.vpk = args.vpk.resolve()
    require(args.vpk.is_file(), "Velopack CLI executable is missing")
    args.unified_packages, args.client_packages = args.unified_packages.resolve(), args.client_packages.resolve()
    require(args.unified_packages.is_dir() and args.client_packages.is_dir() and
            args.unified_packages != args.client_packages, "Product inputs must be distinct existing package directories")
    require(not any(output.is_relative_to(path) or path.is_relative_to(output)
                    for path in (args.unified_packages, args.client_packages)), "Output overlaps original product packages")
    require(args.provenance.is_file() and args.provenance.stat().st_size <= 1024 * 1024, "Provenance is missing or oversized")
    provenance = json.loads(args.provenance.read_text(encoding="utf-8"))
    source_sha = provenance_source(provenance, args.rid, args.old_version)
    args.provenance_data = provenance
    output.mkdir(parents=True)
    report = {"passed": False, "status": "running", "rid": args.rid,
              "oldVersion": args.old_version, "newVersion": args.new_version,
              "sourceSHA": source_sha, "provenance": provenance, "roles": {},
              "scope": "Same product payload repack; updater mechanism, not historical-code migration"}
    started, nonce = time.monotonic(), uuid.uuid4().hex
    deadline = started + args.timeout
    try:
        check_budget(output, deadline)
        version_log = output / "vpk-version.log"
        # vpk 1.2.0 exposes its version in root help, not a --version option.
        run_command([args.vpk, "--help"], version_log, deadline)
        require(version_log.stat().st_size < 65536, "Unexpectedly large CLI version output")
        report["vpkVersion"] = validate_vpk_version(version_log.read_text(encoding="utf-8-sig"))
        for role, directory in (("unified", args.unified_packages), ("client", args.client_packages)):
            report["roles"][role] = prepare_role(role, directory, output, args, source_sha, nonce, deadline)
        check_budget(output, deadline)
        report.update(passed=True, status="passed")
    except (Exception, KeyboardInterrupt) as error:
        report.update(status="failed", error={"type": type(error).__name__, "message": str(error)[:2000]})
        if isinstance(error, PayloadMismatch):
            report["error"]["payloadDifferences"] = error.differences
    finally:
        cleanup_errors = []
        for role in ("unified", "client"):
            for name in ("source", "payload", "verified"):
                path = output / role / name
                try:
                    if path.exists():
                        shutil.rmtree(path)
                except OSError as error:
                    cleanup_errors.append({"path": str(path), "type": type(error).__name__})
        report["temporaryCopyCleanupErrors"] = cleanup_errors
        if cleanup_errors:
            report.update(passed=False, status="failed")
        report["elapsedSeconds"] = round(time.monotonic() - started, 2)
        (output / "updates.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(("PASS" if report["passed"] else "FAIL") + ": " + str(output / "updates.json"), flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f"Interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
