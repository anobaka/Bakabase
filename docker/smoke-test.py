#!/usr/bin/env python3
"""Start a native image twice against one disposable host directory (no user data)."""
import argparse
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

PROGRESS_PATH = "/app/data-path/import/progress"
PROGRESS_HEADER = "X-Bakabase-Import-Token"
SETUP_HEADER = "X-Bakabase-Setup-Token"


def docker(*args, check=True):
    return subprocess.run(["docker", *args], text=True, capture_output=True, check=check).stdout.strip()


def print_failure(container):
    print(docker("inspect", "--format", "{{json .State}}", container, check=False))
    print(docker("logs", container, check=False))


def request(base, path, body=None, headers=None, method=None):
    payload = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=payload, method=method,
                                 headers={"Content-Type": "application/json", **(headers or {})})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=5) as response:
        return response.read()


def expect_status(status, base, path, body=None, headers=None):
    try:
        request(base, path, body, headers)
    except urllib.error.HTTPError as error:
        if error.code != status:
            raise RuntimeError(f"Expected HTTP {status} for {path}, got {error.code}") from error
    else:
        raise RuntimeError(f"Expected HTTP {status} for {path}, got success")


def wait_for_import(container, base, token, operation_id, timeout, require_copy=True):
    deadline = time.monotonic() + timeout
    phases = []
    while time.monotonic() < deadline:
        try:
            progress = json.loads(request(base, PROGRESS_PATH + "/status", headers={PROGRESS_HEADER: token}))
        except urllib.error.HTTPError as error:
            if error.code != 503:
                raise RuntimeError(f"Monitoring lost authorization or its route during restart: HTTP {error.code}") from error
        except OSError:
            if docker("inspect", "--format", "{{.State.Running}}", container) != "true":
                raise RuntimeError("Import container exited before reporting completion")
        else:
            if progress["id"] != operation_id:
                raise RuntimeError("The monitoring token returned another import's state")
            if not phases or phases[-1] != progress["phase"]:
                phases.append(progress["phase"])
            if progress["phase"] == "failed":
                raise RuntimeError(f"Import failed: {progress.get('error')}")
            if progress["phase"] == "completed":
                if not progress.get("lastActivityAtUtc") or progress.get("failedPhase") is not None:
                    raise RuntimeError("Completed operation did not report its final worker activity correctly")
                if require_copy and (progress["totalBytes"] <= 0 or progress["completedBytes"] != progress["totalBytes"]):
                    raise RuntimeError("Completed import has inaccurate byte totals")
                if require_copy and (progress["totalFiles"] <= 0 or progress["completedFiles"] != progress["totalFiles"]):
                    raise RuntimeError("Completed import has inaccurate file totals")
                return phases
        time.sleep(0.1)
    raise TimeoutError("Import did not finish and start the main service before the deadline")


def wait_for_app(container, base, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if docker("inspect", "--format", "{{.State.Running}}", container) != "true":
            raise RuntimeError("Container exited before serving /app/info")
        try:
            info = json.loads(request(base, "/app/info"))["data"]
            if info.get("appDataPath") != "/data":
                raise RuntimeError(f"Image did not use the mounted appdata: {info}")
            return info
        except (OSError, KeyError):
            time.sleep(1)
    raise TimeoutError("Container did not serve /app/info before the deadline")


def initialize_empty(container, base, data, timeout):
    deadline = time.monotonic() + timeout
    setup_file = data / ".bakabase-server-setup.json"
    while time.monotonic() < deadline:
        try:
            setup_token = json.loads(setup_file.read_text())["token"]
            status = json.loads(request(base, "/setup/status", headers={SETUP_HEADER: setup_token}))
            break
        except (OSError, KeyError, json.JSONDecodeError):
            if docker("inspect", "--format", "{{.State.Running}}", container) != "true":
                raise RuntimeError("Container exited before first-run setup became available")
            time.sleep(0.1)
    else:
        raise TimeoutError("The first-run setup endpoint did not become available")
    if status["mode"] != "first-run" or status["canChooseTargetPath"] or not status["isDocker"]:
        raise RuntimeError("Docker setup did not keep its deployment data path fixed")
    if status["currentPath"] != "/data" or (data / "app.json").exists() or list(data.glob("*.db")):
        raise RuntimeError("The server created application data before explicit setup")
    expect_status(401, base, "/setup/status")
    expect_status(401, base, "/setup/local-session", body={})
    expect_status(401, base, "/setup/apply", body={})
    expect_status(401, base, "/setup/directories?path=/data")
    directory_headers = {SETUP_HEADER: setup_token}
    listing = json.loads(request(base, "/setup/directories", headers=directory_headers))
    if (not listing.get("isRestricted") or listing["currentPath"] != "" or listing["parentPath"] is not None
            or not any(entry["path"] == "/data" for entry in listing["roots"])
            or listing["directories"] != listing["roots"]
            or any(entry["path"] == "/" for entry in listing["roots"])):
        raise RuntimeError("The setup directory browser did not expose mounted storage roots")
    expect_status(400, base, "/setup/directories?path=/", headers=directory_headers)
    mounted = json.loads(request(base, "/setup/directories?path=/data", headers=directory_headers))
    if mounted["currentPath"] != "/data" or mounted["parentPath"] is not None:
        raise RuntimeError("The setup directory browser escaped the data mount through its parent")
    candidate_name = "smoke-directory-selection"
    query = urllib.parse.urlencode({"path": "/data", "newFolderName": candidate_name})
    candidate = json.loads(request(base, "/setup/directories?" + query, headers=directory_headers))
    if candidate["currentPath"] != "/data" or candidate["candidatePath"] != "/data/" + candidate_name:
        raise RuntimeError("The setup directory browser did not resolve a prospective child directory")
    if (data / candidate_name).exists():
        raise RuntimeError("Browsing a prospective setup directory created it before confirmation")
    expect_status(404, base, "/setup/directories?path=/data/" + candidate_name, headers=directory_headers)
    expect_status(400, base, "/setup/directories?path=/data&newFolderName=../escape", headers=directory_headers)
    invalid = json.loads(request(base, "/setup/validate", {"targetPath": "/another-data"},
                                 headers={SETUP_HEADER: setup_token}))
    if invalid["valid"]:
        raise RuntimeError("Docker setup allowed changing the fixed data mount")
    validation = json.loads(request(base, "/setup/validate", {}, headers={SETUP_HEADER: setup_token}))
    if not validation["valid"]:
        raise RuntimeError(f"Could not validate empty server setup: {validation.get('error')}")
    result = json.loads(request(base, "/setup/apply", {}, headers={SETUP_HEADER: setup_token}))
    if result["requiresRestart"]:
        raise RuntimeError("First-run setup unexpectedly required an external restart")
    wait_for_import(container, base, result["monitorToken"], result["progress"]["id"], timeout, require_copy=False)
    import_status = json.loads(request(base, "/app/data-path/import"))["data"]
    if import_status["progress"] is not None:
        raise RuntimeError("Fresh initialization was incorrectly shown as a previous import")
    expect_status(401, base, "/setup/apply", {}, headers={SETUP_HEADER: setup_token})
    expect_status(401, base, "/setup/apply", {}, headers={SETUP_HEADER: result["monitorToken"]})
    expect_status(401, base, "/setup/directories?path=/data", headers={SETUP_HEADER: setup_token})
    expect_status(401, base, "/setup/directories?path=/data", headers={SETUP_HEADER: result["monitorToken"]})
    return result


def verify_fixed_data_path(base, data):
    route = "/app/data-path/relocation"
    status = json.loads(request(base, route))
    if status["code"] != 0 or status["data"]["supported"]:
        raise RuntimeError("Docker offered runtime relocation of its fixed data mount")
    session = json.loads(request(base, route + "/setup-session", {}))
    if session["code"] == 0:
        raise RuntimeError("Docker issued a setup capability for relocating its fixed data mount")
    cancellation = json.loads(request(base, route, method="DELETE"))
    if cancellation["code"] == 0:
        raise RuntimeError("Docker accepted relocation cancellation for a fixed data mount")
    for name in (".bakabase-relocate.json", ".bakabase-relocate-work", ".bakabase-import.json"):
        if (data / name).exists():
            raise RuntimeError(f"Rejected Docker relocation changed pending operations: {name}")


def verify_frontend(base):
    page = request(base, "/")
    script = re.search(rb'<script\b[^>]*\bsrc="([^"]+)"', page)
    if b"<html" not in page.lower() or script is None:
        raise RuntimeError("The image did not serve the built frontend")
    bundle = request(base, script.group(1).decode())
    if b"Import existing data" not in bundle:
        raise RuntimeError("The frontend bundle is missing the general AppData import entry")
    setup_page = request(base, "/setup")
    if b"Import existing data" not in setup_page:
        raise RuntimeError("The shared setup page is missing the general AppData import entry")
    if b"Choose data storage folder" not in setup_page:
        raise RuntimeError("The shared setup page is missing the accessible directory selector")
    data_section = setup_page.find(b'<section id="dataSection"')
    target_section = setup_page.find(b'<section id="targetSection"')
    review_section = setup_page.find(b'<section id="reviewSection"')
    if data_section < 0 or target_section <= data_section or review_section <= target_section or b'id="keepPath"' not in setup_page:
        raise RuntimeError("The shared setup page is missing the three-step import and directory workflow")


def verify_native_components(container, base, data, architecture):
    # Leave imported Windows components in place; discovery must use the native
    # system tools without renaming or trying to execute these old copies.
    for folder, names in (("ffmpeg", ("ffmpeg.exe", "ffprobe.exe")), ("7z", ("7z.exe", "7zz.exe"))):
        directory = data / "components" / folder
        directory.mkdir(parents=True, exist_ok=True)
        for name in names:
            (directory / name).write_bytes(b"MZ Windows component fixture\n")
            (directory / name).chmod(0o755)
    for component in ("364e3884-4c6f-446f-b72c-1ec84e8da2c2", "7z-archiver-component-service"):
        result = json.loads(request(base, f"/component/{component}/discover", {}))
        if result["code"] != 0:
            raise RuntimeError(f"Native component discovery failed: {component}")
    contexts = read_component_contexts(base)
    for component in ("364e3884-4c6f-446f-b72c-1ec84e8da2c2", "7z-archiver-component-service"):
        context = next(item for item in contexts if item["id"] == component)
        if context["status"] != 2 or context["location"] != "/usr/bin" or not context["version"]:
            raise RuntimeError(f"Component discovery did not select an installed native tool: {context}")
    expected = "arm64" if architecture == "arm64" else "amd64"
    packages = docker("exec", container, "dpkg-query", "-W", "-f=${Package} ${Architecture}\n", "ffmpeg", "7zip-standalone")
    if any(line.split()[-1] != expected for line in packages.splitlines()):
        raise RuntimeError("The image's media packages use the wrong architecture")
    docker("exec", container, "sh", "-c", '''set -eu
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
ffmpeg -v error -f lavfi -i color=c=red:s=32x32:r=1 -t 1 -c:v mpeg4 -threads 1 "$work/test.mp4"
ffprobe -v error -show_entries format=duration -of default=nw=1:nk=1 "$work/test.mp4"
ffmpeg -v error -i "$work/test.mp4" -frames:v 1 -threads 1 "$work/cover.jpg"
7zz a "$work/test.7z" "$work/cover.jpg" >/dev/null
7zz t "$work/test.7z" >/dev/null
test -s "$work/cover.jpg"
''')
    print("PASS: Windows component copies preserved; discovery uses native FFmpeg/7zz, media probing/frame capture and archive verification succeed")


def read_component_contexts(base):
    # SignalR's existing UI hub publishes the same installed-state data used by
    # the configuration page. Long polling avoids a test-only WebSocket library.
    def raw(path, payload=None, method=None):
        req = urllib.request.Request(base + path, data=payload, method=method,
                                     headers={"Content-Type": "text/plain"})
        with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(req, timeout=10) as response:
            return response.read().decode()
    negotiated = json.loads(raw("/hub/ui/negotiate?negotiateVersion=1", b""))
    route = "/hub/ui?id=" + urllib.parse.quote(negotiated["connectionToken"], safe="")
    try:
        raw(route)
        raw(route, b'{"protocol":"json","version":1}\x1e')
        raw(route)
        raw(route, b'{"type":1,"invocationId":"components","target":"GetInitialData","arguments":[]}\x1e')
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            for value in raw(route).split("\x1e"):
                if not value:
                    continue
                message = json.loads(value)
                if message.get("target") == "GetData" and message.get("arguments", [None])[0] == "DependentComponentContext":
                    return message["arguments"][1]
                if message.get("type") == 3 and message.get("invocationId") == "components":
                    raise RuntimeError(f"UI initialization finished without component status: {message}")
        raise RuntimeError("The UI hub did not publish component status")
    finally:
        raw(route, method="DELETE")


def verify_import(image, source, timeout):
    # Simulate a saved older application version in this stopped fixture. The
    # imported database must take the real pre-upgrade snapshot path while the
    # independent Setup parent still owns its process lock.
    app_file = source / "app.json"
    settings = json.loads(app_file.read_text(encoding="utf-8-sig"))
    app = next(value for key, value in settings.items() if key.lower() == "app")
    version_key = next(key for key in app if key.lower() == "version")
    original_version = app[version_key].split("+", 1)[0]
    match = re.fullmatch(r"(.+\.)([1-9][0-9]*)", original_version)
    if "-" not in original_version:
        backup_version = original_version + "-zz-smoke"
    elif match:
        backup_version = match[1] + str(int(match[2]) - 1)
    else:
        raise RuntimeError(f"Cannot create an older-version backup fixture from {original_version}")
    app[version_key] = backup_version
    backup_key = next((key for key in app if key.lower() == "enableautomaticbackup"), "EnableAutomaticBackup")
    app[backup_key] = True
    app_file.write_text(json.dumps(settings))
    # The old server identity/pairing policy changes during the import. The
    # operation capability must remain usable without granting application access.
    configs = source / "configs"
    configs.mkdir(exist_ok=True)
    (configs / "remote-access.json").write_text(json.dumps({"RemoteAccess": {"mode": 1, "requirePairing": True}}))
    source_token = "A" * 64
    (source / ".bakabase-import-status.json").write_text(json.dumps({"token": source_token}))
    with (source / "import-progress-payload.bin").open("wb") as payload:
        payload.truncate(64 * 1024 * 1024)
    private_directory = source / "private-permission-fixture"
    private_directory.mkdir(mode=0o700)
    private_directory.chmod(0o700)
    private_file = private_directory / "connection.json"
    private_file.write_text('{"fixture":true}')
    private_file.chmod(0o600)
    with closing(sqlite3.connect(source / "bakabase_insideworld.db")) as db:
        db.execute("CREATE TABLE SetupPathSmoke(Id INTEGER PRIMARY KEY, Path TEXT)")
        db.executemany("INSERT INTO SetupPathSmoke VALUES(?,?)",
                       [(1, "Y:/Movies/a.mkv"), (2, "Y:/Movies/b.mkv"), (3, "Z:/Music/c.flac")])
        db.commit()
        db.execute("PRAGMA wal_checkpoint(TRUNCATE)")

    def hashes():
        result = {}
        for path in source.rglob("*"):
            if not path.is_file():
                continue
            digest = hashlib.sha256()
            with path.open("rb") as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(block)
            result[str(path.relative_to(source))] = digest.hexdigest()
        return result

    source_hashes = hashes()
    monitor_token = None
    operation_id = None
    with tempfile.TemporaryDirectory(prefix="bakabase-import-") as directory, \
            tempfile.TemporaryDirectory(prefix="bakabase-media-") as media:
        target = Path(directory)
        for iteration in range(2):
            container = "bakabase-import-" + uuid.uuid4().hex[:12]
            try:
                docker("run", "-d", "--name", container,
                       "--user", f"{os.getuid()}:{os.getgid()}", "-e", "HOME=/tmp",
                       "--mount", f"type=bind,source={directory},target=/data",
                       "--mount", f"type=bind,source={source},target=/import,readonly",
                       "--mount", f"type=bind,source={media},target=/media,readonly",
                       "-p", "127.0.0.1::34567", image)
                port = json.loads(docker("inspect", container))[0]["NetworkSettings"]["Ports"]["34567/tcp"][0]["HostPort"]
                base = f"http://127.0.0.1:{port}"
                if iteration == 0:
                    initialize_empty(container, base, target, timeout)
                    wait_for_app(container, base, timeout)
                    (target / "target-before-import.txt").write_text("preserve previous data")
                    payload = {"operation": "import", "sourcePath": "/import", "originalDataPath": "/old/desktop/appdata"}
                    legacy_queue = json.loads(request(base, "/app/data-path/import", payload))
                    legacy_cancel = json.loads(request(base, "/app/data-path/import", method="DELETE"))
                    if legacy_queue["code"] == 0 or legacy_cancel["code"] == 0 or (target / ".bakabase-import.json").exists():
                        raise RuntimeError("A legacy business API changed the parent-owned maintenance operation")
                    session = json.loads(request(base, "/app/data-path/import/setup-session", {}))
                    if session["code"] != 0:
                        raise RuntimeError("The authorized management API could not issue a setup session")
                    setup_token = session["data"]["setupToken"]
                    setup_status = json.loads(request(base, "/setup/status", headers={SETUP_HEADER: setup_token}))
                    if setup_status["allowedOperations"] != ["import"] or setup_status["canChooseTargetPath"]:
                        raise RuntimeError("An existing Docker instance offered an unavailable setup operation")
                    if not setup_status.get("automaticMaintenance"):
                        raise RuntimeError("The server did not expose its automatic Setup coordinator")
                    listing = json.loads(request(base, "/setup/directories", headers={SETUP_HEADER: setup_token}))
                    for mounted_path in ("/import", "/media"):
                        if not any(entry["path"] == mounted_path and entry.get("readOnly") is True
                                   for entry in listing["roots"]):
                            raise RuntimeError("Setup did not expose a read-only persistent mount")
                    for operation in ("initialize", "relocate"):
                        invalid = {"operation": operation}
                        rejected = json.loads(request(base, "/setup/validate", invalid,
                                                      headers={SETUP_HEADER: setup_token}))
                        if rejected["valid"]:
                            raise RuntimeError(f"Existing Docker setup accepted {operation}")
                        expect_status(400, base, "/setup/apply", invalid, headers={SETUP_HEADER: setup_token})
                    changed_target = {**payload, "targetPath": "/tmp/changed-data-path"}
                    rejected = json.loads(request(base, "/setup/validate", changed_target,
                                                  headers={SETUP_HEADER: setup_token}))
                    if rejected["valid"]:
                        raise RuntimeError("Existing Docker import allowed changing the fixed data directory")
                    expect_status(400, base, "/setup/apply", changed_target, headers={SETUP_HEADER: setup_token})
                    if any((target / name).exists() for name in (".bakabase-import.json", ".bakabase-relocate.json")):
                        raise RuntimeError("Rejected setup operations changed pending data operations")
                    validation = json.loads(request(base, "/setup/validate", payload,
                                                    headers={SETUP_HEADER: setup_token}))
                    if not validation["valid"]:
                        raise RuntimeError(f"Could not validate real appdata: {validation}")
                    rules = [{"sourcePrefix": "Y:/", "targetPrefix": "/media"}]
                    setup_headers = {SETUP_HEADER: setup_token}
                    request(base, "/setup/draft", {"request": payload, "rules": rules}, headers=setup_headers)
                    scan = json.loads(request(base, "/setup/preflight", payload, headers=setup_headers))
                    deadline = time.monotonic() + timeout
                    while scan["phase"] == "scanning" and time.monotonic() < deadline:
                        request(base, "/app/info")  # Source inspection must leave business online.
                        time.sleep(.05)
                        scan = json.loads(request(base, "/setup/preflight", headers=setup_headers))
                    if scan["phase"] != "ready":
                        raise RuntimeError(f"Docker preflight failed: {scan}")
                    tree = json.loads(request(base, "/setup/preflight/tree?scanId=" + scan["id"], headers=setup_headers))
                    if not any(node["path"] == "Y:/" and node["referenceCount"] == 2 for node in tree["nodes"]):
                        raise RuntimeError("Docker preflight did not aggregate the source's Windows paths")
                    preview = json.loads(request(base, "/setup/preflight/preview", {"scanId": scan["id"], "rules": rules}, headers=setup_headers))
                    if preview["matchedReferences"] != 2:
                        raise RuntimeError("Docker path preview has the wrong match count")
                    payload.update(pathPreflightId=scan["id"], pathPreviewId=preview["previewId"], pathMappings=rules)
                    status = json.loads(request(base, "/setup/apply", payload, headers={SETUP_HEADER: setup_token}))
                    if status["requiresRestart"]:
                        raise RuntimeError("The Setup coordinator required a manual container restart")
                    monitor_token = status["monitorToken"]
                    operation_id = status["progress"]["id"]
                    if len(monitor_token) != 64 or status["progress"]["phase"] != "queued":
                        raise RuntimeError("Queued import did not provide its monitoring capability")
                if monitor_token:
                    phases = wait_for_import(container, base, monitor_token, operation_id, timeout)
                    expect_status(401, base, "/setup/apply", {}, headers={SETUP_HEADER: monitor_token})
                    expect_status(401, base, "/app/info")
                    expect_status(401, base, "/app/info", headers={PROGRESS_HEADER: monitor_token})
                    expect_status(401, base, "/app/data-path/import", headers={PROGRESS_HEADER: monitor_token})
                    saved = json.loads((target / ".bakabase-import-status.json").read_text())
                    if saved["token"] != monitor_token or saved["progress"]["id"] != operation_id:
                        raise RuntimeError("Import replaced the target's monitoring capability")
                    if (target / "mount-persistence.txt").read_text() != (source / "mount-persistence.txt").read_text():
                        raise RuntimeError("Imported content is missing")
                    if not list((target / "backups/appdata-imports").glob("*/target-before-import.txt")):
                        raise RuntimeError("Previous server data was not backed up")
                    upgrade_backup = target / "backups" / backup_version
                    prior_settings = json.loads((upgrade_backup / "app.json").read_text(encoding="utf-8-sig"))
                    prior_app = next(value for key, value in prior_settings.items() if key.lower() == "app")
                    if prior_app[version_key] != backup_version:
                        raise RuntimeError("The automatic version backup did not preserve the pre-upgrade app settings")
                    if (upgrade_backup / "mount-persistence.txt").read_text() != (source / "mount-persistence.txt").read_text():
                        raise RuntimeError("The automatic version backup omitted application data")
                    if any(path.name.startswith(".bakabase-") or path.name == ".bakabase.lock" for path in upgrade_backup.iterdir()):
                        raise RuntimeError("The automatic version backup copied runtime maintenance control files")
                    if (target / ".bakabase-import.json").exists():
                        raise RuntimeError("Import did not finish")
                    if (target / ".bakabase-setup-draft.json").exists():
                        raise RuntimeError("Successful mapping did not delete its saved draft")
                    with closing(sqlite3.connect((target / "bakabase_insideworld.db").as_uri() + "?mode=ro", uri=True)) as db:
                        mapped = dict(db.execute("SELECT Id,Path FROM SetupPathSmoke"))
                    if mapped != {1: "/media/Movies/a.mkv", 2: "/media/Movies/b.mkv", 3: "Z:/Music/c.flac"}:
                        raise RuntimeError("Installed path values differ from the reviewed mapping")
                    if (target / private_directory.name).stat().st_mode & 0o777 != 0o700:
                        raise RuntimeError("Import widened private directory permissions")
                    if (target / private_directory.name / private_file.name).stat().st_mode & 0o777 != 0o600:
                        raise RuntimeError("Import widened private file permissions")
                    if hashes() != source_hashes:
                        raise RuntimeError("Import modified its read-only source")
                    print("Observed Docker import phases: " + " -> ".join(phases))
                    print(f"PASS: imported older version {backup_version}; automatic backup completed before business startup")
                    print("PASS: read-only preflight, directory tree, reviewed mapping and automatic draft cleanup")
                expect_status(401, base, PROGRESS_PATH + "/status")
                expect_status(401, base, PROGRESS_PATH + "/status", headers={PROGRESS_HEADER: source_token})
                expect_status(405, base, PROGRESS_PATH + "/status", body={}, headers={PROGRESS_HEADER: monitor_token})
                page = request(base, PROGRESS_PATH)
                if b"X-Bakabase-Import-Token" not in page:
                    raise RuntimeError("Import monitoring page was not served")
                docker("stop", "--time", "30", container)
            except Exception:
                print_failure(container)
                raise
            finally:
                docker("rm", "-f", container, check=False)


def verify_worker_failure(image, source, timeout):
    # A sparse fixture keeps the worker copying long enough to terminate only
    # that child. It is removed immediately afterwards; no large import completes.
    payload = source / "worker-failure-payload.bin"
    with payload.open("wb") as stream:
        stream.truncate(16 * 1024 * 1024 * 1024)
    container = "bakabase-worker-failure-" + uuid.uuid4().hex[:12]
    try:
        with tempfile.TemporaryDirectory(prefix="bakabase-worker-failure-") as directory:
            data = Path(directory)
            try:
                docker("run", "-d", "--name", container,
                       "--user", f"{os.getuid()}:{os.getgid()}", "-e", "HOME=/tmp",
                       "--mount", f"type=bind,source={directory},target=/data",
                       "--mount", f"type=bind,source={source},target=/import,readonly",
                       "-p", "127.0.0.1::34567", image)
                port = json.loads(docker("inspect", container))[0]["NetworkSettings"]["Ports"]["34567/tcp"][0]["HostPort"]
                base = f"http://127.0.0.1:{port}"
                deadline = time.monotonic() + timeout
                while time.monotonic() < deadline:
                    try:
                        token = json.loads((data / ".bakabase-server-setup.json").read_text())["token"]
                        request(base, "/setup/status", headers={SETUP_HEADER: token})
                        break
                    except (OSError, ValueError, KeyError):
                        if docker("inspect", "--format", "{{.State.Running}}", container) != "true":
                            raise RuntimeError("Coordinator exited before the worker failure fixture started")
                        time.sleep(.05)
                else:
                    raise TimeoutError("Worker failure setup did not become available")
                result = json.loads(request(base, "/setup/apply", {"operation": "import", "sourcePath": "/import"},
                                            headers={SETUP_HEADER: token}))
                monitor = result["monitorToken"]
                operation = result["progress"]["id"]
                while time.monotonic() < deadline:
                    progress = json.loads(request(base, PROGRESS_PATH + "/status", headers={PROGRESS_HEADER: monitor}))
                    if progress["phase"] == "copying" and progress["completedBytes"] > 0:
                        break
                    if progress["phase"] in ("failed", "completed"):
                        raise RuntimeError(f"Worker left the copy phase before fault injection: {progress['phase']}")
                    time.sleep(.05)
                else:
                    raise TimeoutError("Maintenance worker did not begin copying")
                killed = docker("exec", container, "sh", "-c",
                                'for f in /proc/[0-9]*/cmdline; do '
                                'if grep -azqx -- "--bakabase-role=worker" "$f" 2>/dev/null; then '
                                'p=${f#/proc/}; p=${p%/cmdline}; kill -KILL "$p"; echo "$p"; exit 0; fi; '
                                'done; exit 1')
                if not killed.isdigit() or killed == "1":
                    raise RuntimeError("The fault injection did not select only the worker child")
                while time.monotonic() < deadline:
                    if docker("inspect", "--format", "{{.State.Running}}", container) != "true":
                        raise RuntimeError("The Setup parent exited with its failed worker")
                    progress = json.loads(request(base, PROGRESS_PATH + "/status", headers={PROGRESS_HEADER: monitor}))
                    if progress["phase"] == "failed":
                        break
                    time.sleep(.1)
                else:
                    raise TimeoutError("The Setup parent did not report its failed worker")
                if progress["id"] != operation or not progress.get("error"):
                    raise RuntimeError("The parent lost the operation identity or worker failure")
                expect_status(503, base, "/app/info")
                expect_status(401, base, PROGRESS_PATH + "/status")
                if not (data / ".bakabase-import.json").exists():
                    raise RuntimeError("Worker failure discarded the import recovery journal")
                persisted = json.loads((data / ".bakabase-import-status.json").read_text())
                if persisted["token"] != monitor or persisted["progress"]["phase"] != "failed":
                    raise RuntimeError("Worker failure was not persisted under the same monitoring capability")
                print("PASS: killed worker child; Setup parent remains online with the same token, failed state and recovery journal")
            except Exception:
                print_failure(container)
                raise
            finally:
                docker("rm", "-f", container, check=False)
    finally:
        payload.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    parser.add_argument("--architecture", choices=("amd64", "arm64"), required=True)
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--version", help="Require this exact /app/info coreVersion")
    parser.add_argument("--runtime-version", help="Require this shared .NET runtime to be loaded by the service")
    args = parser.parse_args()
    actual = docker("image", "inspect", "--format", "{{.Architecture}}", args.image)
    if actual != args.architecture:
        raise RuntimeError(f"Expected {args.architecture}, got {actual}")

    with tempfile.TemporaryDirectory(prefix="bakabase-docker-") as directory:
        data = Path(directory)
        marker = data / "mount-persistence.txt"
        expected = uuid.uuid4().hex
        device_id = None
        for iteration in range(2):
            container = "bakabase-acceptance-" + uuid.uuid4().hex[:12]
            try:
                # Run as the invoking user so this script can inspect and remove
                # its own fixture on Linux, as well as on Docker for macOS.
                docker("run", "-d", "--name", container,
                       "--user", f"{os.getuid()}:{os.getgid()}", "-e", "HOME=/tmp",
                       "--mount", f"type=bind,source={directory},target=/data",
                       "--mount", f"type=bind,source={directory},target=/same-data,readonly",
                       "-p", "127.0.0.1::34567", args.image)
                port = json.loads(docker("inspect", container))[0]["NetworkSettings"]["Ports"]["34567/tcp"][0]["HostPort"]
                base = f"http://127.0.0.1:{port}"
                if iteration == 0:
                    initialize_empty(container, base, data, args.timeout)
                    marker.write_text(expected)
                info = wait_for_app(container, base, args.timeout)
                if iteration == 0:
                    verify_native_components(container, base, data, args.architecture)
                if args.version and info["coreVersion"] != args.version:
                    raise RuntimeError(f"Expected version {args.version}, got {info['coreVersion']}")
                if args.runtime_version:
                    maps = docker("exec", container, "cat", "/proc/1/maps")
                    expected_runtime = f"/shared/Microsoft.NETCore.App/{args.runtime_version}/libcoreclr.so"
                    if expected_runtime not in maps:
                        raise RuntimeError(f"The service did not load the expected shared runtime {args.runtime_version}")
                verify_fixed_data_path(base, data)
                # Different container paths can alias one host directory. Such
                # a source must be rejected before a pending import is written.
                alias_payload = {"sourcePath": "/same-data"}
                alias_validation = json.loads(request(base, "/app/data-path/import/validate", alias_payload))
                if alias_validation["data"]["valid"]:
                    raise RuntimeError("Import accepted the live data directory through a bind alias")
                alias_queue = json.loads(request(base, "/app/data-path/import", alias_payload))
                if alias_queue["code"] == 0 or (data / ".bakabase-import.json").exists():
                    raise RuntimeError("Import queued the live data directory through a bind alias")
                verify_frontend(base)
                current_device_id = json.loads(request(base, "/app/analytics-info"))["data"]["deviceId"]
                if not current_device_id:
                    raise RuntimeError("The container did not create a persistent device identity")
                if iteration == 0:
                    device_id = current_device_id
                elif current_device_id != device_id:
                    raise RuntimeError("Device identity was lost when the container was replaced")
                if marker.read_text() != expected:
                    raise RuntimeError("Bind-mounted files changed across container replacement")
                docker("stop", "--time", "30", container)
                databases = list(data.rglob("*.db"))
                if not databases:
                    raise RuntimeError("No SQLite database was created in the host appdata")
                for database in databases:
                    # These are disposable fixtures with the container stopped.
                    # Allow SQLite to recreate WAL shared memory across OSes.
                    with closing(sqlite3.connect(database)) as connection:
                        if connection.execute("PRAGMA quick_check").fetchone() != ("ok",):
                            raise RuntimeError(f"SQLite integrity check failed: {database.name}")
            except Exception:
                print_failure(container)
                raise
            finally:
                docker("rm", "-f", container, check=False)
        verify_import(args.image, data, args.timeout)
        verify_worker_failure(args.image, data, args.timeout)
        print(f"PASS: {args.architecture}; first-run setup, fixed data path, HTTP/UI, SQLite, persistent bind mount, read-only import and monitoring across pairing changes")


if __name__ == "__main__":
    main()
