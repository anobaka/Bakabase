#!/usr/bin/env python3
"""Audit and run actual Velopack packages on disposable GitHub-hosted desktops.

The execution path installs the original installer, including macOS postinstall.
It must never run on a developer machine. --audit-only reads ZIPs without starting
an application or installer and is safe to use for local package inspection.
"""
import argparse
import contextlib
import hashlib
import http.server
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import platform
import shutil
import signal
import socket
import sqlite3
import stat
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

SPEC = importlib.util.spec_from_file_location("release_contract", Path(__file__).with_name("check-release-contract.py"))
contract = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(contract)
ROOT = Path(__file__).resolve().parents[3]
MAX_ARCHIVE_BYTES = 2 * 1024 ** 3


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def require_hosted_runner(environment, system, machine, rid):
    require(environment.get("GITHUB_ACTIONS") == "true" and
            environment.get("RUNNER_ENVIRONMENT") == "github-hosted" and environment.get("RUNNER_TEMP"),
            "Native installation is permitted only on a disposable GitHub-hosted runner")
    actual = {("Windows", "AMD64"): "win-x64", ("Windows", "x86_64"): "win-x64",
              ("Darwin", "x86_64"): "osx-x64", ("Darwin", "arm64"): "osx-arm64"}.get((system, machine))
    require(actual == rid, f"A native {rid} runner is required; observed {system}/{machine}")


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def one(paths, description):
    values = list(paths)
    require(len(values) == 1, f"Expected exactly one {description}, found {len(values)}")
    return values[0]


def read_manifest(content):
    return {element.tag.split("}")[-1]: element.text for element in ET.fromstring(content).iter() if not list(element)}


def validate_manifest(manifest, role, rid, version):
    assembly = contract.PRODUCTS[role]["assembly"]
    main = assembly + (".exe" if rid == "win-x64" else "")
    for key, value in {"id": assembly, "mainExe": main, "rid": rid, "version": version}.items():
        require(manifest.get(key) == value, f"Package {key} differs: expected {value!r}, got {manifest.get(key)!r}")


def archive_entries(archive):
    entries = archive.infolist()
    require(len(entries) <= 25000 and sum(item.file_size for item in entries) <= MAX_ARCHIVE_BYTES,
            "Archive exceeds the acceptance fixture budget")
    seen = set()
    for entry in entries:
        # ZipInfo normalizes backslashes on Windows. Reject the on-disk spelling
        # before that normalization can hide an unsafe archive path.
        original = entry.orig_filename
        name = PurePosixPath(entry.filename)
        require(not name.is_absolute() and name.parts and ".." not in name.parts and
                "\\" not in original and "\x00" not in original and
                not any(":" in part for part in name.parts),
                "Unsafe archive path")
        require(entry.filename not in seen, "Duplicate archive entry")
        seen.add(entry.filename)
    return entries


def unpack(source, destination):
    destination.mkdir(parents=True)
    with zipfile.ZipFile(source) as archive:
        for entry in archive_entries(archive):
            name = PurePosixPath(entry.filename)
            if name.parts[0] == "__MACOSX":
                continue
            target = destination.joinpath(*name.parts)
            require(target.resolve().is_relative_to(destination.resolve()), "Archive path escapes destination")
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode):
                link = archive.read(entry).decode("utf-8")
                require(not Path(link).is_absolute() and "\\" not in link and ":" not in link and
                        (target.parent / link).resolve().is_relative_to(destination.resolve()), "Unsafe archive symlink")
                target.parent.mkdir(parents=True, exist_ok=True)
                target.symlink_to(link)
            elif entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as source_stream, target.open("wb") as output:
                    shutil.copyfileobj(source_stream, output, 65536)
                if os.name != "nt":
                    target.chmod((mode & 0o777) or 0o644)


def audit_packages(directory, role, rid, version):
    portable = one(directory.glob("*-Portable.zip"), "portable ZIP")
    full = one(directory.glob("*-full.nupkg"), "full update package")
    installer = one(directory.glob("*-Setup.exe" if rid == "win-x64" else "*-Setup.pkg"), "native installer")
    require(installer.stat().st_size > 1024, "Installer is empty or truncated")
    assembly = contract.PRODUCTS[role]["assembly"]
    binaries = {}
    manifests = {}
    bundle = None
    for kind, path in (("portable", portable), ("full", full)):
        with zipfile.ZipFile(path) as archive:
            entries = archive_entries(archive)
            suffix = "/Contents/Resources/sq.version" if rid.startswith("osx-") else "/sq.version"
            manifest_name = one((entry.filename for entry in entries if entry.filename.endswith(suffix)), kind + " manifest")
            manifest = read_manifest(archive.read(manifest_name))
            validate_manifest(manifest, role, rid, version)
            manifests[kind] = manifest
            main = manifest["mainExe"]
            binary = one((entry.filename for entry in entries if entry.filename.endswith("/" + main)
                          and (rid != "win-x64" or "/current/" in "/" + entry.filename or kind == "full")), kind + " main executable")
            prefix = binary[:-len(main)]
            for filename in (main, assembly + ".dll", assembly + ".deps.json",
                             "coreclr.dll" if rid == "win-x64" else "libcoreclr.dylib"):
                require(prefix + filename in archive.namelist(), f"Missing self-contained payload: {filename}")
            signature = archive.read(binary)[:4]
            require(signature[:2] == b"MZ" if rid == "win-x64" else signature in
                    (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"),
                    "Main executable is not a native binary")
            binaries[kind] = {name: hashlib.sha256(archive.read(prefix + name)).hexdigest()
                              for name in (main, assembly + ".dll", assembly + ".deps.json")}
            if kind == "portable":
                portable_content = prefix
                if rid.startswith("osx-"):
                    bundle = PurePosixPath(binary).parts[0]
                    allowed = ("Bakabase.app",) if role == "unified" else ("Bakabase.Client.app", "Bakabase Client.app")
                    require(bundle in allowed, "Unexpected application bundle name")
    require(binaries["portable"] == binaries["full"], "Portable and update package binaries differ")
    if rid.startswith("osx-"):
        contract.check_macos_portable(portable, role, version)
    return {"artifacts": {kind: {"file": path.name, "sizeBytes": path.stat().st_size, "sha256": sha256(path)}
                          for kind, path in (("portable", portable), ("full", full), ("installer", installer))},
            "manifest": manifests["portable"], "binaryHashes": binaries["portable"],
            "portableContent": portable_content, "bundleName": bundle, "passed": True}


def command(arguments, log, environment=None, timeout=240):
    with log.open("wb") as stream:
        process = subprocess.Popen([str(value) for value in arguments], stdout=stream, stderr=subprocess.STDOUT,
                                   env=environment, start_new_session=os.name != "nt")
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True, timeout=20)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=20)
            raise
    require(code == 0, f"Command failed ({code}); see {log.name}")


def native_processes(executable):
    if os.name == "nt":
        environment = dict(os.environ, BAKABASE_ACCEPTANCE_EXE=str(executable))
        script = "@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $env:BAKABASE_ACCEPTANCE_EXE } | Select-Object -ExpandProperty ProcessId) | ConvertTo-Json -Compress"
        output = subprocess.check_output(["powershell", "-NoProfile", "-Command", script], env=environment, text=True, timeout=20).strip()
        values = json.loads(output) if output else []
        return values if isinstance(values, list) else [values]
    output = subprocess.check_output(["ps", "-axo", "pid=,command="], text=True, timeout=10)
    return [int(fields[0]) for line in output.splitlines() if len(fields := line.strip().split(None, 1)) == 2
            and (fields[1] == str(executable) or fields[1].startswith(str(executable) + " "))]


def stop_native(executable):
    stopped = native_processes(executable)
    for pid in stopped:
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, timeout=20)
        else:
            with contextlib.suppress(ProcessLookupError):
                os.kill(pid, signal.SIGTERM)
    deadline = time.monotonic() + 10
    while native_processes(executable) and time.monotonic() < deadline:
        time.sleep(0.2)
    for pid in native_processes(executable):
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, timeout=20)
        else:
            with contextlib.suppress(ProcessLookupError):
                os.kill(pid, signal.SIGKILL)
    deadline = time.monotonic() + 5
    while native_processes(executable) and time.monotonic() < deadline:
        time.sleep(0.2)
    require(not native_processes(executable), "Owned application process did not exit")
    return stopped


def wait_absent(path, timeout):
    deadline = time.monotonic() + timeout
    while path.exists() and time.monotonic() < deadline:
        time.sleep(0.2)
    require(not path.exists(), f"Native cleanup did not remove {path}")


def remove_owned_tree(path):
    deadline = time.monotonic() + 15
    while path.exists():
        try:
            shutil.rmtree(path)
        except OSError:
            if time.monotonic() >= deadline:
                raise
            time.sleep(0.25)


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def prepare_data(directory, role):
    directory.mkdir(parents=True)
    port = free_port()
    (directory / "app.json").write_text(json.dumps({"App": {"language": "en-US", "enableAnonymousDataTracking": False,
        "listeningPorts": [port], "autoListeningPortCount": 0, "maxParallelism": 1}}))
    if role == "client":
        (directory / "client").mkdir()
        (directory / "client/host.json").write_text(json.dumps({"LoopbackPort": port}))
    (directory / "acceptance-sentinel.txt").write_text("installer acceptance owned external AppData\n")
    return port


def api(port, path, payload=None):
    body = None if payload is None else json.dumps(payload).encode()
    request = urllib.request.Request(f"http://127.0.0.1:{port}{path}", data=body,
                                     headers={"Content-Type": "application/json"})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(request, timeout=3) as response:
        return response.read(8 * 1024 * 1024)


def inspect_running(executable, data, port, role):
    endpoint = "/app/info" if role == "unified" else "/client/app/info"
    deadline, last = time.monotonic() + 120, None
    while time.monotonic() < deadline:
        try:
            response = json.loads(api(port, endpoint))
            require(response["code"] == 0, "Application info failed")
            info = response["data"]
            actual = info["appDataPath"] if role == "unified" else info["dataDirectory"]
            require(Path(actual).resolve() == data.resolve(), "Application data escaped its expected location")
            pids = native_processes(executable)
            require(pids, "API answered without the expected installed native process")
            if role == "unified":
                options = json.loads((data / "app.json").read_text(encoding="utf-8-sig"))["App"]
                require(options.get("version") == info["coreVersion"], "Startup has not persisted its current version")
            else:
                require(info.get("available") is True, "Client host is not available")
            break
        except (OSError, ValueError, KeyError, AssertionError, urllib.error.URLError) as error:
            last = str(error)
            time.sleep(0.5)
    else:
        raise TimeoutError(f"Native application startup failed: {last}")
    page = api(port, "/" if role == "unified" else "/client/connect-page")
    require(b"<html" in page.lower() and b"<script" in page.lower(), "Application did not serve its actual UI")
    if role == "client":
        context = json.loads(api(port, "/remote-access/context"))["data"]
        require(context["clientMode"] == 2 and context["serverReachable"] is False, "Legacy client role is incorrect")
    else:
        api(port, "/app/terms", {})
        created = json.loads(api(port, "/resource/placeholder", {"items": [{"title": "Native package acceptance"}],
                                                              "acquireImmediately": False}))["data"]
        require(len(created) == 1 and created[0].get("resourceId") and not created[0].get("error"),
                "Installed authoritative library could not persist a resource")
    return {"executable": str(executable), "processIds": pids, "effectiveDataDirectory": str(data),
            "appInfo": info, "uiBytes": len(page), "passed": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", required=True, type=Path)
    parser.add_argument("--role", required=True, choices=("unified", "client"))
    parser.add_argument("--rid", required=True, choices=("win-x64", "osx-x64", "osx-arm64"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--results-directory", required=True, type=Path)
    parser.add_argument("--audit-only", action="store_true")
    args = parser.parse_args()
    if not args.audit_only:
        require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    results = args.results_directory.resolve()
    require(not results.exists(), "Results must be a new directory")
    results.mkdir(parents=True)
    report = {"passed": False, "role": args.role, "rid": args.rid, "version": args.version,
              "headSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
              "scope": "archive-audit-only" if args.audit_only else "native-portable-and-original-installer",
              "limitations": ["No production signing identity, notarization, Gatekeeper or SmartScreen trust is validated.",
                              "Synthetic acceptance package version; no production feed or published Release is changed."]}
    try:
        report["packages"] = audit_packages(args.packages, args.role, args.rid, args.version)
        if not args.audit_only:
            execute(args, results, report)
        report["passed"] = True
    except (Exception, KeyboardInterrupt) as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        (results / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


def execute(args, results, report):
    require(shutil.disk_usage(os.environ["RUNNER_TEMP"]).free >= 5 * 1024 ** 3,
            "Native package acceptance requires 5 GiB free space")
    package = report["packages"]
    assembly = contract.PRODUCTS[args.role]["assembly"]
    bundle = package["bundleName"]
    mac = args.rid.startswith("osx-")
    home = Path.home()
    # These paths must be absent before either application starts. Only a fresh
    # hosted VM may run this gate; its newly created defaults are owned by this run.
    if mac:
        defaults = [home / "Library/Application Support" / name for name in ("Bakabase", "Bakabase.Client")]
        defaults += [base / name for base in (Path("/Applications"), home / "Applications")
                     for name in ("Bakabase.app", "Bakabase.Client.app", "Bakabase Client.app")]
        defaults += [base / name for base in (Path("/tmp/velopack"), home / "Library/Caches/velopack")
                     for name in ("Bakabase", "Bakabase.Client")]
        defaults += [home / "Library" / parent / name
                     for parent in ("Caches", "WebKit", "Preferences")
                     for name in ("com.anobaka.bakabase", "com.anobaka.bakabase.client")]
        defaults += [home / "Library/Saved Application State" / (name + ".savedState")
                     for name in ("com.anobaka.bakabase", "com.anobaka.bakabase.client")]
        defaults += [home / "Library/Preferences" / (name + ".plist")
                     for name in ("com.anobaka.bakabase", "com.anobaka.bakabase.client")]
    else:
        defaults = [Path(os.environ["LOCALAPPDATA"]) / name
                    for name in ("Bakabase", "Bakabase.Client", "Bakabase.AppData", "Bakabase.Client.AppData")]
    require(not any(path.exists() or path.is_symlink() for path in defaults),
            "Runner already contains Bakabase installation, data or cache")
    require(not os.environ.get("BAKABASE_DATA_DIR") and not os.environ.get("BAKABASE_CLIENT_DATA_DIR"),
            "Unexpected application data override on runner")
    report["preflightAbsentPaths"] = [str(path) for path in defaults]
    owned_executables, launchd_environment, receipt_ids = [], {}, []
    child_logs, children, cleanup_errors = [], [], []
    blocked = []

    class DenyNetwork(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            if len(blocked) < 200:
                blocked.append({"method": self.command, "target": self.path[:512]})
            self.send_error(503, "Disposable native package acceptance blocks background network access")
        do_POST = do_GET
        do_CONNECT = do_GET
        def log_message(self, *_):
            pass

    proxy = http.server.ThreadingHTTPServer(("127.0.0.1", 0), DenyNetwork)
    proxy_thread = threading.Thread(target=proxy.serve_forever, daemon=True)
    proxy_thread.start()
    proxy_url = f"http://127.0.0.1:{proxy.server_port}"
    isolated_environment = {"BAKABASE_UPDATE_URL": proxy_url, "BAKABASE_CLIENT_UPDATE_URL": proxy_url,
                            "Analytics__Sentry__BackendDsn": "", "Analytics__Sentry__ClientDsn": "",
                            "HTTP_PROXY": proxy_url, "HTTPS_PROXY": proxy_url, "ALL_PROXY": proxy_url,
                            "http_proxy": proxy_url, "https_proxy": proxy_url, "all_proxy": proxy_url,
                            "NO_PROXY": "localhost,127.0.0.1,::1", "no_proxy": "localhost,127.0.0.1,::1"}
    environment = dict(os.environ, **isolated_environment)
    work = Path(tempfile.mkdtemp(prefix="bakabase-native-acceptance-", dir=os.environ["RUNNER_TEMP"])).resolve()
    installed_root = Path("/Applications") / bundle if mac else work / "installed"
    install_data = home / "Library/Application Support" / assembly if mac else work / "installed-appdata"
    portable_data = work / "portable-appdata"
    data_paths = [portable_data, install_data]
    updater = installed_root / "Update.exe"

    def validate_content(content):
        validate_manifest(read_manifest((content / "sq.version").read_bytes()), args.role, args.rid, args.version)
        for filename, expected in package["binaryHashes"].items():
            require(sha256(content / filename) == expected, f"Installer payload differs from portable: {filename}")
        return contract.check_publish(content, args.role, require_web=args.role == "unified")

    def start_direct(executable, data, label):
        owned_executables.append(executable)
        log = (results / (label + "-app.log")).open("wb")
        child_logs.append(log)
        env = dict(environment, BAKABASE_DATA_DIR=str(data), BAKABASE_CLIENT_DATA_DIR=str(data))
        children.append(subprocess.Popen([str(executable)], cwd=executable.parent, env=env,
                                         stdout=log, stderr=subprocess.STDOUT, start_new_session=os.name != "nt"))

    try:
        portable_root = work / "portable"
        unpack(args.packages / package["artifacts"]["portable"]["file"], portable_root)
        content = portable_root / package["portableContent"]
        report["portableContentAudit"] = validate_content(content)
        portable_exe = content / package["manifest"]["mainExe"]
        port = prepare_data(portable_data, args.role)
        start_direct(portable_exe, portable_data, "portable")
        report["portableStartup"] = inspect_running(portable_exe, portable_data, port, args.role)
        report["portableProcessesStopped"] = stop_native(portable_exe)

        installer = (args.packages / package["artifacts"]["installer"]["file"]).resolve()
        port = prepare_data(install_data, args.role)
        if mac:
            expanded = work / "expanded-installer"
            command(["pkgutil", "--expand-full", installer, expanded], results / "pkg-expand.log")
            payload = one((path for path in expanded.rglob(bundle) if path.is_dir()), "installer application bundle")
            report["installerPayloadAudit"] = validate_content(payload / "Contents/MacOS")
            for info in expanded.rglob("PackageInfo"):
                receipt = ET.parse(info).getroot().get("identifier")
                require(receipt, "Installer receipt identifier is missing")
                require(subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True).returncode != 0,
                        "Runner already has this installer receipt")
                receipt_ids.append(receipt)
            require(receipt_ids, "Installer contains no component receipt")
            report["receiptIds"] = receipt_ids
            # Preserve LaunchServices startup: do not bypass postinstall with a
            # direct executable launch. Supply only network/telemetry isolation.
            for key, value in isolated_environment.items():
                previous = subprocess.run(["launchctl", "getenv", key], capture_output=True, text=True)
                launchd_environment[key] = previous.stdout.rstrip("\n") if previous.returncode == 0 else None
                subprocess.run(["launchctl", "setenv", key, value], check=True)
            for key in ("BAKABASE_DATA_DIR", "BAKABASE_CLIENT_DATA_DIR"):
                previous = subprocess.run(["launchctl", "getenv", key], capture_output=True, text=True)
                require(not previous.stdout.strip(), "Unexpected LaunchServices data override")
            installed_exe = installed_root / "Contents/MacOS" / assembly
            owned_executables.append(installed_exe)
            # vpk's postinstall uses $USER to select the GUI user. Explicitly
            # retain the runner user while the actual system installer is root.
            command(["sudo", "/usr/bin/env", "USER=" + os.environ["USER"], "/usr/sbin/installer",
                     "-pkg", installer, "-target", "/", "-verboseR"], results / "installer.log", environment)
            report["installation"] = {"mechanism": "original-pkg-system-install", "systemInstallExecuted": True,
                                      "automaticLaunchServicesStartup": True, "target": str(installed_root)}
            report["installedContentAudit"] = validate_content(installed_exe.parent)
        else:
            install_environment = dict(environment, BAKABASE_DATA_DIR=str(install_data), BAKABASE_CLIENT_DATA_DIR=str(install_data))
            command([installer, "--silent", "--installto", installed_root, "--log", results / "setup-native.log"],
                    results / "installer.log", install_environment)
            require(updater.is_file(), "Original Setup did not install Update.exe")
            installed_exe = installed_root / "current" / (assembly + ".exe")
            report["installation"] = {"mechanism": "original-setup-silent-installto", "systemInstallExecuted": True,
                                      "automaticStartupSuppressedByInstaller": True, "target": str(installed_root)}
            report["installedContentAudit"] = validate_content(installed_exe.parent)
            start_direct(installed_exe, install_data, "installed")
        report["installedStartup"] = inspect_running(installed_exe, install_data, port, args.role)
        report["installedProcessesStopped"] = stop_native(installed_exe)
        if args.role == "unified":
            databases = {}
            for label, data in (("portable", portable_data), ("installed", install_data)):
                with contextlib.closing(sqlite3.connect(data / "bakabase_insideworld.db")) as db:
                    require(db.execute("PRAGMA integrity_check").fetchall() == [("ok",)], "SQLite integrity failed")
                    count = db.execute("SELECT count(*) FROM ResourcesV2").fetchone()[0]
                    require(count == 1, "Native package did not retain the one created resource")
                    databases[label] = {"integrity": "ok", "resourceCount": count}
            report["databases"] = databases
    finally:
        def cleanup_step(label, action):
            try:
                action()
            except Exception as error:
                cleanup_errors.append(f"{label}: {error}")

        for executable in owned_executables:
            cleanup_step(f"Stop {executable}", lambda: stop_native(executable))
        for child in children:
            def reap_child():
                try:
                    child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=10)
            cleanup_step("Reap application process", reap_child)
        for log in child_logs:
            cleanup_step("Close application log", log.close)
        for label, data in (("portable", portable_data), ("installed", install_data)):
            def copy_logs():
                if not data.exists():
                    return
                logs = results / (label + "-application-logs")
                logs.mkdir()
                for index, path in enumerate(sorted(data.rglob("*.log"))[:20]):
                    # Only this run's fresh fixture logs, bounded even on startup failure.
                    with path.open("rb") as stream:
                        stream.seek(max(0, path.stat().st_size - 1024 * 1024))
                        (logs / f"{index}-{path.name}").write_bytes(stream.read())
            cleanup_step(f"Preserve {label} logs", copy_logs)

        def uninstall_windows():
            if not mac and updater.exists():
                command([updater, "--silent", "--log", results / "uninstall-native.log", "uninstall"],
                        results / "uninstaller.log", dict(environment, BAKABASE_DATA_DIR=str(install_data),
                                                          BAKABASE_CLIENT_DATA_DIR=str(install_data)))
                # Velopack's self-delete helper holds work as its current directory
                # until it removes the root after a delay. Observe completion.
                wait_absent(installed_root, 30)
                report["nativeUninstallPassed"] = True
        cleanup_step("Original Windows uninstaller", uninstall_windows)

        def check_external_data():
            if install_data.exists():
                require((install_data / "acceptance-sentinel.txt").read_text() == "installer acceptance owned external AppData\n",
                        "Installer or uninstaller changed external AppData sentinel")
                report["externalDataSentinelPreserved"] = True
        cleanup_step("External AppData", check_external_data)
        if mac:
            for receipt in receipt_ids:
                def forget_receipt():
                    if subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True).returncode == 0:
                        command(["sudo", "pkgutil", "--forget", receipt], results / ("forget-" + str(receipt_ids.index(receipt)) + ".log"))
                        require(subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True).returncode != 0,
                                "Installer receipt remains after cleanup")
                cleanup_step(f"Forget receipt {receipt}", forget_receipt)
        report["createdDefaultPaths"] = [str(path) for path in defaults if path.exists()]
        for path in defaults:
            def remove_default():
                if path.exists():
                    if mac:
                        command(["sudo", "rm", "-rf", "--", path], results / ("cleanup-" + str(defaults.index(path)) + ".log"))
                    else:
                        remove_owned_tree(path)
            cleanup_step(f"Remove owned default {path}", remove_default)
        cleanup_step("Remove acceptance work directory", lambda: remove_owned_tree(work))
        report["ownedFilesRemoved"] = not work.exists() and not any(path.exists() for path in defaults)
        if not report["ownedFilesRemoved"]:
            cleanup_errors.append("Owned installation/data cleanup is incomplete")
        for key, previous in launchd_environment.items():
            def restore_environment():
                result = subprocess.run(["launchctl", "unsetenv", key] if previous is None else
                                        ["launchctl", "setenv", key, previous], capture_output=True)
                require(result.returncode == 0, "Could not restore LaunchServices environment")
            cleanup_step(f"Restore environment {key}", restore_environment)
        proxy.shutdown()
        proxy.server_close()
        proxy_thread.join(timeout=5)
        report["blockedBackgroundRequests"] = blocked
        report["cleanupErrors"] = cleanup_errors
        require(not cleanup_errors, "Native acceptance cleanup failed: " + "; ".join(cleanup_errors))


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f"Interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
