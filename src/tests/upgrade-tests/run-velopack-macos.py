#!/usr/bin/env python3
"""Real, isolated macOS Velopack application upgrade (no system installation).

Inputs are actual vpk artifacts; only UpdateMac may replace the running bundle.
The startup hook isolates Velopack cache/log paths and disables automatic
LaunchServices restart. All application APIs and update/download code are real.
"""
import argparse
import hashlib
import http.server
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
import threading
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


def digest(path, algorithm="sha256"):
    h = hashlib.new(algorithm)
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest().upper()


def unpack_portable(source, destination):
    """Accept vpk's internal relative symlinks, never archive traversal."""
    with zipfile.ZipFile(source) as archive:
        entries = archive.infolist()
        if len(entries) > 20000 or sum(e.file_size for e in entries) > 700 * 1024 * 1024:
            raise ValueError("Portable archive exceeds fixture budget")
        for entry in entries:
            name = PurePosixPath(entry.filename)
            if name.parts and name.parts[0] == "__MACOSX":
                continue  # Finder metadata from vpk's ditto ZIP; never materialized.
            if name.is_absolute() or ".." in name.parts or not name.parts or name.parts[0] != "Bakabase.app":
                raise ValueError("Unsafe portable archive path")
            target = destination.joinpath(*name.parts)
            if not target.resolve().is_relative_to(destination.resolve()):
                raise ValueError("Archive path leaves installation")
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode):
                link = archive.read(entry).decode("utf-8")
                if Path(link).is_absolute() or not (target.parent / link).resolve().is_relative_to(destination.resolve()):
                    raise ValueError("Unsafe portable archive symlink")
                target.parent.mkdir(parents=True, exist_ok=True)
                target.symlink_to(link)
            elif entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as src, target.open("wb") as dst:
                    shutil.copyfileobj(src, dst, 65536)
                target.chmod((mode & 0o777) or 0o644)


def manifest(path):
    return {e.tag.split("}")[-1]: e.text for e in ET.parse(path).iter() if not list(e)}


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def wait_until(action, seconds, description):
    deadline = time.monotonic() + seconds
    last = None
    while time.monotonic() < deadline:
        try:
            result = action()
            if result:
                return result
        except (OSError, ValueError, urllib.error.URLError) as error:
            last = str(error)
        time.sleep(0.25)
    raise TimeoutError("Timed out waiting for %s: %s" % (description, last))


def snapshot_db(db, destination):
    with sqlite3.connect("file:%s?mode=ro" % db, uri=True) as source, sqlite3.connect(destination) as copy:
        source.backup(copy)
        integrity = copy.execute("PRAGMA integrity_check").fetchall()
        if integrity != [("ok",)]:
            raise AssertionError("SQLite integrity failed: %r" % integrity)
        tables = [r[0] for r in copy.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
        counts = {name: copy.execute('SELECT COUNT(*) FROM "%s"' % name.replace('"', '""')).fetchone()[0]
                  for name in tables}
        return {"integrity": "ok", "tables": counts, "snapshotSha256": digest(destination)}


def validate_resource_ids(resources, expected_ids):
    if (not isinstance(resources, list) or len(resources) != 2
            or len(expected_ids) != 2 or len(set(expected_ids)) != 2
            or any(not isinstance(item, dict) or type(item.get("id")) is not int for item in resources)):
        raise AssertionError("Expected exactly two identified resource objects")
    actual_ids = [item["id"] for item in resources]
    if len(set(actual_ids)) != 2 or set(actual_ids) != set(expected_ids):
        raise AssertionError("Resource identities differ from the two created resources")


def validate_database_count(snapshot):
    if snapshot["tables"].get("ResourcesV2") != 2:
        raise AssertionError("SQLite snapshot must contain exactly two real ResourcesV2 rows")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old-portable", type=Path, required=True)
    parser.add_argument("--new-package", type=Path, required=True)
    parser.add_argument("--hook", type=Path, required=True)
    parser.add_argument("--provenance", type=Path, required=True)
    parser.add_argument("--results-directory", type=Path, required=True, help="New directory under /tmp")
    parser.add_argument("--keep-work", action="store_true", help="Retain owned installed/appdata/packages after stopping all processes")
    args = parser.parse_args()
    if platform.system() != "Darwin":
        parser.error("This runner requires macOS; it does not emulate UpdateMac")
    rid = "osx-arm64" if platform.machine() == "arm64" else "osx-x64"
    root = args.results_directory.resolve()
    if not root.is_relative_to(Path("/tmp").resolve()) or root.exists():
        parser.error("Results must be a new directory below /tmp")
    for value in (args.old_portable, args.new_package, args.hook, args.provenance):
        if not value.is_file():
            parser.error("Missing input: %s" % value)
    if shutil.disk_usage(root.parent).free < 1400 * 1024 * 1024:
        parser.error("At least 1.4 GiB free disk required for bounded old/new staging")
    processes = subprocess.check_output(["ps", "-axo", "pid=,command="], text=True)
    if any(".app/Contents/MacOS/Bakabase" in line and "run-velopack-macos.py" not in line for line in processes.splitlines()):
        parser.error("Another native Bakabase is running; close it before using the single-instance fixture")
    root.mkdir(parents=True)
    (root / ".bakabase-upgrade-test").write_text("owned fixture\n")
    for part in ("installed", "appdata", "packages", "temp", "evidence"):
        (root / part).mkdir()
    report = {"passed": False, "platform": platform.platform(), "architecture": platform.machine(),
              "provenance": json.loads(args.provenance.read_text()), "root": str(root),
              "limitations": ["Old source is packaged with a synthetic test version; neither artifact is a historical signed installer.",
                              "Test-only startup hook replaces the default Velopack locator/cache/log paths.",
                              "Real UpdateMac applies the update with --norestart; the runner restarts the new binary with explicit AppData environment.",
                              "No production feed, default user cache, LaunchServices automatic restart, signing, notarization or system installation is tested."]}
    report["limitations"].append("macOS-managed WebKit/SavedState caches are outside the AppData/Velopack isolation scope and are not deleted.")
    children, servers, streams = [], [], []
    try:
        package = args.new_package.resolve()
        with zipfile.ZipFile(package) as archive:
            version_entry = next(n for n in archive.namelist() if n.endswith("/Contents/Resources/sq.version"))
            new_manifest = {e.tag.split("}")[-1]: e.text for e in ET.fromstring(archive.read(version_entry)).iter() if not list(e)}
            dll_entry = next(n for n in archive.namelist() if n.endswith("/Contents/MacOS/Bakabase.Service.dll"))
            expected_dll = hashlib.sha256(archive.read(dll_entry)).hexdigest().upper()
        if new_manifest["id"] != "Bakabase" or new_manifest["mainExe"] != "Bakabase" or new_manifest.get("rid") != rid:
            raise ValueError("Unexpected new package identity")
        unpack_portable(args.old_portable, root / "installed")
        app = root / "installed/Bakabase.app/Contents/MacOS"
        old_manifest = manifest(app / "sq.version")
        if old_manifest["id"] != "Bakabase" or old_manifest["version"] == new_manifest["version"]:
            raise ValueError("Expected a distinct real Bakabase old package")
        report.update(oldManifest=old_manifest, newManifest=new_manifest,
                      oldPortableSha256=digest(args.old_portable), newPackageSha256=digest(package),
                      oldServiceSha256=digest(app / "Bakabase.Service.dll"), expectedNewServiceSha256=expected_dll)
        asset = {"PackageId": "Bakabase", "Version": new_manifest["version"], "Type": "Full",
                 "FileName": package.name, "SHA1": digest(package, "sha1"), "SHA256": digest(package), "Size": package.stat().st_size}
        feed = json.dumps({"Assets": [asset]}).encode()
        request_log, proxy_log = [], []
        byte_count = [0]
        feed_lock = threading.Lock()

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                path = urllib.parse.urlsplit(self.path).path
                is_package = path == "/" + rid + "/" + package.name
                allowed = path == "/" + rid + "/releases.osx.json" or is_package
                size = package.stat().st_size if is_package else len(feed)
                with feed_lock:
                    status = 404 if not allowed else 429 if byte_count[0] + size > 256 * 1024 * 1024 else 200
                    if len(request_log) < 1000:
                        request_log.append({"path": path[:1024], "status": status})
                    if status == 200:
                        byte_count[0] += size
                if status != 200:
                    self.send_error(status)
                    return
                self.send_response(200)
                self.send_header("Content-Length", str(size))
                self.end_headers()
                if is_package:
                    with package.open("rb") as src:
                        while True:
                            block = src.read(65536)
                            if not block:
                                break
                            self.wfile.write(block)
                else:
                    self.wfile.write(feed)

            def log_message(self, *_):
                pass

        class DenyProxy(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                if len(proxy_log) < 1000:
                    proxy_log.append({"method": self.command, "target": self.path[:1024]})
                self.send_error(503, "Isolated upgrade fixture blocks background external downloads")

            do_CONNECT = do_GET
            do_POST = do_GET

            def log_message(self, *_):
                pass

        for handler in (Handler, DenyProxy):
            server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
            servers.append(server)
            threading.Thread(target=server.serve_forever, daemon=True).start()
        feed_url = "http://127.0.0.1:%d" % servers[0].server_port
        proxy_url = "http://127.0.0.1:%d" % servers[1].server_port
        port = free_port()
        (root / "appdata/app.json").write_text(json.dumps({"App": {"language": "en-US", "enableAnonymousDataTracking": False,
            "enablePreReleaseChannel": False, "listeningPorts": [port], "autoListeningPortCount": 0, "maxParallelism": 1}}))
        (root / "appdata/data").mkdir()
        sentinel = root / "appdata/data/upgrade-sentinel.txt"
        sentinel.write_text("real UpdateMac external AppData sentinel\n")
        sentinel_hash = digest(sentinel)
        env = os.environ.copy()
        env.update(BAKABASE_UPGRADE_TEST_ROOT=str(root), BAKABASE_DATA_DIR=str(root / "appdata"),
                   BAKABASE_UPDATE_URL=feed_url, DOTNET_STARTUP_HOOKS=str(args.hook.resolve()), TMPDIR=str(root / "temp"),
                   HTTP_PROXY=proxy_url, HTTPS_PROXY=proxy_url, ALL_PROXY=proxy_url,
                   http_proxy=proxy_url, https_proxy=proxy_url, all_proxy=proxy_url,
                   NO_PROXY="localhost,127.0.0.1,::1", no_proxy="localhost,127.0.0.1,::1",
                   Analytics__Sentry__BackendDsn="")
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

        def api(path, method="GET", data=None):
            body = json.dumps(data).encode() if data is not None else None
            request = urllib.request.Request("http://127.0.0.1:%d%s" % (port, path), data=body, method=method,
                                             headers={"Content-Type": "application/json"})
            with opener.open(request, timeout=4) as response:
                result = json.loads(response.read(8 * 1024 * 1024))
            if isinstance(result, dict) and "code" in result and result["code"] != 0:
                raise ValueError("Application API failed: %r" % result)
            return result

        def start(label):
            log = (root / (label + "-app.log")).open("wb")
            streams.append(log)
            child = subprocess.Popen([str(app / "Bakabase")], cwd=app, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            children.append(child)
            def ready():
                if child.poll() is not None:
                    raise RuntimeError("%s app exited %s; see its log" % (label, child.returncode))
                return api("/app/info")
            info = wait_until(ready, 120, label + " real application startup")["data"]
            if Path(info["appDataPath"]).resolve() != root / "appdata":
                raise AssertionError("AppData escaped fixture")
            def migrated():
                options = json.loads((root / "appdata/app.json").read_text(encoding="utf-8-sig"))["App"]
                return options.get("version") == info["coreVersion"]
            wait_until(migrated, 120, label + " database migrations and persisted core version")
            report[label + "AppInfo"] = info
            return child

        print("Starting actual old application", flush=True)
        old = start("old")
        api("/app/terms", "POST")
        created = api("/resource/placeholder", "POST", {"items": [{"title": "Upgrade retained alpha"}, {"title": "Upgrade retained beta"}], "acquireImmediately": False})["data"]
        if len(created) != 2 or any(not item.get("resourceId") or item.get("error") for item in created):
            raise AssertionError("Real application fixture creation failed: %r" % created)
        ids = [item["resourceId"] for item in created]
        keys = "/resource/keys?" + urllib.parse.urlencode([("ids", i) for i in ids])
        before = api(keys)["data"]
        validate_resource_ids(before, ids)
        report["createdResources"] = created
        report["resourcesBefore"] = before
        report["oldDatabase"] = snapshot_db(root / "appdata/bakabase_insideworld.db", root / "evidence/old.sqlite")
        validate_database_count(report["oldDatabase"])
        version = api("/updater/app/new-version")["data"]
        report["updateCheck"] = version
        if version.get("version") != new_manifest["version"] or version.get("installedVersion") != old_manifest["version"]:
            raise AssertionError("Real updater did not identify expected old/new versions: %r" % version)
        print("Real application downloading local full package", flush=True)
        api("/updater/app/update", "POST")
        downloaded = root / "packages" / package.name
        def download_done():
            return downloaded.exists() and downloaded.stat().st_size == package.stat().st_size and digest(downloaded) == asset["SHA256"]
        wait_until(download_done, 120, "real UpdateManager download and checksum")
        wait_until(lambda: "Full release download complete" in (root / "velopack-managed.log").read_text(), 10, "UpdateManager completion")
        time.sleep(0.5)
        print("Requesting real UpdateMac application replacement", flush=True)
        try:
            api("/updater/app/restart", "POST")
        except (OSError, ValueError):
            pass  # Environment.Exit may close the triggering HTTP request.
        old.wait(timeout=30)
        wait_until(lambda: (app / "sq.version").exists() and manifest(app / "sq.version").get("version") == new_manifest["version"], 90, "UpdateMac replacement")
        wait_until(lambda: "applied successfully." in (root / "velopack-native.log").read_text(), 30, "native updater completion")
        if digest(app / "Bakabase.Service.dll") != expected_dll:
            raise AssertionError("Updated executable content differs from actual package")
        report["newServiceSha256"] = digest(app / "Bakabase.Service.dll")
        print("Starting replaced application and checking retained SQLite resources", flush=True)
        start("new")
        after = api(keys)["data"]
        validate_resource_ids(after, ids)
        report["resourcesAfter"] = after
        if before != after:
            raise AssertionError("Resources changed across upgrade: %r -> %r" % (before, after))
        if digest(sentinel) != sentinel_hash:
            raise AssertionError("External AppData sentinel changed")
        report["newDatabase"] = snapshot_db(root / "appdata/bakabase_insideworld.db", root / "evidence/new.sqlite")
        validate_database_count(report["newDatabase"])
        report.update(resourcesPreserved=ids, sentinelSha256=sentinel_hash, passed=True)
    except Exception as error:
        report["error"] = "%s: %s" % (type(error).__name__, error)
        print(report["error"], flush=True)
    finally:
        for child in children:
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=5)
        updater_events = root / "updater-processes.jsonl"
        if updater_events.exists():
            report["updaterProcesses"] = [json.loads(line) for line in updater_events.read_text().splitlines()]
            for process in report["updaterProcesses"]:
                pid = process["pid"]
                command = subprocess.run(["ps", "-p", str(pid), "-o", "command="], capture_output=True, text=True).stdout
                if str(root / "installed") in command and "UpdateMac" in command:
                    os.kill(pid, signal.SIGTERM)
                    report["passed"] = False
                    report["error"] = "Updater did not exit naturally; terminated owned updater"
                    for _ in range(40):
                        command = subprocess.run(["ps", "-p", str(pid), "-o", "command="], capture_output=True, text=True).stdout
                        if str(root / "installed") not in command or "UpdateMac" not in command:
                            break
                        time.sleep(0.1)
                    if str(root / "installed") in command and "UpdateMac" in command:
                        os.kill(pid, signal.SIGKILL)
                        time.sleep(0.2)
                        command = subprocess.run(["ps", "-p", str(pid), "-o", "command="], capture_output=True, text=True).stdout
                process["exited"] = str(root / "installed") not in command or "UpdateMac" not in command
                if not process["exited"]:
                    report["passed"] = False
                    report["error"] = "Owned updater process did not stop"
        for server in servers:
            server.shutdown()
            server.server_close()
        for stream in streams:
            stream.close()
        report["applicationProcessesExited"] = all(child.poll() is not None for child in children)
        if "request_log" in locals():
            report.update(feedRequests=request_log, feedBytes=byte_count[0], blockedBackgroundRequests=proxy_log)
        if not args.keep_work:
            for part in ("installed", "packages", "appdata", "temp"):
                shutil.rmtree(root / part)
        report["workDirectoriesRemoved"] = not args.keep_work
        (root / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
        print("Evidence: %s" % (root / "report.json"), flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    def interrupted(_signal, _frame):
        raise KeyboardInterrupt("Upgrade fixture interrupted")
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
