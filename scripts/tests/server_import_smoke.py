#!/usr/bin/env python3
"""Legacy manual-restart integration; temporary data only.

For the independent Setup coordinator, run server_setup_process_smoke.py instead.
The common HTTP/process helpers remain shared with that current integration test.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import secrets
import socket
import sqlite3
import subprocess
import tempfile
import time
import uuid
from urllib.error import HTTPError, URLError
from urllib.request import build_opener, ProxyHandler, Request


HTTP = build_opener(ProxyHandler({}))


def check(condition, message):
    if not condition:
        raise AssertionError(message)


def free_port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


PROGRESS_PATH = "/app/data-path/import/progress/status"
TOKEN_HEADER = "X-Bakabase-Import-Token"
SETUP_HEADER = "X-Bakabase-Setup-Token"


def request(port, path, method="GET", body=None, headers=None):
    content = None if body is None else json.dumps(body).encode()
    call = Request(f"http://127.0.0.1:{port}{path}", data=content, method=method,
                   headers={"Content-Type": "application/json", **(headers or {})})
    try:
        response = HTTP.open(call, timeout=3)
    except HTTPError as error:
        response = error
    with response:
        return response.status, json.load(response)


def ok(port, path, method="GET", body=None):
    status, response = request(port, path, method, body)
    check(status == 200 and not response.get("code"), f"{method} {path}: {status} {response}")
    return response.get("data")


def queue_import(port, body):
    setup_token = ok(port, "/app/data-path/import/setup-session", "POST")["setupToken"]
    headers = {SETUP_HEADER: setup_token}
    code, status = request(port, "/setup/status", headers=headers)
    check(code == 200 and status["mode"] == "import" and not status["canChooseTargetPath"],
          f"Existing-service setup had the wrong scope: {status}")
    check(status["allowedOperations"] == ["import"],
          f"Fixed existing-service setup exposed an unsupported operation: {status}")
    for forbidden in ("initialize", "relocate"):
        code, rejected = request(port, "/setup/apply", "POST", {"operation": forbidden}, headers)
        check(code == 400, f"Existing-service setup accepted {forbidden}: {code} {rejected}")
    body = dict(body, operation="import")
    code, validation = request(port, "/setup/validate", "POST", body, headers)
    check(code == 200 and validation["valid"], f"Source validation failed: {validation}")
    code, result = request(port, "/setup/apply", "POST", body, headers)
    check(code == 200 and result["requiresRestart"], f"Existing-service import was not queued: {result}")
    check(request(port, "/setup/status", headers=headers)[0] == 401,
          "Submitted setup capability remained reusable")
    return result


def digest_tree(root):
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_file():
            digest = hashlib.sha256()
            with path.open("rb") as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(block)
            result[str(path.relative_to(root))] = digest.hexdigest()
    return result


class Server:
    def __init__(self, release, data, log, port=None, initial_source=None, expected_data=None):
        self.release = release
        self.data = data
        self.log = log
        self.port = port or free_port()
        self.process = None
        self.output = None
        self.initial_source = initial_source
        self.expected_data = expected_data or data

    def start(self):
        env = dict(os.environ, BAKABASE_DATA_DIR=str(self.data),
                   BAKABASE_BIND_ADDRESS="127.0.0.1", API_LISTENING_PORTS=str(self.port))
        self.output = self.log.open("wb")
        self.process = subprocess.Popen([str(self.release / "bakabase-server")], cwd=self.release,
                                        env=env, stdout=self.output, stderr=subprocess.STDOUT,
                                        start_new_session=True)
        return self

    def ready(self, monitor_token=None, observed_phases=None):
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            check(self.process.poll() is None, f"Server exited {self.process.returncode}: {self.log}")
            try:
                if monitor_token:
                    code, snapshot = request(self.port, PROGRESS_PATH, headers={TOKEN_HEADER: monitor_token})
                    check(code == 200, f"Monitoring token stopped working during startup: {code} {snapshot}")
                    phase = snapshot["phase"]
                    if observed_phases is not None and (not observed_phases or observed_phases[-1] != phase):
                        observed_phases.append(phase)
                    check(phase != "failed", f"Import failed: {snapshot}")
                    check(phase != "completed" or "Server ready:" in self.log.read_text(errors="replace"),
                          "Monitoring declared completion before application initialization")
                status, response = request(self.port, "/app/info")
                if status == 503:
                    bootstrap_path = self.data / ".bakabase-server-setup.json"
                    if not monitor_token and bootstrap_path.is_file():
                        bootstrap = json.loads(bootstrap_path.read_text())
                        if not bootstrap["submitted"]:
                            check(not (self.data / "bakabase_insideworld.db").exists(),
                                  "First-run setup opened the application database before confirmation")
                            check(request(self.port, "/setup/status")[0] == 401,
                                  "First-run setup status was exposed without a setup capability")
                            setup_headers = {SETUP_HEADER: bootstrap["token"]}
                            code, setup = request(self.port, "/setup/status", headers=setup_headers)
                            check(code == 200 and setup["mode"] == "first-run" and not setup["canChooseTargetPath"],
                                  f"Explicit native data directory was not kept fixed: {code} {setup}")
                            check(set(setup["allowedOperations"]) == {"initialize", "import"},
                                  f"First-run setup exposed the wrong operations: {setup}")
                            initial_request = ({"operation": "import", "sourcePath": str(self.initial_source)}
                                               if self.initial_source else {"operation": "initialize"})
                            code, validation = request(self.port, "/setup/validate", "POST", initial_request, setup_headers)
                            check(code == 200 and validation["valid"], f"Empty initial library was refused: {validation}")
                            code, committed = request(self.port, "/setup/apply", "POST", initial_request, setup_headers)
                            check(code == 200 and not committed["requiresRestart"], f"First-run setup failed: {committed}")
                            monitor_token = committed["monitorToken"]
                    time.sleep(0.02)
                    continue
                check(status == 200 and not response.get("code"), f"Unexpected app/info: {status} {response}")
                info = response["data"]
                check(Path(info["appDataPath"]).resolve() == self.expected_data.resolve(), "Unexpected AppData path")
                # Startup persists the release version asynchronously after binding the port.
                options = json.loads((self.expected_data / "app.json").read_text(encoding="utf-8-sig"))
                options = options.get("App", options)
                if (options.get("version", options.get("Version", "")) == info["coreVersion"]
                        and "Server ready:" in self.log.read_text(errors="replace")
                        and (not monitor_token or phase == "completed")):
                    return self
            except (URLError, TimeoutError, OSError, json.JSONDecodeError):
                pass
            time.sleep(0.02 if monitor_token else 0.2)
        raise AssertionError(f"Server did not become ready: {self.log}")

    def stop(self, require_graceful=True):
        forced = False
        if self.process and self.process.poll() is None:
            try:
                os.killpg(self.process.pid, signal.SIGINT)
            except ProcessLookupError:
                pass
            try:
                self.process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                forced = True
                os.killpg(self.process.pid, signal.SIGKILL)
                self.process.wait(timeout=5)
        if self.output:
            self.output.close()
            self.output = None
        if require_graceful:
            check(not forced, f"Server did not stop gracefully: {self.log}")


def exercise(release, root):
    servers = []

    def launch(name, data, port=None, monitor_token=None, observed_phases=None, initial_source=None):
        server = Server(release, data, root / f"{name}.log", port=port, initial_source=initial_source)
        servers.append(server)
        return server.start().ready(monitor_token, observed_phases)

    source = root / "source appdata"
    target = root / "target appdata"
    try:
        first = launch("source", source)
        ok(first.port, "/app/terms", "POST")
        source_id = ok(first.port, "/app/analytics-info")["deviceId"]
        first.stop()
        (source / "source-sentinel.txt").write_text("source library must survive unchanged\n")
        tool = source / "components/smoke-tool.sh"
        tool.parent.mkdir(exist_ok=True)
        tool.write_text("#!/bin/sh\necho imported-tool\n")
        tool.chmod(0o750)
        private = source / "smoke-private"
        private.mkdir(mode=0o700)
        secret = private / "key.txt"
        secret.write_text("private key fixture\n")
        secret.chmod(0o600)
        source.chmod(0o700)
        before = digest_tree(source)

        second = launch("target-before", target)
        (target / "target-sentinel.txt").write_text("original server data must be backed up\n")
        target_id = ok(second.port, "/app/analytics-info")["deviceId"]
        check(target_id != source_id, "Fresh directories unexpectedly share device IDs")
        status = ok(second.port, "/app/data-path/import")
        check(status["supported"] and not status.get("sourcePath"), "Native import is unavailable or already queued")
        body = {"sourcePath": str(source), "originalDataPath": "/old-machine/AppData"}
        queued = queue_import(second.port, body)
        monitor_token = queued["monitorToken"]
        check(len(monitor_token) == 64 and queued["progress"]["phase"] == "queued",
              f"Queue response omitted monitoring capability or queued phase: {queued}")
        code, _ = request(second.port, PROGRESS_PATH)
        check(code == 401, "Import progress was exposed without the operation token")
        code, snapshot = request(second.port, PROGRESS_PATH, headers={TOKEN_HEADER: monitor_token})
        check(code == 200 and snapshot["phase"] == "queued", "Queued status cannot be monitored")
        marker = target / ".bakabase-import.json"
        journal = json.loads(marker.read_text())
        import_id = journal.get("id", journal.get("Id"))
        check(bool(import_id), "Import journal has no operation ID")
        check(ok(second.port, "/app/data-path/import")["sourcePath"], "Queued import not reported")
        check(before == digest_tree(source), "Validating/queuing import modified the source")

        # The API must leave process supervision to the caller, never spawn a replacement.
        _, restart = request(second.port, "/app/restart", "POST")
        check(bool(restart.get("code")), f"Standalone restart was not rejected: {restart}")
        check("process manager" in restart.get("message", ""), f"Unexpected restart rejection: {restart}")
        ok(second.port, "/app/info")
        second.stop()

        observed_phases = []
        third = launch("target-after", target, second.port, monitor_token, observed_phases)
        check("starting" in observed_phases, f"Startup was never exposed through monitoring: {observed_phases}")
        check(observed_phases[-1] == "completed", f"Ready server omitted completion: {observed_phases}")
        check(ok(third.port, "/app/data-path/import")["monitorToken"] == monitor_token,
              "Import replaced its operation token with the source's monitoring state")
        check(ok(third.port, "/app/analytics-info")["deviceId"] == source_id, "Imported device identity was not restored")
        check((target / "source-sentinel.txt").read_bytes() == (source / "source-sentinel.txt").read_bytes(),
              "Source sentinel was not imported")
        check((target / "components/smoke-tool.sh").stat().st_mode & 0o111 == tool.stat().st_mode & 0o111,
              "Imported component lost executable permission")
        check(target.stat().st_mode & 0o777 == 0o700 and (target / "smoke-private").stat().st_mode & 0o777 == 0o700
              and (target / "smoke-private/key.txt").stat().st_mode & 0o777 == 0o600,
              "Import broadened private AppData permissions")
        check(not marker.exists(), "Completed import left its journal pending")
        check(not (target / "target-sentinel.txt").exists(), "Import merged old target files into the new library")
        backup = target / "backups/appdata-imports" / import_id
        check(backup.stat().st_mode & 0o777 == 0o700, "Import backup root is not private")
        check((backup / "target-sentinel.txt").read_text() == "original server data must be backed up\n",
              "The original target was not backed up")
        check((backup / "bakabase_insideworld.db").is_file(), "Original target database backup is missing")
        roots = json.loads((target / "appdata-import-roots.json").read_text())
        check(str(source.resolve()) in roots and "/old-machine/AppData" in roots,
              f"Original AppData roots were not retained: {roots}")
        check(before == digest_tree(source), "Import modified source bytes")

        duplicate = Server(release, target, root / "duplicate.log")
        servers.append(duplicate)
        duplicate.start()
        check(duplicate.process.wait(timeout=10) == 1, "A duplicate instance did not refuse the occupied AppData")
        check("Cannot lock appdata" in duplicate.log.read_text(), "Duplicate failed for a reason other than data ownership")
        check(third.process.poll() is None, "Duplicate startup terminated the owner")
        ok(third.port, "/app/info")
        third.stop()

        for index, directory in enumerate((source, target, backup)):
            # Even read-only SQLite can create WAL sidecars. Inspect a stopped copy
            # so the verification itself cannot invalidate the source byte check.
            inspection = root / "sqlite-checks" / str(index)
            inspection.mkdir(parents=True)
            for suffix in ("", "-wal", "-shm"):
                path = directory / f"bakabase_insideworld.db{suffix}"
                if path.exists():
                    shutil.copyfile(path, inspection / path.name)
            with sqlite3.connect(inspection / "bakabase_insideworld.db") as db:
                check(db.execute("PRAGMA integrity_check").fetchone()[0] == "ok", f"SQLite corruption in {directory}")
        check(before == digest_tree(source), "Read-only verification modified source bytes")

        initial_import_phases = []
        initial = launch("first-import", root / "first import appdata", initial_source=source,
                         observed_phases=initial_import_phases)
        check(ok(initial.port, "/app/analytics-info")["deviceId"] == source_id,
              "First-run import did not restore the original identity in the same process")
        check((initial.data / "source-sentinel.txt").is_file(), "First-run source data was not installed")
        check(initial_import_phases[-1] == "completed", "First-run import never reached ready")
        check(before == digest_tree(source), "First-run import changed its source")
        initial.stop()

        # A temporary unreadable source file forces an actual copy failure. The
        # monitor must remain available while the application and SQLite stay closed.
        failure_target = root / "failure appdata"
        failure_before = launch("failure-before", failure_target)
        queued_failure = queue_import(failure_before.port, body)
        failed_token = queued_failure["monitorToken"]
        failure_before.stop()
        failed = Server(release, failure_target, root / "failure-after.log", port=failure_before.port)
        servers.append(failed)
        original_mode = tool.stat().st_mode & 0o777
        tool.chmod(0)
        try:
            try:
                with tool.open("rb"):
                    pass
            except PermissionError:
                pass
            else:
                raise AssertionError("Copy-failure fixture needs an unprivileged Unix user")
            failed.start()
            deadline = time.monotonic() + 30
            failure_phases = []
            while time.monotonic() < deadline:
                check(failed.process.poll() is None, f"Failed import lost its monitoring server: {failed.log}")
                try:
                    code, snapshot = request(failed.port, PROGRESS_PATH, headers={TOKEN_HEADER: failed_token})
                    check(code == 200, f"Failed import monitoring is unavailable: {code} {snapshot}")
                    phase = snapshot["phase"]
                    if not failure_phases or failure_phases[-1] != phase:
                        failure_phases.append(phase)
                    if phase == "failed":
                        check(bool(snapshot.get("error")), "Failed import did not report a reason")
                        break
                except (URLError, TimeoutError, OSError):
                    pass
                time.sleep(0.02)
            else:
                raise AssertionError(f"Unreadable source did not fail import: {failure_phases}")
            check(request(failed.port, "/app/info")[0] == 503,
                  "Failed copy started the ordinary application")
            check(request(failed.port, PROGRESS_PATH)[0] == 401,
                  "Failed copy exposed progress without its capability")
            check("Server ready:" not in failed.log.read_text(errors="replace"),
                  "Failed copy initialized the ordinary application")
            check((failure_target / ".bakabase-import.json").is_file(),
                  "Failed copy discarded its retry journal")
            check(not (failure_target / "backups/appdata-imports" / queued_failure["progress"]["id"]).exists(),
                  "Failed copying moved original target data into backup prematurely")
        finally:
            failed.stop(require_graceful=False)
            tool.chmod(original_mode)
        check(before == digest_tree(source), "Failure handling modified source bytes")

        relocation_source = root / "relocation anchor"
        relocation_target = root / "relocated library"
        relocate_before = launch("relocate-before", relocation_source)
        relocation_identity = ok(relocate_before.port, "/app/analytics-info")["deviceId"]
        relocate_status = ok(relocate_before.port, "/app/data-path/relocation")
        check(not relocate_status["supported"], "Fixed-path deployment unexpectedly permitted UI relocation")
        _, rejection = request(relocate_before.port, "/app/data-path/relocation/setup-session", "POST")
        check(bool(rejection.get("code")), "Fixed-path relocation session was not rejected")
        _, rejection = request(relocate_before.port, "/app/data-path/relocation", "DELETE")
        check(bool(rejection.get("code")), "Fixed-path relocation mutation was not rejected")
        relocate_before.stop()
        relocation_source.chmod(0o700)
        kept_backup = relocation_source / "backups/kept/sentinel.txt"
        kept_backup.parent.mkdir(parents=True, exist_ok=True)
        kept_backup.write_text("preserve every historical backup\n")
        kept_log = relocation_source / "logs/relocation-sentinel.log"
        kept_log.parent.mkdir(exist_ok=True)
        kept_log.write_text("preserve existing logs\n")

        # This test fixes AppData to temporary directories, so the UI must refuse a
        # relocation request. Seed the equivalent approved plan while stopped to test
        # the actual native restart/executor without altering the user's default anchor.
        relocation_id = uuid.uuid4().hex
        relocation_token = secrets.token_hex(32).upper()
        relocation_marker = relocation_source / ".bakabase-relocate.json"
        relocation_marker.write_text(json.dumps({"schemaVersion": 1, "id": relocation_id,
            "sourcePath": str(relocation_source), "targetPath": str(relocation_target),
            "phase": "queued", "incomingEntries": []}))
        metadata = relocation_source / ".bakabase-import-status.json"
        saved = json.loads(metadata.read_text())
        saved.update(token=relocation_token, applied=False)
        saved["progress"].update(id=relocation_id, operation="relocate", phase="queued",
            sourcePath=str(relocation_source), targetPath=str(relocation_target), backupPath=None,
            error=None, startedAtUtc=None, elapsedSeconds=0, completedBytes=0, totalBytes=0,
            completedFiles=0, totalFiles=0, completedEntries=0, totalEntries=0)
        metadata.write_text(json.dumps(saved))
        metadata.chmod(0o600)
        source_library_before = {name: digest for name, digest in digest_tree(relocation_source).items()
                                 if not name.startswith(".")}
        relocation_phases = []
        relocated = Server(release, relocation_source, root / "relocate-after.log", port=relocate_before.port,
                           expected_data=relocation_target)
        servers.append(relocated)
        relocated.start().ready(relocation_token, relocation_phases)
        check(ok(relocated.port, "/app/analytics-info")["deviceId"] == relocation_identity,
              "Relocation changed the library identity")
        check(relocation_target.stat().st_mode & 0o777 == 0o700,
              "Relocation broadened the private source root permissions")
        check(not relocation_marker.exists(), "Relocation left its journal pending")
        check((relocation_source / ".redirect").read_text().strip() == str(relocation_target),
              "Relocation did not persist the anchor redirect")
        check((relocation_target / "backups/kept/sentinel.txt").read_bytes() == kept_backup.read_bytes(),
              "Relocation omitted historical backups")
        check((relocation_target / "logs/relocation-sentinel.log").read_bytes() == kept_log.read_bytes(),
              "Relocation omitted historical logs")
        check(source_library_before == {name: digest for name, digest in digest_tree(relocation_source).items()
                                        if not name.startswith(".")}, "Relocation modified or deleted original library bytes")
        check(ok(relocated.port, "/app/data-path/relocation")["monitorToken"] == relocation_token,
              "Relocation lost its monitoring capability after switching directories")
        relocated.stop()
        resumed = Server(release, relocation_source, root / "relocate-resumed.log", expected_data=relocation_target)
        servers.append(resumed)
        resumed.start().ready()
        check(ok(resumed.port, "/app/analytics-info")["deviceId"] == relocation_identity,
              "Next launch did not reuse the relocated library")
        resumed.stop()

        combined_current = root / "combined original library"
        combined_target = root / "combined imported library"
        combined_before = launch("combined-before", combined_current)
        combined_old_identity = ok(combined_before.port, "/app/analytics-info")["deviceId"]
        check(combined_old_identity != source_id, "Combined-import fixtures unexpectedly share identities")
        combined_before.stop()
        (combined_current / "current-only.txt").write_text("retain the previous library without merging it\n")
        current_backup = combined_current / "backups/kept/current-backup.txt"
        current_backup.parent.mkdir(parents=True, exist_ok=True)
        current_backup.write_text("retain old historical backups in the original directory\n")
        combined_original_bytes = {
            name: digest for name, digest in digest_tree(combined_current).items()
            if not name.startswith(".")
        }

        # Like the relocation case above, seed an already-approved plan while the
        # fixed-path fixture is stopped. The source is a separate, real library;
        # neither the old library nor the source may be merged into each other.
        combined_id = uuid.uuid4().hex
        combined_token = secrets.token_hex(32).upper()
        combined_original_root = "/combined-original-machine/data"
        combined_marker = combined_current / ".bakabase-relocate.json"
        combined_marker.write_text(json.dumps({
            "schemaVersion": 2, "id": combined_id, "phase": "queued", "incomingEntries": [],
            "sourcePath": str(combined_current), "importSourcePath": str(source),
            "targetPath": str(combined_target), "originalDataPath": combined_original_root,
        }))
        combined_marker.chmod(0o600)
        metadata = combined_current / ".bakabase-import-status.json"
        saved = json.loads(metadata.read_text())
        saved.update(token=combined_token, applied=False)
        saved["progress"].update(
            id=combined_id, operation="import", phase="queued", sourcePath=str(source),
            targetPath=str(combined_target), backupPath=str(combined_current), error=None,
            currentFile=None, startedAtUtc=None, elapsedSeconds=0, bytesPerSecond=0,
            remainingSeconds=None, completedBytes=0, totalBytes=0, completedFiles=0,
            totalFiles=0, completedEntries=0, totalEntries=0,
        )
        metadata.write_text(json.dumps(saved))
        metadata.chmod(0o600)

        def verify_combined_monitor(server):
            status = ok(server.port, "/app/data-path/import")
            check(status["monitorToken"] == combined_token,
                  "Combined import changed its monitor token after the directory switch")
            code, snapshot = request(server.port, PROGRESS_PATH, headers={TOKEN_HEADER: combined_token})
            check(code == 200 and snapshot["id"] == combined_id and snapshot["phase"] == "completed"
                  and snapshot["operation"] == "import", f"Combined import lost completed progress: {snapshot}")
            for field, expected in (("sourcePath", source), ("targetPath", combined_target),
                                    ("backupPath", combined_current)):
                check(Path(snapshot[field]).resolve() == expected.resolve(),
                      f"Combined import reported the wrong {field}: {snapshot}")
            check(request(server.port, PROGRESS_PATH)[0] == 401,
                  "Combined import exposed progress without its capability")

        combined_phases = []
        combined = Server(release, combined_current, root / "combined-after.log",
                          port=combined_before.port, expected_data=combined_target)
        servers.append(combined)
        combined.start().ready(combined_token, combined_phases)
        check("starting" in combined_phases and combined_phases[-1] == "completed",
              f"Combined import did not expose startup and completion: {combined_phases}")
        verify_combined_monitor(combined)
        check(ok(combined.port, "/app/analytics-info")["deviceId"] == source_id,
              "Combined import did not restore the external source identity")
        check((combined_target / "source-sentinel.txt").read_bytes() == (source / "source-sentinel.txt").read_bytes(),
              "Combined import did not install external source content into the new directory")
        combined_roots = json.loads((combined_target / "appdata-import-roots.json").read_text())
        check(str(source.resolve()) in combined_roots and combined_original_root in combined_roots,
              f"Combined import omitted its source or original-device path: {combined_roots}")
        check(not (combined_target / "current-only.txt").exists()
              and not (combined_target / "backups/kept/current-backup.txt").exists(),
              "Combined import merged current-library content into the imported library")
        check(not combined_marker.exists() and not (combined_target / ".bakabase-import.json").exists(),
              "Combined import left an outer or inner journal pending")
        check((combined_current / ".redirect").read_text().strip() == str(combined_target),
              "Combined import did not persist the new target in the original anchor")
        combined.stop()

        combined_resumed = Server(release, combined_current, root / "combined-resumed.log",
                                  expected_data=combined_target)
        servers.append(combined_resumed)
        combined_resumed.start().ready()
        verify_combined_monitor(combined_resumed)
        check(ok(combined_resumed.port, "/app/analytics-info")["deviceId"] == source_id,
              "Next launch did not reuse the combined import's new target")
        combined_resumed.stop()
        check(combined_original_bytes == {
            name: digest for name, digest in digest_tree(combined_current).items()
            if not name.startswith(".")
        }, "Combined import modified or deleted ordinary content in the original library")
        check(before == digest_tree(source), "Combined import or its next launch modified the external source")

        return {"passed": True, "release": str(release), "sourceFilesUnchanged": len(before),
                "importId": import_id, "observedProgressPhases": observed_phases,
                "failedProgressPhases": failure_phases,
                "initialImportPhases": initial_import_phases,
                "relocationPhases": relocation_phases,
                "combinedImportPhases": combined_phases,
                "checks": ["first-run capability required before database creation", "fixed-path first-run initialization",
                "native startup", "terms API", "shared setup validation and queue", "setup capability is single-use",
                "first-run offers initialization and import", "existing fixed instance rejects initialization and relocation",
                "first-run import completes without a process restart",
                "restart rejection", "device identity imported", "source SHA256 unchanged",
                "target backup", "journal completed", "historical roots", "component execution and private AppData permissions",
                "duplicate refused", "SQLite integrity", "queued monitoring capability",
                "same-port monitoring after restart", "starting then completed after ready",
                "progress requires operation token", "copy failure keeps read-only monitor",
                "copy failure keeps ordinary API unavailable", "fixed deployment rejects relocation APIs",
                "native relocation switches anchor and preserves identity", "relocation retains original library bytes",
                "relocation includes backups and logs", "relocation preserves monitoring capability",
                "subsequent launch follows relocated directory",
                "combined import installs external data into a new target", "combined import retains current and source bytes",
                "combined import preserves token and reports original directory as retained backup",
                "subsequent launch follows combined import target"]}
    finally:
        for server in reversed(servers):
            server.stop(require_graceful=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, default=Path.home() / ".local/share/bakabase-server/current")
    parser.add_argument("--keep-data", action="store_true", help="Keep temporary logs and fixtures after success")
    args = parser.parse_args()
    release = args.release.expanduser().resolve()
    check((release / "bakabase-server").is_file(), f"Native published launcher missing: {release}")
    root = Path(tempfile.mkdtemp(prefix="bakabase-native-import-smoke-")).resolve()
    try:
        report = exercise(release, root)
        print(json.dumps(report, indent=2))
        if args.keep_data:
            print(f"Temporary evidence: {root}")
        else:
            shutil.rmtree(root)
    except BaseException:
        print(f"Temporary evidence retained after failure: {root}")
        for log in root.glob("*.log"):
            print(f"--- {log.name} ---\n" + "\n".join(log.read_text(errors="replace").splitlines()[-15:]))
        raise


if __name__ == "__main__":
    main()
