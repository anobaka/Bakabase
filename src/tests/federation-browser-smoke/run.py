#!/usr/bin/env python3
"""Run isolated legacy-client -> unified migration and federated browser flows.

Requires the built web app and this directory's pinned Playwright/Chromium installation.
All three listeners, databases and credentials are temporary; no installed application is used.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def stop_fixture_process(process):
    """Stop only our dedicated process group, including Chromium children on timeout."""
    if os.name == "posix":
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    elif process.poll() is None:
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    try:
        process.wait(timeout=8)
    except subprocess.TimeoutExpired:
        if os.name == "posix":
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        else:
            process.kill()
        process.wait(timeout=8)


def retain_host_diagnostics(fixture, hosts, host_processes, results):
    """Retain useful failure evidence without arbitrary log text, pairing codes or keys.

    Host logging may contain ready payloads and complete HTTP bodies. An allowlist of
    exception types, stack method names and known failure categories is safer than
    trying to redact every possible credential format from those messages.
    """
    categories = ("address already in use", "no space left on device", "permission denied",
                  "file not found", "could not load", "failed to load", "connection refused")
    for role, process in host_processes.items():
        path = fixture / (role + ".log")
        # Bound memory even when a failing host produced a large log.
        with path.open("rb") as stream:
            stream.seek(max(0, path.stat().st_size - 128 * 1024))
            tail = stream.read().decode("utf-8", errors="replace")
        exceptions = list(dict.fromkeys(re.findall(
            r"\b((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*Exception)(?=[:\s]|$)", tail)))
        methods = list(dict.fromkeys(re.findall(
            r"^\s+at ((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*)(?=[(<])", tail, re.MULTILINE)))
        diagnostic = {
            "role": role, "exitCode": process.returncode,
            "ready": (Path(hosts[role]["directory"]) / "ready").exists(),
            "logBytes": path.stat().st_size,
            "failureCategories": [label for label in categories if label in tail.lower()],
            "exceptionTypes": exceptions[-30:], "stackMethods": methods[-50:],
            "notice": "Sanitized diagnostic summary; raw messages, ready payloads, paths and arguments omitted. Exit code may reflect runner cleanup.",
        }
        (results / (role + "-host.log")).write_text(json.dumps(diagnostic, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--node", default="node")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--web-root", type=Path, default=ROOT / "src/web/dist")
    parser.add_argument("--results-directory", type=Path)
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--keep-fixtures", action="store_true", help="Keep temporary data for debugging after stopping all test processes")
    args = parser.parse_args()
    web_root = args.web_root.resolve()
    if not (web_root / "index.html").is_file():
        parser.error("Build src/web first, or pass --web-root pointing at an actual production build.")
    if not (HERE / "node_modules/playwright/package.json").is_file():
        parser.error("Run npm ci and npx playwright install chromium in src/tests/federation-browser-smoke first.")
    results = (args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-browser-results-"))).resolve()
    results.mkdir(parents=True, exist_ok=True)
    (results / "result.json").write_text(json.dumps({"passed": False, "status": "running"}))
    fixture = None
    processes, streams, hosts, host_processes = [], [], {}, {}
    try:
        if not args.no_build:
            for project in ("Bakabase.Federation.TestHost", "Bakabase.Client.TestHost"):
                with (results / (project + "-build.log")).open("w") as output:
                    subprocess.run([args.dotnet, "build", str(ROOT / "src/tests" / project / (project + ".csproj")),
                                    "--nologo", "-v:q"], cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                                   check=True, timeout=300)
        fixture = Path(tempfile.mkdtemp(prefix="bakabase-browser-fixture-"))
        deadline = time.monotonic() + args.timeout
        ports = set()
        for role in ("unified", "source", "client"):
            port = free_port()
            while port in ports:
                port = free_port()
            ports.add(port)
            directory = fixture / role
            project = "Bakabase.Client.TestHost" if role == "client" else "Bakabase.Federation.TestHost"
            dll = ROOT / "src/tests" / project / "bin/Debug/net9.0" / (project + ".dll")
            if not dll.is_file():
                raise AssertionError(f"Missing {dll}; run without --no-build.")
            stream = (fixture / (role + ".log")).open("w")
            streams.append(stream)
            env = {**os.environ, "BAKABASE_FEDERATION_TEST_WEB_ROOT": str(web_root),
                   "Analytics__Sentry__BackendDsn": "", "Analytics__Sentry__ClientDsn": "",
                   # An old client must ignore this conflicting unified setting.
                   "BAKABASE_DATA_DIR": str(fixture / "unified"),
                   "DOTNET_ENVIRONMENT": "Development"}
            command = [args.dotnet, str(dll), str(port), str(directory)]
            if role != "client":
                command.append("57")
            process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=os.name == "posix")
            processes.append(process)
            host_processes[role] = process
            hosts[role] = {"base": f"http://127.0.0.1:{port}", "directory": str(directory)}
        startup_deadline = min(deadline, time.monotonic() + 90)
        while not all((Path(host["directory"]) / "ready").exists() for host in hosts.values()):
            if any(process.poll() is not None for process in processes):
                raise AssertionError(f"A fixture exited during startup; inspect sanitized *-host.log diagnostics in {results}")
            if time.monotonic() > startup_deadline:
                raise TimeoutError("Fixture startup timed out")
            time.sleep(0.2)
        config = fixture / "browser-config.json"
        config.write_text(json.dumps({"hosts": hosts, "results": str(results), "repo": str(ROOT)}))
        with (results / "browser.log").open("w") as output:
            browser = subprocess.Popen([args.node, str(HERE / "browser.cjs"), str(config)], cwd=ROOT,
                                       stdout=output, stderr=subprocess.STDOUT,
                                       start_new_session=os.name == "posix")
            processes.append(browser)
            code = browser.wait(timeout=max(1, deadline - time.monotonic()))
            if code:
                raise AssertionError(f"Browser checks failed (exit {code}); inspect {results / 'browser.log'}")
        print(f"PASS: legacy migration and browser flows. Results: {results}")
        return 0
    except Exception as error:
        (results / "result.json").write_text(json.dumps({"passed": False, "status": "failed", "reason": str(error)}, indent=2))
        raise
    finally:
        for process in reversed(processes):
            stop_fixture_process(process)
        for stream in streams:
            stream.close()
        if fixture is not None:
            try:
                retain_host_diagnostics(fixture, hosts, host_processes, results)
            finally:
                if args.keep_fixtures:
                    print(f"Stopped fixture processes; retained temporary data: {fixture}")
                else:
                    shutil.rmtree(fixture, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
