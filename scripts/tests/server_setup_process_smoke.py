#!/usr/bin/env python3
"""Manual Unix integration for the independent Setup coordinator; temporary data only.

Unlike server_import_smoke.py's legacy manual-restart contract, this exercises an
online automatic transition, actual worker death, and parent/child shutdown.
"""
import argparse
import json
import os
from pathlib import Path
import shlex
import shutil
import signal
import subprocess
import tempfile
import time

import server_import_smoke as common


def children(pid):
    rows = subprocess.check_output(["ps", "-axo", "pid=,ppid=,command="], text=True)
    return {int(parts[0]): parts[2] for row in rows.splitlines()
            if len(parts := row.strip().split(None, 2)) == 3 and int(parts[1]) == pid}


def alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False


class Server(common.Server):
    def stop(self, require_graceful=True):
        forced = False
        if self.process and self.process.poll() is None:
            self.process.send_signal(signal.SIGTERM)
            try:
                self.process.wait(timeout=35)
            except subprocess.TimeoutExpired:
                forced = True
                os.killpg(self.process.pid, signal.SIGKILL)
                self.process.wait(timeout=5)
        if self.output:
            self.output.close()
            self.output = None
        if require_graceful:
            common.check(not forced, f"Coordinator did not stop its children: {self.log}")


def exercise(release, root):
    servers = []
    def start(name):
        server = Server(release, root / name, root / f"{name}.log")
        servers.append(server)
        return server.start().ready()

    def submit(server, source):
        session = common.ok(server.port, "/app/data-path/import/setup-session", "POST")
        headers = {common.SETUP_HEADER: session["setupToken"]}
        code, status = common.request(server.port, "/setup/status", headers=headers)
        common.check(code == 200 and status["automaticMaintenance"], f"Setup does not own automatic maintenance: {status}")
        body = {"operation": "import", "sourcePath": str(source)}
        code, validation = common.request(server.port, "/setup/validate", "POST", body, headers)
        common.check(code == 200 and validation["valid"], f"Invalid fixture: {validation}")
        code, result = common.request(server.port, "/setup/apply", "POST", body, headers)
        common.check(code == 200 and not result["requiresRestart"], f"Automatic commit response was lost or rejected: {result}")
        return result["monitorToken"]

    try:
        source = start("source")
        identity = common.ok(source.port, "/app/analytics-info")["deviceId"]
        source.stop()
        (source.data / "sentinel.txt").write_text("External source remains unchanged.")
        before = common.digest_tree(source.data)
        target = start("target")
        parent = target.process.pid
        first_business = [pid for pid, command in children(parent).items() if "--bakabase-role=business" in command]
        common.check(len(first_business) == 1, "Business is not an independent child")
        common.check(common.ok(target.port, "/app/data-path/import")["automaticMaintenance"], "Status omitted managed mode")
        for method in ("POST", "DELETE"):
            _, rejected = common.request(target.port, "/app/data-path/import", method,
                                         {"sourcePath": str(source.data)} if method == "POST" else None)
            common.check(bool(rejected.get("code")), "Legacy mutation bypassed the coordinator")
        token = submit(target, source.data)
        phases = []
        target.ready(token, phases)
        common.check(target.process.pid == parent and target.process.poll() is None, "Automatic import replaced the coordinator")
        common.check(not alive(first_business[0]), "The previous business child survived replacement")
        common.check(common.ok(target.port, "/app/analytics-info")["deviceId"] == identity, "Imported identity is missing")
        common.check((target.data / "sentinel.txt").read_bytes() == (source.data / "sentinel.txt").read_bytes(), "Source data was not installed")
        common.check(common.digest_tree(source.data) == before, "Import changed source bytes")

        # A sparse large file gives enough time to kill the actual copying worker.
        with (source.data / "large.bin").open("wb") as stream:
            stream.truncate(8 * 1024 ** 3)
        token = submit(target, source.data)
        deadline = time.monotonic() + 40
        victim = None
        while time.monotonic() < deadline:
            try:
                code, progress = common.request(target.port, common.PROGRESS_PATH, headers={common.TOKEN_HEADER: token})
                if code == 200 and progress["phase"] == "copying":
                    for pid, command in children(parent).items():
                        if "--bakabase-role=worker" in command:
                            victim = pid
                            break
                if victim:
                    break
            except (OSError, common.URLError):
                pass
            time.sleep(0.01)
        common.check(victim is not None, "Did not observe a separate worker while copying")
        os.kill(victim, signal.SIGKILL)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            code, progress = common.request(target.port, common.PROGRESS_PATH, headers={common.TOKEN_HEADER: token})
            if code == 200 and progress["phase"] == "failed":
                break
            time.sleep(0.05)
        common.check(progress["phase"] == "failed", "Worker death was not reported")
        common.check(target.process.poll() is None, "Worker death killed the coordinator")
        common.check(common.request(target.port, "/app/info")[0] == 503, "Business opened partial data after a worker crash")
        time.sleep(0.5)
        common.check(common.request(target.port, common.PROGRESS_PATH, headers={common.TOKEN_HEADER: token})[1]["phase"] == "failed", "Disk refresh hid failure")
        target.stop()
        common.check(not children(parent), "Coordinator left children behind on SIGTERM")

        # A vanished coordinator must also release its business child and database locks.
        recovery = Server(release, source.data, root / "parent-loss.log")
        servers.append(recovery)
        recovery.start().ready()
        orphan = next(pid for pid, command in children(recovery.process.pid).items() if "--bakabase-role=business" in command)
        recovery.process.kill()
        recovery.process.wait(timeout=5)
        deadline = time.monotonic() + 20
        while alive(orphan) and time.monotonic() < deadline:
            time.sleep(0.1)
        common.check(not alive(orphan), "Business survived its owning coordinator")
        return {"passed": True, "release": str(release), "automaticPhases": phases,
                "killedWorker": victim, "workerFailurePhase": progress.get("failedPhase"),
                "checks": ["first-run before business DB", "independent business PID", "parent-owned setup session",
                           "automatic response acknowledged before shutdown", "same-port automatic import",
                           "identity imported and source unchanged", "legacy mutation rejected", "actual copying worker killed",
                           "same-token failure page survives", "business unavailable after failure",
                           "SIGTERM reaps children", "parent death shuts down business"]}
    finally:
        for server in reversed(servers):
            server.stop(require_graceful=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, default=Path.home() / ".local/share/bakabase-server/current")
    parser.add_argument("--service-dll", type=Path, help="Test a framework-dependent build with dotnet on PATH")
    parser.add_argument("--keep-data", action="store_true")
    args = parser.parse_args()
    root = Path(tempfile.mkdtemp(prefix="bakabase-setup-process-smoke-")).resolve()
    release = args.release.expanduser().resolve()
    try:
        if args.service_dll:
            dotnet = shutil.which("dotnet")
            common.check(dotnet, "dotnet is required for --service-dll")
            release = root / "launcher"
            release.mkdir()
            wrapper = release / "bakabase-server"
            wrapper.write_text("#!/bin/sh\nexec " + shlex.quote(dotnet) + " " + shlex.quote(str(args.service_dll.resolve())) + ' "$@"\n')
            wrapper.chmod(0o700)
        report = exercise(release, root)
        print(json.dumps(report, indent=2))
        if args.keep_data:
            print(f"Temporary evidence: {root}")
        else:
            shutil.rmtree(root)
    except BaseException:
        print(f"Temporary evidence retained after failure: {root}")
        raise


if __name__ == "__main__":
    main()
