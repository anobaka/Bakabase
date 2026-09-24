#!/usr/bin/env python3
"""Run isolated federated browser, legacy-pairing import and server-switching flows.

Requires the built web app and this directory's pinned Playwright/Chromium installation.
Every listener (and the relays the unified host starts), database and credential is
temporary; no installed application is used.
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

# Every analytics key the Service reads, blank, and anonymous tracking off: a fixture serves the
# production frontend, which would otherwise start Clarity, GA4, PostHog and Sentry with the
# shipped project ids. The test host turns the same things off itself and refuses to report
# ready while any is set; this is the runner saying so for its part.
ANALYTICS_OFF = {
    "Analytics__Clarity__ProjectId": "", "Analytics__Ga4__MeasurementId": "",
    "Analytics__Sentry__FrontendDsn": "", "Analytics__Sentry__BackendDsn": "",
    "Analytics__PostHog__ApiKey": "", "Analytics__PostHog__ApiHost": "",
    "App__EnableAnonymousDataTracking": "false",
}

# What a request log line may carry (RequestRecorder in the test host). Anything else is
# dropped when a failed run's logs are kept.
REQUEST_LOG_FIELDS = ("id", "method", "path", "site", "dest", "origin", "websocket", "ticket", "cookie", "signature",
                      "device", "status", "denial", "csp")


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
        if process is None and not path.is_file():
            # Started by the browser stage, which never got that far.
            continue
        # Bound memory even when a failing host produced a large log.
        with path.open("rb") as stream:
            stream.seek(max(0, path.stat().st_size - 128 * 1024))
            tail = stream.read().decode("utf-8", errors="replace")
        exceptions = list(dict.fromkeys(re.findall(
            r"\b((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*Exception)(?=[:\s]|$)", tail)))
        methods = list(dict.fromkeys(re.findall(
            r"^\s+at ((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*)(?=[(<])", tail, re.MULTILINE)))
        diagnostic = {
            "role": role, "exitCode": None if process is None else process.returncode,
            "ready": (Path(hosts[role]["directory"]) / "ready").exists(),
            "logBytes": path.stat().st_size,
            "failureCategories": [label for label in categories if label in tail.lower()],
            "exceptionTypes": exceptions[-30:], "stackMethods": methods[-50:],
            "notice": "Sanitized diagnostic summary; raw messages, ready payloads, paths and arguments omitted. Exit code may reflect runner cleanup.",
        }
        (results / (role + "-host.log")).write_text(json.dumps(diagnostic, indent=2))


def legacy_client_directory(fixture):
    """Where an old thin client's AppData would be. The removed program is not run: the browser
    stage writes the pairing it would have left there (legacy-client.cjs), for the desktop
    fixtures to import."""
    return fixture / "client"


def host_environment(role, port, fixture, web_root):
    """The environment one fixture host starts with.

    "unified" and "first-launch" are composed as the desktop app, each with this browser as its
    window and an old thin client's data directory to import from. The two long-lived Services
    record what reaches them, and every Service has a name of its own, so a check by name can
    tell them apart.
    """
    env = {**os.environ, **ANALYTICS_OFF, "BAKABASE_FEDERATION_TEST_WEB_ROOT": str(web_root),
           "DOTNET_ENVIRONMENT": "Development"}
    for key in ("BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW", "BAKABASE_CLIENT_DATA_DIR",
                "BAKABASE_FEDERATION_TEST_REQUEST_LOG", "BAKABASE_FEDERATION_TEST_SERVER_NAME"):
        env.pop(key, None)
    if role in ("unified", "first-launch"):
        # The desktop app: its own server plus the relays that manage other servers. Its window
        # is this browser, and it reads an old thin client's pairings from the fixture's client
        # directory — the variable that thin client's own AppData profile answered to.
        env["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"] = f"http://localhost:{port}"
        env["BAKABASE_CLIENT_DATA_DIR"] = str(legacy_client_directory(fixture))
    if role in ("unified", "source"):
        # What reaches each server and how it answered: the managed server's side of the
        # relay, and this device's own answers to the managed server's page.
        env["BAKABASE_FEDERATION_TEST_REQUEST_LOG"] = str(fixture / (role + "-requests.jsonl"))
    env["BAKABASE_FEDERATION_TEST_SERVER_NAME"] = "fixture-" + role
    return env


def retain_request_logs(fixture, results):
    """Keeps each Service's request log after a failed run: the evidence of what reached which
    server and how it answered. Only the recorder's own fields survive the copy."""
    for log in sorted(fixture.glob("*-requests.jsonl")):
        kept = []
        for line in log.read_text(errors="replace").splitlines():
            try:
                entry = json.loads(line)
            except ValueError:
                continue
            if isinstance(entry, dict):
                kept.append(json.dumps({key: entry[key] for key in REQUEST_LOG_FIELDS if key in entry}) + "\n")
        (results / log.name).write_text("".join(kept))


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
    passed = False
    processes, streams, hosts, host_processes = [], [], {}, {}
    first_launch_directory = None
    try:
        if not args.no_build:
            project = "Bakabase.Federation.TestHost"
            with (results / (project + "-build.log")).open("w") as output:
                subprocess.run([args.dotnet, "build", str(ROOT / "src/tests" / project / (project + ".csproj")),
                                "--nologo", "-v:q"], cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                               check=True, timeout=300)
        fixture = Path(tempfile.mkdtemp(prefix="bakabase-browser-fixture-"))
        deadline = time.monotonic() + args.timeout
        ports = set()

        def take_port():
            port = free_port()
            while port in ports:
                port = free_port()
            ports.add(port)
            return port

        def dll_of(project):
            dll = ROOT / "src/tests" / project / "bin/Debug/net9.0" / (project + ".dll")
            if not dll.is_file():
                raise AssertionError(f"Missing {dll}; run without --no-build.")
            return dll

        for role in ("unified", "source"):
            port = take_port()
            directory = fixture / role
            stream = (fixture / (role + ".log")).open("w")
            streams.append(stream)
            env = host_environment(role, port, fixture, web_root)
            command = [args.dotnet, str(dll_of("Bakabase.Federation.TestHost")), str(port), str(directory), "57"]
            process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=os.name == "posix")
            processes.append(process)
            host_processes[role] = process
            hosts[role] = {"base": f"http://127.0.0.1:{port}", "directory": str(directory),
                           "requestLog": env["BAKABASE_FEDERATION_TEST_REQUEST_LOG"]}
            if role == "unified":
                hosts[role]["window"] = env["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"]
        # A fresh desktop install on a machine where an old thin client had paired. The browser
        # stage starts it once that pairing is on disk, and stops it again; it runs in the
        # browser's process group, so the cleanup below reaches it too.
        port = take_port()
        first_launch_env = host_environment("first-launch", port, fixture, web_root)
        first_launch = {
            "base": f"http://127.0.0.1:{port}", "directory": str(fixture / "first-launch"),
            "window": first_launch_env["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"],
            "command": [args.dotnet, str(dll_of("Bakabase.Federation.TestHost")), str(port),
                        str(fixture / "first-launch"), "0"],
            # Named like the others, so its sanitized summary is kept the same way.
            "log": str(fixture / "first-launch.log"),
            # Only what differs from the browser's own environment, which the host inherits.
            "env": {key: value for key, value in first_launch_env.items() if os.environ.get(key) != value},
            "unset": [key for key in os.environ if key not in first_launch_env],
        }
        first_launch_directory = first_launch["directory"]
        startup_deadline = min(deadline, time.monotonic() + 90)
        while not all((Path(host["directory"]) / "ready").exists() for host in hosts.values()):
            if any(process.poll() is not None for process in processes):
                raise AssertionError(f"A fixture exited during startup; inspect sanitized *-host.log diagnostics in {results}")
            if time.monotonic() > startup_deadline:
                raise TimeoutError("Fixture startup timed out")
            time.sleep(0.2)
        config = fixture / "browser-config.json"
        config.write_text(json.dumps({"hosts": hosts, "firstLaunch": first_launch,
                                      "legacyClient": {"directory": str(legacy_client_directory(fixture))},
                                      "results": str(results), "repo": str(ROOT)}))
        with (results / "browser.log").open("w") as output:
            browser = subprocess.Popen([args.node, str(HERE / "browser.cjs"), str(config)], cwd=ROOT,
                                       stdout=output, stderr=subprocess.STDOUT,
                                       start_new_session=os.name == "posix")
            processes.append(browser)
            code = browser.wait(timeout=max(1, deadline - time.monotonic()))
            if code:
                raise AssertionError(f"Browser checks failed (exit {code}); inspect {results / 'browser.log'}")
        passed = True
        print(f"PASS: federated browser, legacy-pairing import and server-switching flows. Results: {results}")
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
                diagnosed_hosts, diagnosed_processes = dict(hosts), dict(host_processes)
                if first_launch_directory is not None:
                    # The browser stage's own host: no process here, only its log.
                    diagnosed_hosts["first-launch"] = {"directory": first_launch_directory}
                    diagnosed_processes["first-launch"] = None
                retain_host_diagnostics(fixture, diagnosed_hosts, diagnosed_processes, results)
                if not passed:
                    retain_request_logs(fixture, results)
            finally:
                if args.keep_fixtures:
                    print(f"Stopped fixture processes; retained temporary data: {fixture}")
                else:
                    shutil.rmtree(fixture, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
