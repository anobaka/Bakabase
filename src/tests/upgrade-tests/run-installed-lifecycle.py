#!/usr/bin/env python3
"""Exercise installed Bakabase products together on a fresh GitHub-hosted VM.

Installs the original legacy client first, then the unified application. Uses
both products' real default data directories, without startup hooks or data
overrides. macOS runs original pkg postinstall; bundle removal on macOS is
explicitly not described as a native uninstaller. Never run on a developer host.
"""
import argparse
import contextlib
import http.server
import importlib.util
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import socket
import sqlite3
import subprocess
import tempfile
import threading
import time
from types import SimpleNamespace
import urllib.error
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location("package_acceptance", Path(__file__).with_name("run-package-acceptance.py"))
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)
require = base.require
FORBIDDEN_ENV = {"BAKABASE_DATA_DIR", "BAKABASE_CLIENT_DATA_DIR", "DOTNET_STARTUP_HOOKS", "BAKABASE_UPGRADE_TEST_ROOT"}
ROLES = ("client", "unified")
CLIENT_DEVICE_NAME = "Installed lifecycle retained client settings"


def sibling(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


updates = sibling("installed-update-exercise")


def default_paths(rid, home, environment):
    if rid.startswith("osx-"):
        data = {role: home / "Library/Application Support" / base.contract.PRODUCTS[role]["dataFolder"] for role in ROLES}
        absent = list(data.values())
        absent += [parent / name for parent in (Path("/Applications"), home / "Applications")
                   for name in ("Bakabase.app", "Bakabase.Client.app", "Bakabase Client.app")]
        absent += [parent / name for parent in (Path("/tmp/velopack"), home / "Library/Caches/velopack")
                   for name in ("Bakabase", "Bakabase.Client")]
        identities = [base.contract.PRODUCTS[role]["bundle"] for role in ROLES]
        absent += [home / "Library" / parent / name for parent in ("Caches", "WebKit", "Preferences") for name in identities]
        absent += [home / "Library/Preferences" / (name + ".plist") for name in identities]
        absent += [home / "Library/Saved Application State" / (name + ".savedState") for name in identities]
    else:
        require(rid == "win-x64" and environment.get("LOCALAPPDATA"), "Windows local AppData is required")
        local = Path(environment["LOCALAPPDATA"])
        require(local.is_absolute(), "Windows local AppData must be an absolute path")
        data = {role: local / base.contract.PRODUCTS[role]["windowsDataFolder"] for role in ROLES}
        absent = list(data.values()) + [local / base.contract.PRODUCTS[role]["assembly"] for role in ROLES]
        absent += [local / "velopack" / base.contract.PRODUCTS[role]["assembly"] for role in ROLES]
        # Only these products' shortcuts are owned. Never remove a shared Start Menu folder.
        shortcut_roots = [home / "Desktop"]
        if environment.get("PUBLIC"):
            shortcut_roots.append(Path(environment["PUBLIC"]) / "Desktop")
        if environment.get("APPDATA"):
            shortcut_roots.append(Path(environment["APPDATA"]) / "Microsoft/Windows/Start Menu/Programs")
        absent += [parent / name for parent in shortcut_roots
                   for name in ("Bakabase.lnk", "Bakabase.Client.lnk", "Bakabase Client.lnk")]
    absent += updates.default_log_paths(rid, home, environment)
    return {"data": data, "absent": absent}


def require_pristine(paths, environment):
    require(not any(path.exists() or path.is_symlink() for path in paths),
            "Runner already contains a product installation, data, cache or shortcut")
    require(not any(key.upper() in FORBIDDEN_ENV and value for key, value in environment.items()),
            "Data directory overrides and startup hooks are forbidden")


def require_separate_apps(apps):
    require(set(apps) == set(ROLES), "Both installed product roles are required")
    for role, app in apps.items():
        require(app["role"] == role, "Application role does not match its key")
        require(type(app["port"]) is int and 0 < app["port"] <= 65535, "Invalid application port")
        require_pristine([], app["environment"])
    require(apps["client"]["port"] != apps["unified"]["port"], "Product ports overlap")
    for field in ("data", "exe"):
        left, right = (apps[role][field].resolve() for role in ROLES)
        require(left != right and not left.is_relative_to(right) and not right.is_relative_to(left),
                f"Product {field} locations overlap")


def client_state(app):
    settings = app["data"] / "client/host.json"
    require(json.loads(settings.read_text(encoding="utf-8-sig"))["LoopbackPort"] == app["port"],
            "Legacy client changed its persisted port")
    connection = json.loads((app["data"] / "client/connection.json").read_text(encoding="utf-8-sig"))
    require(connection.get("DeviceName") == CLIENT_DEVICE_NAME and connection.get("Servers") == [] and
            connection.get("ActiveServerId") is None, "Legacy client persistent connection settings changed")
    return {name: base.sha256(app["data"] / "client" / name)
            for name in ("host.json", "connection.json")}


def verify_client_state(app, expected):
    actual = client_state(app)
    require(actual == expected, "Legacy client settings or sentinel changed")
    return actual


def database_state(app, expected_id):
    require(type(expected_id) is int and expected_id > 0, "Expected resource ID must be a positive integer")
    path = app["data"] / "bakabase_insideworld.db"
    require(path.is_file(), "Unified library database is missing")
    # Read-only URI prevents a missing or wrong path from silently creating a DB.
    with contextlib.closing(sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=10)) as db:
        require(db.execute("PRAGMA integrity_check").fetchall() == [("ok",)], "Unified SQLite integrity failed")
        ids = [row[0] for row in db.execute("SELECT Id FROM ResourcesV2 ORDER BY Id")]
    require(ids == [expected_id], "Unified library did not retain exactly the created resource")
    return {"integrity": "ok", "resourceCount": len(ids), "resourceIds": ids}


def observe_app(app, startup=False, deadline=None):
    """Read the real installed process/API without changing application data."""
    endpoint = "/client/app/info" if app["role"] == "client" else "/app/info"
    ui_endpoint = "/client/connect-page" if app["role"] == "client" else "/"
    if deadline is None:
        deadline = time.monotonic() + 120
    last = None
    ui_timeout_retries = 0
    while time.monotonic() < deadline:
        try:
            response = json.loads(base.api(app["port"], endpoint))
            require(response["code"] == 0, "Application info failed")
            info = response["data"]
            actual = info["dataDirectory"] if app["role"] == "client" else info["appDataPath"]
            require(Path(actual).resolve() == app["data"].resolve(), "Application did not use its product's default AppData")
            pids = base.native_processes(app["exe"])
            require(pids, "Expected installed native process is absent")
            if app["role"] == "client":
                require(info["available"] is True, "Legacy client host is unavailable")
            else:
                options = json.loads((app["data"] / "app.json").read_text(encoding="utf-8-sig"))["App"]
                require(options.get("version") == info["coreVersion"], "Unified startup has not persisted its current version")
                require(Path(info["anchorPath"]).resolve() == app["data"].resolve() and
                        Path(info["defaultDataPath"]).resolve() == app["data"].resolve() and
                        info["dataInInstallRoot"] is False, "Unified default data anchor is incorrect")
        except (OSError, ValueError, KeyError, TypeError, AssertionError, urllib.error.URLError) as error:
            last = str(error)
            time.sleep(0.5)
            continue
        try:
            page = base.api(app["port"], ui_endpoint)
        except TimeoutError as error:
            if not startup:
                raise
            # First UI work can still be queued after /app/info is available.
            # Use the original readiness deadline and unchanged 3s request
            # timeout; an already-running survivor never retries this failure.
            last = f"UI GET {ui_endpoint}: {type(error).__name__}: {error}"
            ui_timeout_retries += 1
            time.sleep(0.5)
            continue
        break
    else:
        raise TimeoutError(f"{app['role']} installed startup failed: {last}")
    require(b"<html" in page.lower() and b"<script" in page.lower(), "Installed application did not serve its actual UI")
    if app["role"] == "client":
        context = json.loads(base.api(app["port"], "/remote-access/context"))["data"]
        require(context["clientMode"] == 2 and context["serverReachable"] is False,
                "Legacy client adopted the unified library host")
        status = json.loads(base.api(app["port"], "/client/status"))
        require(status["code"] == 0, "Legacy client settings API failed")
        settings = status["data"]
        require(settings["deviceName"] == CLIENT_DEVICE_NAME and settings["servers"] == [] and
                settings.get("activeServerId") is None and settings["serverReachable"] is False,
                "Legacy client API did not read its retained persistent settings")
    else:
        settings = None
    app.setdefault("diagnosticPids", []).extend(pid for pid in pids if pid not in app.get("diagnosticPids", []))
    return {"passed": True, "role": app["role"], "executable": str(app["exe"]), "processIds": pids,
            "port": app["port"], "effectiveDataDirectory": str(app["data"]), "appInfo": info, "uiBytes": len(page),
            "clientSettingsApi": settings, "startupUiTimeoutRetries": ui_timeout_retries}


def start_app(app, label):
    """Start an installed product. Future updater tests can use the same app dict."""
    require(not base.native_processes(app["exe"]), "Restart requested while the product is still running")
    if app["rid"].startswith("osx-"):
        base.command(["open", "-n", app["installRoot"]], app["results"] / (label + "-open.log"), app["environment"])
    else:
        log = (app["results"] / (label + "-app.log")).open("wb")
        app["childLogs"].append(log)
        child = subprocess.Popen([str(app["exe"])], cwd=app["exe"].parent, env=app["environment"],
                                 stdout=log, stderr=subprocess.STDOUT)
        app["children"].append(child)
    return observe_app(app, startup=True)


def stop_app(app):
    stopped = base.stop_native(app["exe"])
    for child in app["children"]:
        try:
            child.wait(timeout=10)
        except subprocess.TimeoutExpired:
            child.kill()
            child.wait(timeout=10)
    return stopped


def audit_installed(app):
    content = app["exe"].parent
    base.validate_manifest(base.read_manifest((content / "sq.version").read_bytes()), app["role"], app["rid"], app["version"])
    for filename, expected in app["packageAudit"]["binaryHashes"].items():
        require(base.sha256(content / filename) == expected, "Installed executable payload differs from audited package")
    return base.contract.check_publish(content, app["role"], require_web=app["role"] == "unified")


def install_app(app, label, audit=None):
    require(not app["installRoot"].exists(), "Install target already exists")
    app["installationAttempted"] = True
    installer = app["packages"] / app["packageAudit"]["artifacts"]["installer"]["file"]
    if app["rid"].startswith("osx-"):
        base.command(["sudo", "/usr/bin/env", "USER=" + os.environ["USER"], "/usr/sbin/installer",
                      "-pkg", installer, "-target", "/", "-verboseR"],
                     app["results"] / (label + "-installer.log"), app["environment"])
        mechanism = "original-pkg-system-install-with-postinstall-automatic-launch"
        startup = observe_app(app, startup=True)
    else:
        # No installto or AppData override: verify the real product defaults.
        base.command([installer, "--silent", "--log", app["results"] / (label + "-setup-native.log")],
                     app["results"] / (label + "-installer.log"), app["environment"])
        require((app["installRoot"] / "Update.exe").is_file(), "Original Setup did not install its updater")
        mechanism = "original-setup-silent-default-install-root"
        startup = start_app(app, label)
    return {"mechanism": mechanism, "target": str(app["installRoot"]), "startup": startup,
            "contentAudit": (audit or audit_installed)(app), "passed": True}


def remove_app(app, label):
    """Windows uses its original uninstaller; macOS removes only the owned bundle."""
    stopped = stop_app(app)
    if app["rid"].startswith("osx-"):
        base.command(["sudo", "rm", "-rf", "--", app["installRoot"]], app["results"] / (label + "-remove-bundle.log"))
        for index, receipt in enumerate(app["receipts"]):
            if subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True, timeout=20).returncode == 0:
                base.command(["sudo", "pkgutil", "--forget", receipt], app["results"] / f"{label}-forget-{index}.log")
        mechanism = "owned-macos-bundle-removal-and-receipt-forget"
    else:
        updater = app["installRoot"] / "Update.exe"
        require(updater.is_file(), "Native Windows uninstaller is missing")
        base.command([updater, "--silent", "--log", app["results"] / (label + "-uninstall-native.log"), "uninstall"],
                     app["results"] / (label + "-uninstaller.log"), app["environment"])
        base.wait_absent(app["installRoot"], 30)
        mechanism = "original-windows-update-exe-uninstall"
    require(not app["installRoot"].exists() and not base.native_processes(app["exe"]), "Product removal was incomplete")
    require(app["data"].is_dir(), "Product removal deleted external default AppData")
    return {"mechanism": mechanism, "nativeUninstaller": app["rid"] == "win-x64",
            "stoppedProcessIds": stopped, "externalDataPreserved": True, "passed": True}


def verify_coexistence(apps, label, expected_client_state, resource_id):
    require_separate_apps(apps)
    observations = {role: observe_app(apps[role]) for role in ROLES}
    require(set(observations["client"]["processIds"]).isdisjoint(observations["unified"]["processIds"]),
            "The two product APIs resolved to the same process")
    return {"label": label, "apps": observations,
            "clientSettingsHashes": verify_client_state(apps["client"], expected_client_state),
            "unifiedDatabase": database_state(apps["unified"], resource_id), "passed": True}


def require_same_process(before, after):
    require(set(before["processIds"]) == set(after["processIds"]), "The other product was interrupted or replaced")


def exercise_coexistence(apps, report, feed=None):
    client, unified = apps["client"], apps["unified"]
    report["currentStage"] = "initial-client-install"
    report["clientFirstInstall"] = install_app(client, "initial")
    client_before = observe_app(client)
    expected_client = client_state(client)
    report["clientSettingsBaseline"] = expected_client
    report["currentStage"] = "initial-unified-install"
    report["unifiedSecondInstall"] = install_app(unified, "initial")
    require_same_process(client_before, observe_app(client))
    terms = json.loads(base.api(unified["port"], "/app/terms", {}))
    require(terms["code"] == 0, "Could not accept unified application terms")
    created = json.loads(base.api(unified["port"], "/resource/placeholder",
                                 {"items": [{"title": "Installed coexistence retained resource"}], "acquireImmediately": False}))
    require(created["code"] == 0 and len(created["data"]) == 1, "Could not create unified resource")
    item = created["data"][0]
    resource_id = item.get("resourceId")
    require(type(resource_id) is int and resource_id > 0 and not item.get("error"), "Resource persistence failed")
    report["resourceId"] = resource_id
    report["currentStage"] = "initial-coexistence-checkpoint"
    report["checkpoints"] = [verify_coexistence(apps, "both-installed", expected_client, resource_id)]

    for restarted, survivor in ((client, unified), (unified, client)):
        report["currentStage"] = "manual-restart-" + restarted["role"]
        before = observe_app(survivor)
        old_pids = stop_app(restarted)
        require_same_process(before, observe_app(survivor))
        after = start_app(restarted, "restart")
        require(set(old_pids).isdisjoint(after["processIds"]), "Restart did not produce a new native process")
        require_same_process(before, observe_app(survivor))
        report["checkpoints"].append(verify_coexistence(apps, restarted["role"] + "-restarted", expected_client, resource_id))

    if feed is not None:
        updates.run(apps, report, feed, SimpleNamespace(observe_app=observe_app, verify_coexistence=verify_coexistence))
    report["currentStage"] = "remove-unified-client-survives"
    before = observe_app(client)
    if feed is not None:
        feed.deactivate("unified")
        unified["version"], unified["packageAudit"] = unified["originalVersion"], unified["originalPackageAudit"]
    report["removeUnified"] = remove_app(unified, "remove-for-client-survival")
    require_same_process(before, observe_app(client))
    verify_client_state(client, expected_client)
    report["unifiedDataAfterRemoval"] = database_state(unified, resource_id)
    stop_app(client)
    report["clientAfterUnifiedRemoval"] = start_app(client, "survivor-restart")
    verify_client_state(client, expected_client)

    before = observe_app(client)
    report["currentStage"] = "restore-original-unified"
    report["restoreUnified"] = install_app(unified, "restore")
    require_same_process(before, observe_app(client))
    report["checkpoints"].append(verify_coexistence(apps, "unified-restored", expected_client, resource_id))
    before = observe_app(unified)
    report["currentStage"] = "remove-client-unified-survives"
    report["removeClient"] = remove_app(client, "remove-for-unified-survival")
    require_same_process(before, observe_app(unified))
    report["clientSettingsAfterRemoval"] = verify_client_state(client, expected_client)
    stop_app(unified)
    report["unifiedAfterClientRemoval"] = start_app(unified, "survivor-restart")
    report["finalUnifiedDatabase"] = database_state(unified, resource_id)
    report["coexistencePassed"] = True


def registered_products():
    """Read only the two products' exact Windows uninstall keys."""
    script = "$roots=@('HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall','HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall','HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall'); @($roots | ForEach-Object { $r=$_; @('Bakabase','Bakabase.Client') | ForEach-Object { $p=Join-Path $r $_; if(Test-Path -LiteralPath $p){$p} } }) | ConvertTo-Json -Compress"
    output = subprocess.check_output(["powershell", "-NoProfile", "-Command", script], text=True, timeout=20).strip()
    values = json.loads(output) if output else []
    return values if isinstance(values, list) else [values]


def execute(args, results, report, *, package_auditor=None, feed_factory=None, exercise=None):
    started_epoch = time.time()
    report["currentStage"] = "preflight"
    paths = default_paths(args.rid, Path.home(), os.environ)
    require_pristine(paths["absent"], os.environ)
    mac = args.rid.startswith("osx-")
    require(shutil.disk_usage(os.environ["RUNNER_TEMP"]).free >= 5 * 1024 ** 3 and
            shutil.disk_usage(Path.home()).free >= 5 * 1024 ** 3, "Lifecycle acceptance requires 5 GiB free per working volume")
    if not mac:
        require(not registered_products(), "Runner already has a registered Bakabase installation")
    report["preflightAbsentPaths"] = [str(path) for path in paths["absent"]]
    blocked, launchd_environment, apps, cleanup_errors = [], {}, {}, []
    work = Path(tempfile.mkdtemp(prefix="bakabase-installed-lifecycle-", dir=os.environ["RUNNER_TEMP"])).resolve()

    class Denier(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            if len(blocked) < 200:
                # Drop query strings so background logs cannot include credentials.
                blocked.append({"method": self.command, "target": self.path.split("?", 1)[0][:512]})
            self.send_error(503, "Installed lifecycle blocks external background requests")
        do_POST = do_GET
        do_CONNECT = do_GET
        def log_message(self, *_):
            pass

    proxy = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Denier)
    thread = threading.Thread(target=proxy.serve_forever, daemon=True)
    thread.start()
    url = f"http://127.0.0.1:{proxy.server_port}"
    isolated = {"BAKABASE_UPDATE_URL": url, "BAKABASE_CLIENT_UPDATE_URL": url,
                "Analytics__Sentry__BackendDsn": "", "Analytics__Sentry__ClientDsn": "",
                "HTTP_PROXY": url, "HTTPS_PROXY": url, "ALL_PROXY": url,
                "http_proxy": url, "https_proxy": url, "all_proxy": url,
                "NO_PROXY": "localhost,127.0.0.1,::1", "no_proxy": "localhost,127.0.0.1,::1"}
    feed, authorization = None, None
    try:
        if getattr(args, "updates_manifest", None):
            factory = feed_factory or sibling("installed-update-feed").Feed
            feed = factory(args.updates_manifest, args.rid, args.version)
            isolated.update(feed.environment)
        environment = dict(os.environ, **isolated)
        if mac:
            for key in FORBIDDEN_ENV:
                prior = subprocess.run(["launchctl", "getenv", key], capture_output=True, text=True, timeout=20)
                require(not prior.stdout.strip(), "LaunchServices contains a data override or startup hook")
            for key, value in isolated.items():
                prior = subprocess.run(["launchctl", "getenv", key], capture_output=True, text=True, timeout=20)
                launchd_environment[key] = prior.stdout.rstrip("\n") if prior.returncode == 0 else None
                subprocess.run(["launchctl", "setenv", key, value], check=True, timeout=20)
        for role in ROLES:
            packages = getattr(args, role + "_packages").resolve()
            audit = (package_auditor or base.audit_packages)(packages, role, args.rid, args.version)
            product = base.contract.PRODUCTS[role]
            installed = Path("/Applications") / audit["bundleName"] if mac else Path(os.environ["LOCALAPPDATA"]) / product["assembly"]
            role_results = results / role
            role_results.mkdir()
            app = {"role": role, "rid": args.rid, "exe": installed / "Contents/MacOS" / product["assembly"] if mac else installed / "current" / (product["assembly"] + ".exe"),
                   "data": paths["data"][role], "port": None, "version": args.version, "environment": dict(environment),
                   "results": role_results, "installRoot": installed, "packages": packages, "packageAudit": audit,
                   "receipts": [], "children": [], "childLogs": [], "installationAttempted": False}
            apps[role] = app
            if mac:
                expanded = work / (role + "-expanded")
                installer = packages / audit["artifacts"]["installer"]["file"]
                base.command(["pkgutil", "--expand-full", installer, expanded], role_results / "expand.log")
                for info in expanded.rglob("PackageInfo"):
                    receipt = ET.parse(info).getroot().get("identifier")
                    require(receipt == product["bundle"], "Unexpected native installer receipt identity")
                    require(subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True, timeout=20).returncode != 0,
                            "Runner already has the product's installer receipt")
                    app["receipts"].append(receipt)
                require(len(app["receipts"]) == 1, "Expected one native product receipt")
        # Both products and receipts have passed preflight before creating defaults.
        with contextlib.ExitStack() as reserved:
            for role, app in apps.items():
                app["port"] = base.prepare_data(app["data"], role)
                config_path = app["data"] / "app.json"
                config = json.loads(config_path.read_text())
                config["App"]["enablePreReleaseChannel"] = False
                config_path.write_text(json.dumps(config))
                # Keep earlier chosen ports reserved until both configs exist.
                sock = reserved.enter_context(socket.socket())
                sock.bind(("127.0.0.1", app["port"]))
        require_separate_apps(apps)
        (apps["client"]["data"] / "client/connection.json").write_text(
            json.dumps({"DeviceName": CLIENT_DEVICE_NAME, "Servers": [], "ActiveServerId": None}) + "\n")
        report["apps"] = {role: {"role": role, "rid": app["rid"], "executable": str(app["exe"]), "defaultData": str(app["data"]),
                                 "port": app["port"], "packageAudit": app["packageAudit"], "receiptIds": app["receipts"]}
                          for role, app in apps.items()}
        if getattr(args, "macos_native_authorization", False):
            require(mac and feed is not None, "Native authorization requires macOS installed updater acceptance")
            report["currentStage"] = "prepare-native-update-authorization"
            authorization_module = sibling("installed-macos-authorization")
            try:
                authorization = authorization_module.prepare(apps, results)
            except authorization_module.PreparationFailure as error:
                authorization = error.context
                report["nativeAuthorizationSetup"] = authorization.evidence
                raise
            report["nativeAuthorizationSetup"] = authorization.evidence
            for app in apps.values():
                app["nativeAuthorization"] = (authorization_module, authorization)
        (exercise or exercise_coexistence)(apps, report, feed)
    except (Exception, KeyboardInterrupt) as error:
        report["failureBeforeCleanup"] = {"stage": report.get("currentStage"), "type": type(error).__name__, "message": str(error)}
        try:
            report["failureDiagnostics"] = sibling("installed-lifecycle-diagnostics").capture(apps, results, started_epoch)
        except Exception as diagnostic_error:
            report["failureDiagnostics"] = {"errors": [str(diagnostic_error)]}
        raise
    finally:
        def cleanup(label, action):
            try:
                action()
            except Exception as error:
                cleanup_errors.append(f"{label}: {error}")

        report["updaterCleanup"] = {"passed": False, "remainingProcesses": [{"verification": "not completed"}]}
        cleanup("Stop native updaters and preserve default logs", lambda: updates.cleanup(apps, report))
        updater_cleanup = report.get("updaterCleanup", {})
        can_remove = "remainingProcesses" in updater_cleanup and not updater_cleanup["remainingProcesses"]
        if authorization is not None:
            report["nativeAuthorizationCleanup"] = {"passed": False, "remainingProcesses": [{"verification": "not completed"}]}
            cleanup("Remove native authorization fixture", lambda: report.update(
                nativeAuthorizationCleanup=authorization_module.cleanup(authorization)))
            authorization_cleanup = report["nativeAuthorizationCleanup"]
            can_remove = (can_remove and authorization_cleanup.get("canRemoveOwnedPaths") is True and
                          "remainingProcesses" in authorization_cleanup and not authorization_cleanup["remainingProcesses"])
            if authorization_cleanup.get("passed") is not True:
                cleanup_errors.append("Native authorization fixture cleanup did not pass")
        for role, app in apps.items():
            cleanup("Stop " + role, lambda: stop_app(app))
            for log in app["childLogs"]:
                cleanup("Close " + role + " log", log.close)
            def preserve_logs():
                if not app["data"].exists():
                    return
                destination = app["results"] / "application-logs"
                destination.mkdir()
                for index, path in enumerate(sorted(app["data"].rglob("*.log"))[:20]):
                    with path.open("rb") as stream:
                        stream.seek(max(0, path.stat().st_size - 1024 * 1024))
                        (destination / f"{index}-{path.name}").write_bytes(stream.read())
            cleanup("Preserve " + role + " logs", preserve_logs)
            if can_remove and app["installationAttempted"] and app["installRoot"].exists():
                cleanup("Remove " + role, lambda: report.setdefault("cleanupRemovals", {}).update({role: remove_app(app, "cleanup")}))
            if mac and can_remove:
                for receipt in app["receipts"]:
                    def forget():
                        if subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True, timeout=20).returncode == 0:
                            base.command(["sudo", "pkgutil", "--forget", receipt], app["results"] / "cleanup-forget.log")
                        require(subprocess.run(["pkgutil", "--pkg-info", receipt], capture_output=True, timeout=20).returncode != 0,
                                "Native receipt remains")
                    cleanup("Forget " + role + " receipt", forget)
        report["createdDefaultPaths"] = [str(path) for path in paths["absent"] if path.exists()]
        for index, path in enumerate(paths["absent"]):
            def remove_default():
                require(can_remove, "Files retained because a native updater is still running")
                if path.exists() or path.is_symlink():
                    if mac:
                        base.command(["sudo", "rm", "-rf", "--", path], results / f"cleanup-{index}.log")
                    elif path.is_file() or path.is_symlink():
                        path.unlink()
                    else:
                        base.remove_owned_tree(path)
            cleanup("Remove owned default " + str(path), remove_default)
        if can_remove:
            cleanup("Remove work directory", lambda: base.remove_owned_tree(work))
        for key, prior in launchd_environment.items():
            cleanup("Restore launch environment " + key,
                    lambda: subprocess.run(["launchctl", "unsetenv", key] if prior is None else
                                           ["launchctl", "setenv", key, prior], check=True, capture_output=True, timeout=20))
        cleanup("Stop background denier", proxy.shutdown)
        cleanup("Close background denier", proxy.server_close)
        if feed is not None:
            report["updateFeed"] = {"deliveries": feed.deliveries, "requests": feed.requests}
            cleanup("Close update feed", feed.close)
        thread.join(timeout=5)
        report["blockedBackgroundRequests"] = blocked
        report["ownedFilesRemoved"] = not work.exists() and not any(path.exists() or path.is_symlink() for path in paths["absent"])
        if not report["ownedFilesRemoved"]:
            cleanup_errors.append("Owned files remain after cleanup")
        if not mac:
            cleanup("Verify Windows uninstall registry", lambda: require(not registered_products(), "Product uninstall keys remain"))
        report["cleanupErrors"] = cleanup_errors
        require(not cleanup_errors, "Installed lifecycle cleanup failed: " + "; ".join(cleanup_errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unified-packages", required=True, type=Path)
    parser.add_argument("--client-packages", required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=("win-x64", "osx-x64", "osx-arm64"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--results-directory", required=True, type=Path)
    parser.add_argument("--updates-manifest", type=Path, help="Verified same-code update manifest; enables real automatic restarts")
    parser.add_argument("--macos-native-authorization", action="store_true",
                        help="Use an ephemeral hosted-runner administrator for the actual macOS update authorization dialogs")
    args = parser.parse_args()
    base.require_hosted_runner(os.environ, platform.system(), platform.machine(), args.rid)
    results = args.results_directory.resolve()
    require(not results.exists(), "Results must be a new directory")
    results.mkdir(parents=True)
    report = {"passed": False, "rid": args.rid, "packageVersion": args.version,
              "automaticUpdatesRequested": args.updates_manifest is not None,
              "executionHeadSHA": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=base.ROOT, text=True).strip(),
              "scope": "legacy-client-first-installed-coexistence-and-removal",
              "limitations": ["Package source provenance is provided by the consuming workflow; execution SHA describes this test script.",
                              "Background containment uses own loopback feeds and a rejecting proxy, not an OS network sandbox.",
                              "No production signing, notarization, Gatekeeper or SmartScreen trust is validated.",
                              "macOS removal deletes the owned bundle and receipt; macOS has no native product uninstaller in this gate."]}
    try:
        execute(args, results, report)
        report["passed"] = True
    except (Exception, KeyboardInterrupt) as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        (results / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(signum, _frame):
        raise KeyboardInterrupt(f"Interrupted by signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
