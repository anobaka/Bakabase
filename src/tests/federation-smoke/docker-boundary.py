#!/usr/bin/env python3
"""Real macOS portable <-> production Docker Service acceptance, using business APIs only.

No TestHost, build, image pull, production feed, existing profile or user container
is used. Results must be a new /tmp directory. Execution requires an already
built test-only VelopackIsolationHook and explicitly supplied source provenance.
"""
import argparse
from contextlib import closing
import hashlib
import importlib.util
import ipaddress
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import sqlite3
import subprocess
import sys
import time
import traceback
import urllib.parse
import uuid
import wave

# Also allow importlib-based unit tests without changing the repository's package layout.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from docker_boundary_support import (BoundaryFailure, Deadline, LABEL, OwnedDocker,
    free_port, host_request, sanitized_log, start_deny_proxy, stop_native)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def prepare_data(directory, port):
    (directory / "configs").mkdir(parents=True)
    (directory / "app.json").write_text(json.dumps({"App": {"language": "en-US",
        "listeningPorts": [port], "autoListeningPortCount": 0,
        "enableAnonymousDataTracking": False, "maxParallelism": 1}}))
    (directory / "configs/remote-access.json").write_text(json.dumps({"RemoteAccess": {
        "ServerId": uuid.uuid4().hex, "Mode": 1, "RequirePairing": True,
        "AllowLiveTranscode": False}}))


def make_audio(path, seed):
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(8000)
        output.writeframes(bytes((index + seed) % 251 for index in range(16000)))


def validate_created(items):
    if not isinstance(items, list) or len(items) != 17:
        raise BoundaryFailure("Resource creation did not return 17 items")
    ids = [item.get("resourceId") for item in items]
    if any(type(value) is not int or value <= 0 for value in ids) or len(set(ids)) != 17 or any(item.get("error") for item in items):
        raise BoundaryFailure("Resource creation returned missing, duplicate or failed IDs")
    return ids


def check_database(directory):
    path = directory / "bakabase_insideworld.db"
    try:
        # Both owners have stopped. Open the existing fixture database without
        # creating a missing file, allowing SQLite to recover its WAL/journal.
        # A read-only connection cannot always open the app's remaining WAL.
        with closing(sqlite3.connect(path.as_uri() + "?mode=rw", uri=True, timeout=5)) as connection:
            assert connection.execute("PRAGMA integrity_check").fetchall() == [("ok",)]
            count = connection.execute("SELECT COUNT(*) FROM ResourcesV2").fetchone()[0]
            played = connection.execute("SELECT COUNT(*) FROM ResourcesV2 WHERE PlayedAt IS NOT NULL").fetchone()[0]
            assert count == 17 and played == 0
    except sqlite3.Error as error:
        # These fixed schema-only queries run against our synthetic fixture;
        # retain the SQLite diagnostic without dumping rows or application logs.
        raise BoundaryFailure(f"{directory.name} SQLite verification failed: {error}") from error
    return {"integrity": "ok", "resources": count, "playedAtWrites": played,
            "connectionMode": "rw-existing-after-owner-stopped"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-portable", type=Path, required=True)
    parser.add_argument("--hook", type=Path, required=True)
    parser.add_argument("--docker-image", required=True)
    parser.add_argument("--native-host", required=True,
        help="This Mac's private LAN IPv4 address, reachable from Docker without loopback source translation")
    parser.add_argument("--curl-image", default="curlimages/curl:latest")
    parser.add_argument("--provenance", type=Path, required=True)
    parser.add_argument("--results-directory", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--minimum-free-gib", type=int, default=5)
    args = parser.parse_args()
    try:
        native_host = ipaddress.IPv4Address(args.native_host)
        if not native_host.is_private or native_host.is_loopback or native_host.is_unspecified or native_host.is_multicast:
            raise ValueError()
    except ValueError:
        parser.error("--native-host must be this Mac's non-loopback private IPv4 address")
    if platform.system() != "Darwin" or args.timeout <= 0 or args.minimum_free_gib < 1:
        parser.error("Requires macOS and positive time/disk budgets")
    root = args.results_directory.resolve()
    if root.exists() or not root.is_relative_to(Path("/tmp").resolve()):
        parser.error("Results must be a new directory below /tmp")
    if not all(path.is_file() for path in (args.native_portable, args.hook, args.provenance)):
        parser.error("All package, hook and provenance inputs must exist")
    provenance = json.loads(args.provenance.read_text())
    if not all(re.fullmatch(r"[0-9a-f]{40}", provenance.get(key, "")) for key in ("nativeSourceCommit", "dockerSourceCommit")):
        parser.error("Provenance requires full nativeSourceCommit and dockerSourceCommit SHAs")
    existing = subprocess.run(["pgrep", "-x", "Bakabase"], capture_output=True, timeout=5)
    if existing.returncode != 1:
        parser.error("Close existing native Bakabase processes before running this fixture")
    if shutil.disk_usage(Path("/tmp").resolve()).free < args.minimum_free_gib * 1024**3:
        parser.error("Insufficient free space for isolated native and Docker data")
    root.mkdir(parents=True)
    (root / ".bakabase-upgrade-test").write_text("owned Docker boundary fixture\n")
    for name in ("installed", "packages", "temp", "media"):
        (root / name).mkdir()
    deadline = Deadline(args.timeout, root, args.minimum_free_gib * 1024**3)
    owned = OwnedDocker(deadline)
    report = {"passed": False, "provenance": provenance, "checks": {},
        "host": platform.platform(), "architecture": platform.machine(),
        "limitations": ["Same macOS host and Docker VM; not a physical NAS or physical LAN.",
            "Production Linux amd64 Service runs through the selected Docker runtime (emulated on ARM).",
            "Test-only startup hook isolates Velopack directories; no update is invoked.",
            "Child proxy settings are not a network sandbox; macOS-managed WebKit caches are outside AppData isolation."]}
    native = stream = proxy = None
    started = time.monotonic()
    report["readiness"] = {name: {"stage": "notStarted", "appVersion": None,
        "infoCoreVersion": None, "lastErrorType": None, "lastBoundaryFailure": None}
        for name in ("native", "docker")}
    def progress():
        # This live snapshot deliberately excludes response bodies, addresses,
        # resource/session IDs and credentials, including while a request blocks.
        path = root / "progress.json.tmp"
        path.write_text(json.dumps({"elapsedSeconds": round(time.monotonic() - started, 2),
            "readiness": report["readiness"], "checks": report["checks"]}, indent=2) + "\n")
        path.replace(root / "progress.json")
    def complete(name):
        report["checks"][name] = True
        progress()
        print("CHECK PASSED: " + name, flush=True)
    progress()
    def expired(*_):
        raise BoundaryFailure("Overall deadline exceeded")
    previous_alarm = signal.signal(signal.SIGALRM, expired)
    previous_term = signal.signal(signal.SIGTERM, expired)
    signal.setitimer(signal.ITIMER_REAL, args.timeout)
    try:
        # With Docker's containerd store, platform inspection returns a child
        # manifest ID that cannot itself be used as a locally cached image name.
        # Pin the index first, then inspect and run its explicit amd64 platform.
        service_index = json.loads(owned.command("image", "inspect", args.docker_image).stdout)[0]
        service_image = json.loads(owned.command("image", "inspect", "--platform", "linux/amd64", service_index["Id"]).stdout)[0]
        curl_image = json.loads(owned.command("image", "inspect", args.curl_image).stdout)[0]
        if service_image["Os"] != "linux" or service_image["Architecture"] != "amd64" or curl_image["Os"] != "linux":
            raise BoundaryFailure("Expected an existing production Linux amd64 image and Linux curl image")
        owned.curl_id = curl_image["Id"]
        owned.curl_platform = "linux/" + curl_image["Architecture"]
        report["docker"] = {"requestedImage": args.docker_image, "imageId": service_index["Id"],
            "platformManifestId": service_image["Id"],
            "architecture": service_image["Architecture"], "repoDigests": service_image.get("RepoDigests", []),
            "curlImageId": owned.curl_id, "curlPlatform": owned.curl_platform,
            "runtime": json.loads(owned.command("info", "--format", "{{json .}}").stdout)["OperatingSystem"]}
        spec = importlib.util.spec_from_file_location("upgrade_unpack", Path(__file__).parent.parent / "upgrade-tests/run-velopack-macos.py")
        upgrade = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(upgrade)
        upgrade.unpack_portable(args.native_portable, root / "installed")
        app = root / "installed/Bakabase.app/Contents/MacOS"
        report["native"] = {"portableSha256": digest(args.native_portable),
            "serviceAssemblySha256": digest(app / "Bakabase.Service.dll"), "manifest": upgrade.manifest(app / "sq.version")}
        native_port, published_port = free_port(), free_port()
        while published_port == native_port:
            published_port = free_port()
        prepare_data(root / "appdata", native_port)
        prepare_data(root / "docker-data", 34567)
        media_files = {role: root / "media" / (role + ".wav") for role in ("native", "docker")}
        for seed, path in enumerate(media_files.values()):
            make_audio(path, seed * 97)
        file_hashes = {role: digest(path) for role, path in media_files.items()}
        network = "bakabase-boundary-" + owned.token
        owned.networks.append(network)
        owned.command("network", "create", "--driver", "bridge", "--label", f"{LABEL}={owned.token}", network)
        container = network + "-service"
        owned.containers.append(container)
        owned.service_id = owned.command("create", "--pull", "never", "--name", container,
            "--label", f"{LABEL}={owned.token}", "--platform", "linux/amd64", "--network", network,
            "--cpus", "2", "--memory", "2g", "--pids-limit", "256", "--log-opt", "max-size=1m", "--log-opt", "max-file=1",
            "-p", f"127.0.0.1:{published_port}:34567", "-v", f"{root / 'docker-data'}:/data",
            "-v", f"{root / 'media'}:/media:ro", "-e", "BAKABASE_DATA_DIR=/data",
            # The production ASP.NET base image defaults to port 8080. Docker
            # hosts resolve ports from environment, not desktop app.json.
            "-e", "ASPNETCORE_HTTP_PORTS=34567",
            "-e", "BAKABASE_UPDATE_URL=http://127.0.0.1:1", "-e", "Analytics__Sentry__BackendDsn=",
            service_index["Id"]).stdout.decode().strip()
        owned.service("start")
        native_base, docker_base = f"http://127.0.0.1:{native_port}", f"http://127.0.0.1:{published_port}"
        # OrbStack's host.docker.internal forwards through host loopback. Use
        # the explicit LAN address so the forged-Host checks exercise a real
        # non-loopback source address, retaining the same denial assertions.
        bridge_base = f"http://{native_host}:{native_port}"
        owned.allowed_bases.add(bridge_base)
        report["topology"] = {"nativeOwner": native_base, "nativeToDocker": docker_base,
            "dockerToNative": bridge_base, "dockerOwner": "http://127.0.0.1:34567",
            "dockerPortEnvironment": {"ASPNETCORE_HTTP_PORTS": "34567"},
            "dockerOwnerTransport": "curl helper sharing only the owned Service network namespace"}
        proxy = start_deny_proxy()
        proxy_url = f"http://127.0.0.1:{proxy.server_port}"
        environment = {**os.environ, "BAKABASE_UPGRADE_TEST_ROOT": str(root), "BAKABASE_DATA_DIR": str(root / "appdata"),
            "DOTNET_STARTUP_HOOKS": str(args.hook.resolve()), "TMPDIR": str(root / "temp"),
            "BAKABASE_UPDATE_URL": proxy_url, "Analytics__Sentry__BackendDsn": "",
            "NO_PROXY": "localhost,127.0.0.1,::1", "no_proxy": "localhost,127.0.0.1,::1"}
        for key in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"):
            environment[key] = proxy_url
        stream = (root / "temp/native.log").open("wb")
        native = subprocess.Popen([str(app / "Bakabase")], cwd=app, env=environment,
            stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        native_api = lambda *a, **kw: host_request(deadline, native_base, *a, **kw)
        nodes = [{"name": "native", "api": native_api, "data": root / "appdata", "path": str(media_files["native"])},
                 {"name": "docker", "api": owned.request, "data": root / "docker-data", "path": "/media/docker.wav"}]
        def ready(node):
            name = node["name"]
            diagnostic = report["readiness"][name]
            def update(stage, **values):
                changed = diagnostic["stage"] != stage
                diagnostic.update(stage=stage, **values)
                progress()
                if changed:
                    print("READY STAGE: " + name + "." + stage, flush=True)
            def version(value):
                # Versions are the only fields copied from app/info or options.
                return value if isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9.+_-]{1,128}", value) else None
            until = time.monotonic() + deadline.remaining(120)
            update("waitingForAppInfo", attempts=0)
            while time.monotonic() < until:
                if native.poll() is not None:
                    message = "Owned native process exited while waiting for " + name
                    update("failed", lastErrorType="BoundaryFailure", lastBoundaryFailure=message)
                    raise BoundaryFailure(message)
                diagnostic["attempts"] += 1
                try:
                    diagnostic["lastOperation"] = "readAppOptions"
                    progress()
                    options = json.loads((node["data"] / "app.json").read_text(encoding="utf-8-sig"))["App"]
                    diagnostic["appVersion"] = version(options.get("version"))
                    diagnostic["lastOperation"] = "requestAppInfo"
                    progress()
                    info = node["api"]("/app/info")
                    diagnostic["infoCoreVersion"] = version(info.get("coreVersion"))
                    if options.get("version") == info["coreVersion"]:
                        update("checkingAppDataPath", lastOperation="validateAppDataPath")
                        expected = str(root / "appdata") if node["name"] == "native" else "/data"
                        assert info["appDataPath"].rstrip("/") == expected
                        update("ready", lastOperation="complete")
                        return {key: info.get(key) for key in ("coreVersion", "appDataPath", "runtimeMode")}
                    update("waitingForMigration", lastOperation="compareVersions")
                except (BoundaryFailure, OSError, ValueError, KeyError) as error:
                    update("waitingForAppInfo" if diagnostic["lastOperation"] == "requestAppInfo" else "waitingForAppOptions",
                        lastErrorType=type(error).__name__,
                        lastBoundaryFailure=str(error) if isinstance(error, BoundaryFailure) else None)
                except AssertionError as error:
                    update("failed", lastErrorType=type(error).__name__, lastBoundaryFailure=None)
                    raise
                time.sleep(min(0.5, deadline.remaining()))
            message = "Owned " + name + " application readiness/migration deadline exceeded"
            update("timedOut", timeoutMessage=message)
            raise BoundaryFailure(message)
        for node in nodes:
            node["appInfo"] = ready(node)
            node["api"]("/app/terms", "POST")
            state = node["api"]("/federation/local/peers")
            assert state["browsingEnabled"] is False and state["sharingEnabled"] is False
            assert state["remoteAccessMode"] == 1 and state["requirePairing"] is True
            node["identity"] = state["identity"]
            node["id"] = state["identity"]["nodeId"]
            denied = node["api"]("/federation/local/queries", "POST", {"nodeIds": [node["id"]], "query": {}}, expected=403)
            assert denied["code"] == "BrowsingDisabled"
            created = node["api"]("/resource/placeholder", "POST", {"items": [{"title": node["name"] + f" resource {i:02}"} for i in range(17)], "acquireImmediately": False})
            node["ids"] = validate_created(created)
            materialized = node["api"](f'/resource/{node["ids"][0]}/materialize', "POST", {"path": node["path"], "mergeIfOccupied": False})
            assert materialized["materialized"] is True and materialized["merged"] is False
            for setting in ("browsing", "sharing"):
                node["api"]("/federation/local/peers/" + setting, "PUT", {"enabled": True})
        a, b = nodes
        assert a["id"] != b["id"]
        complete("defaultOffAndBusinessApiSeed")
        report["nodes"] = [{"name": n["name"], "identity": n["identity"], "appInfo": n["appInfo"], "resourceIds": n["ids"]} for n in nodes]
        def query(node, ids):
            return node["api"]("/federation/local/queries", "POST", {"nodeIds": ids, "pageSize": 7, "query": {"queryContractVersion": 1, "sort": "NameAsc"}})
        def release(node, page):
            node["api"](f'/federation/local/queries/{page["sessionId"]}', "DELETE", expected=204)
        def cursor(page):
            return f'/federation/local/queries/{page["sessionId"]}/pages?cursor=' + urllib.parse.quote(page["nextCursor"], safe="")
        def pair(reader, source, address):
            invite = source["api"]("/federation/local/peers/invite", "POST")
            result = reader["api"]("/federation/local/peers/connect", "POST", {"address": address, "code": invite["code"]})
            assert result["outcome"] == "granted"
        pair(a, b, docker_base)
        denied = b["api"]("/federation/local/queries", "POST", {"nodeIds": [a["id"]], "query": {}}, expected=503)
        assert denied["code"] == "PeerUnavailable"
        pair(b, a, bridge_base)
        complete("separateDirectionalAuthorization")
        for node in nodes:
            first = page = query(node, [a["id"], b["id"]])
            items = list(page["items"])
            assert page["coverageComplete"] and page["totalWithinParticipants"] == 34
            while page.get("nextCursor"):
                path = cursor(page)
                page = node["api"](path)
                replay = node["api"](path)
                assert replay["items"] == page["items"] and replay["nextCursor"] == page["nextCursor"]
                items.extend(page["items"])
            refs = {(item["ref"]["nodeId"], item["ref"]["libraryEpoch"], item["ref"]["resourceId"]) for item in items}
            expected = {(n["id"], n["identity"]["libraryEpoch"], rid) for n in nodes for rid in n["ids"]}
            assert len(items) == 34 and refs == expected
            release(node, first)
        complete("bothDirections34RowsPaginationReplayIdentity")
        def media(reader, source):
            ref = {"nodeId": source["id"], "libraryEpoch": source["identity"]["libraryEpoch"], "resourceId": source["ids"][0]}
            detail = reader["api"]("/federation/local/resources/resolve", "POST", {"refs": [ref]})["resources"][0]
            assert detail["ref"] == ref
            asset = next(asset for asset in detail["assets"] if asset["kind"] == "audio")
            ticket = reader["api"]("/federation/local/playback-sessions", "POST", {"assetRef": {"resourceRef": ref, "assetId": asset["assetId"]}, "mode": "preview"})
            path = urllib.parse.urlsplit(ticket["url"]).path
            assert path.startswith("/federation/local/")
            expected = media_files[source["name"]].read_bytes()
            for start, end in ((0, 63), (128, 255)):
                assert reader["api"](path, headers={"Range": f"bytes={start}-{end}"}, expected=206, raw=True) == expected[start:end + 1]
            return path
        media(a, b)
        media(b, a)
        complete("bidirectionalRemoteDetailAndExactMediaRanges")
        for api, port in ((lambda *a, **kw: host_request(deadline, docker_base, *a, **kw), 34567),
                          (lambda *a, **kw: owned.request(*a, base=bridge_base, **kw), native_port)):
            for headers in ({}, {"X-Forwarded-For": "127.0.0.1", "Forwarded": "for=127.0.0.1", "Host": f"localhost:{port}", "Origin": f"http://localhost:{port}"}):
                api("/federation/local/peers", expected=403, headers=headers, raw=True)
            api("/federation/v1/export/mapping-roots", expected=401, raw=True)
        complete("remoteAndForwardedLocalControlDeniedAndExportRequiresGrant")
        owned.service("stop")
        partial = query(a, [a["id"], b["id"]])
        assert not partial["coverageComplete"] and partial["totalWithinParticipants"] == 17
        assert any(node["nodeId"] == b["id"] for node in partial["omittedNodes"])
        release(a, partial)
        owned.service("start")
        ready(b)
        assert b["api"]("/federation/local/peers")["identity"] == b["identity"]
        for reader, source in ((a, b), (b, a)):
            recovered = query(reader, [source["id"]])
            assert recovered["coverageComplete"] and recovered["totalWithinParticipants"] == 17
            release(reader, recovered)
        complete("dockerOfflineCoverageAndRestartWithoutRePairing")
        a["api"]("/federation/local/peers/browsing", "PUT", {"enabled": False})
        shared = query(b, [a["id"]])
        assert shared["coverageComplete"] and shared["totalWithinParticipants"] == 17
        release(b, shared)
        a["api"]("/federation/local/peers/browsing", "PUT", {"enabled": True})
        complete("nativeBrowsingOffStillSharesToDocker")
        ticket = media(a, b)
        active = query(a, [b["id"]])
        cached = cursor(active)
        a["api"](cached)
        state = b["api"]("/federation/local/peers")
        grant = next(p["inboundGrant"]["grantId"] for p in state["peers"] if p["nodeId"] == a["id"])
        b["api"]("/federation/local/peers/grants/" + grant, "DELETE")
        a["api"](cached, expected=(401, 403, 409, 410, 502, 503), raw=True)
        a["api"](ticket, expected=(401, 403, 409, 410), raw=True)
        release(a, active)
        complete("sourceRevocationDeniesCachedPageAndMedia")
        owned.service("stop")
        stop_native(native)
        report["databases"] = {node["name"]: check_database(node["data"]) for node in nodes}
        assert {role: digest(path) for role, path in media_files.items()} == file_hashes
        report["mediaSha256"] = file_hashes
        complete("stoppedDatabaseIntegrityAndNoPlayedAtOrFileChanges")
        report["passed"] = True
    except BaseException as error:
        report["error"] = {"type": type(error).__name__, "message": str(error) if isinstance(error, BoundaryFailure) else "Fixture assertion or operation failed; inspect sanitized diagnostics."}
        report["error"]["frames"] = [{"file": Path(frame.filename).name, "line": frame.lineno, "function": frame.name}
                                     for frame in traceback.extract_tb(error.__traceback__)[-6:]]
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous_alarm)
        signal.signal(signal.SIGTERM, previous_term)
        cleanup_errors = []
        try:
            stop_native(native)
        except Exception as error:
            cleanup_errors.append(type(error).__name__)
        try:
            if stream:
                stream.close()
            native_log = root / "temp/native.log"
            if native_log.exists():
                with native_log.open("rb") as source:
                    source.seek(max(0, native_log.stat().st_size - 128 * 1024))
                    (root / "native-diagnostics.json").write_text(json.dumps(sanitized_log(source.read()), indent=2))
        except Exception as error:
            cleanup_errors.append(type(error).__name__)
        try:
            if owned.service_id and owned.inspect_owned("container", owned.service_id):
                logs = owned.command("logs", "--tail", "1000", owned.service_id, cleanup=True)
                (root / "docker-diagnostics.json").write_text(json.dumps(sanitized_log(logs.stdout + logs.stderr), indent=2))
        except Exception as error:
            cleanup_errors.append(type(error).__name__)
        cleanup_errors.extend(owned.cleanup())
        try:
            if proxy:
                proxy.shutdown()
                proxy.server_close()
        except Exception as error:
            cleanup_errors.append(type(error).__name__)
        stopped = native is None or native.poll() is not None
        if not cleanup_errors and stopped:
            for name in ("installed", "packages", "temp", "media", "appdata", "docker-data"):
                try:
                    if (root / name).exists():
                        shutil.rmtree(root / name)
                except Exception as error:
                    cleanup_errors.append(type(error).__name__)
        report["cleanup"] = {"errors": cleanup_errors, "nativeStopped": stopped,
            "containersRemoved": not owned.containers, "networksRemoved": not owned.networks,
            "workDirectoriesRemoved": all(not (root / name).exists() for name in ("installed", "packages", "temp", "media", "appdata", "docker-data"))}
        report["passed"] = report["passed"] and not cleanup_errors and all(report["cleanup"][key] for key in ("nativeStopped", "containersRemoved", "networksRemoved", "workDirectoriesRemoved"))
        report["elapsedSeconds"] = round(time.monotonic() - started, 2)
        (root / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(("PASS" if report["passed"] else "FAIL") + ": native/Docker boundary; evidence " + str(root / "report.json"))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
