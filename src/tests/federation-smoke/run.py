#!/usr/bin/env python3
"""Three independent production Service hosts; no fake remote APIs or shared AppService statics.

Build Bakabase.Federation.TestHost first. Run with --dotnet /path/to/dotnet.
Only temporary fixture directories are used. --keep leaves the three hosts for manual UI checks.
After management checks, datasync.py runs data sync across three more hosts within the same
deadline (--skip-datasync leaves it out).
"""
import argparse
from contextlib import closing
import json
import os
from pathlib import Path
import socket
import shutil
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

import datasync

REQUEST_DEADLINE = None


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def request(base, path, method="GET", body=None, expected=200, headers=None, raw=False):
    remaining = 15 if REQUEST_DEADLINE is None else REQUEST_DEADLINE - time.monotonic()
    if remaining <= 0:
        raise TimeoutError("Federation smoke overall deadline exceeded")
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method=method,
                                 headers={"Content-Type": "application/json", **(headers or {})})
    try:
        response = urllib.request.urlopen(req, timeout=min(15, remaining))
    except urllib.error.HTTPError as error:
        response = error
    with response:
        content = response.read()
        allowed = (expected,) if isinstance(expected, int) else expected
        if response.status not in allowed:
            # Protocol errors contain no credentials; do not print successful pairing responses.
            raise AssertionError(f"{method} {path}: HTTP {response.status}, expected {allowed}: {content[:1000]!r}")
        if raw:
            return response.status, dict(response.headers), content
        return json.loads(content) if content else None


def run(args):
    global REQUEST_DEADLINE
    REQUEST_DEADLINE = time.monotonic() + args.timeout
    repo = Path(__file__).resolve().parents[3]
    dll = repo / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0/Bakabase.Federation.TestHost.dll"
    if not dll.exists():
        raise SystemExit("Build src/tests/Bakabase.Federation.TestHost first.")
    root = Path(tempfile.mkdtemp(prefix="bakabase-federation-smoke-"))
    results = args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-federation-results-"))
    results.mkdir(parents=True, exist_ok=True)
    processes, streams, nodes = [], [], []
    print(f"Fixture directory: {root}; retained results: {results}", flush=True)
    try:
        for label in ("a", "b", "c"):
            directory = root / label
            port = free_port()
            log = (root / f"{label}.log").open("w")
            streams.append(log)
            process = subprocess.Popen([args.dotnet, str(dll), str(port), str(directory), "257"],
                                       cwd=repo, stdout=log, stderr=subprocess.STDOUT)
            processes.append(process)
            nodes.append({"base": f"http://127.0.0.1:{port}", "directory": directory, "pid": process.pid})
        deadline = min(time.monotonic() + 90, REQUEST_DEADLINE)
        while not all((node["directory"] / "ready").exists() for node in nodes):
            exited = [(node["pid"], process.returncode) for node, process in zip(nodes, processes)
                      if process.poll() is not None]
            if exited:
                raise AssertionError(f"Host exited during startup (pid, exit code): {exited}. Inspect {results}/*.log")
            if time.monotonic() > deadline:
                raise AssertionError(f"Startup did not finish. Inspect {root}/*.log")
            time.sleep(0.2)
        for node in nodes:
            # The test host blanks every analytics key and turns anonymous tracking off
            # (FixtureAnalytics) whatever this environment says; here is what its UI is told.
            analytics = request(node["base"], "/app/analytics-info")["data"]
            configured = [key for key in ("clarityProjectId", "ga4MeasurementId", "sentryDsn", "postHogApiKey")
                          if analytics.get(key)]
            assert not configured and analytics["enableAnonymousDataTracking"] is False, \
                f"A fixture would report to analytics: {configured or 'anonymous tracking on'}"
            node["status"] = request(node["base"], "/federation/local/peers")
            node["id"] = node["status"]["identity"]["nodeId"]
            node["epoch"] = node["status"]["identity"]["libraryEpoch"]
            assert "sharingEnabled" not in node["status"] and "peers" not in node["status"]
            page = request(node["base"], "/resource/search?saveSearch=false", "POST", {"page": 1, "pageSize": 20})
            assert page["totalCount"] == 257
        assert len({node["id"] for node in nodes}) == 3
        a, b, c = nodes

        def pair(manager, owner):
            code = request(owner["base"], "/remote-access/pairing/code", "POST")["data"]["code"]
            outcome = request(manager["base"], "/federation/local/servers/pair", "POST",
                              {"address": owner["base"], "code": code})
            assert outcome["outcome"] == 0, outcome["outcome"]
            assert outcome["serverId"] == owner["id"]

        def operation(manager, owner, operation, **fields):
            return request(manager["base"], f'/federation/local/management/{owner["id"]}/request', "POST",
                           {"libraryEpoch": owner["epoch"], "operation": operation, **fields}, expected=(200, 409, 503))

        pair(a, b)
        pair(b, c)
        listing = request(a["base"], "/federation/local/servers")
        assert [server["serverId"] for server in listing["servers"]] == [b["id"]], "Management is directional and not transitive"
        assert request(c["base"], "/federation/local/servers")["servers"] == []
        assert listing["servers"][0]["pathMappings"] == [], "Pairing must not invent a path mapping"

        # Resource metadata is pulled to the manager and searched there, still scoped to its owner.
        end = min(time.monotonic() + 60, REQUEST_DEADLINE)
        while True:
            page = operation(a, b, "resourceSearch", body={"page": 1, "pageSize": 20})
            if page.get("totalCount") == 257 and len(page.get("data", [])) == 20:
                break
            if time.monotonic() > end:
                raise AssertionError(f"Managed metadata did not become searchable: {page.get('code')}")
            time.sleep(0.2)
        print("PASS: explicit directional management pairing and owner-scoped local metadata search", flush=True)

        tree_path = f'/federation/local/servers/{b["id"]}/path-mapping-tree'
        tree = request(a["base"], tree_path)
        assert tree["libraryEpoch"] == b["epoch"]
        assert sum(item["resourceCount"] for item in tree["nodes"]) == 1, "B must not export C replica paths"
        assert len(tree["nodes"]) <= 200 and tree.get("nextCursor") is None
        directory = tree["nodes"][0]
        assert directory["serverPath"] and not directory.get("sourceRootId")
        children = request(a["base"], tree_path + "?" + urllib.parse.urlencode({
            "parent": directory["serverPath"], "libraryEpoch": b["epoch"], "limit": 200}))
        assert len(children["nodes"]) <= 200
        mappings_path = f'/federation/local/servers/{b["id"]}/path-mappings'
        mapping = {"serverPath": str(b["directory"]), "localPath": str(a["directory"])}
        request(a["base"], mappings_path, "PUT", {"mappings": [mapping], "expectedMappings": []})
        conflict = request(a["base"], mappings_path, "PUT", {"mappings": [], "expectedMappings": []}, expected=409)
        assert conflict["code"] == "PathMappingsChanged"
        assert request(a["base"], "/federation/local/servers")["servers"][0]["pathMappings"] == [mapping]
        request(a["base"], mappings_path, "PUT", {"mappings": [], "expectedMappings": [mapping]})
        print("PASS: owned path tree, actual prefixes, bounded pages and concurrent mapping edit protection", flush=True)

        media_path = f'/federation/local/management/{b["id"]}/media?' + urllib.parse.urlencode({
            "libraryEpoch": b["epoch"], "resourceId": 1, "origin": 2, "key": str(b["directory"] / "fixture.wav")})
        _, _, content = request(a["base"], media_path, headers={"Range": "bytes=0-63"}, expected=206, raw=True)
        assert len(content) == 64 and content[:4] == b"RIFF"
        _, headers, content = request(a["base"], media_path, method="HEAD", raw=True)
        assert not content and int(headers["Content-Length"]) == 16044
        request(a["base"], media_path, headers={"Range": "bytes=999999-"}, expected=416, raw=True)
        print("PASS: authenticated management media, HEAD and byte ranges", flush=True)

        for path, method in (("/federation/local/queries", "POST"),
                             ("/federation/local/peers/invite", "POST"),
                             ("/federation/v1/export/mapping-roots", "GET")):
            request(a["base"], path, method, {} if method == "POST" else None, expected=(403, 404))
        request(a["base"], "/federation/local/servers", expected=403, headers={"Origin": "https://evil.example"})
        request(a["base"], "/federation/local/servers", expected=403, headers={"Host": "evil.example"})
        request(b["base"], "/resource/search", "POST", {}, expected=403,
                headers={"Authorization": "Bakabase-Node malformed"})
        print("PASS: retired read-only endpoints are unavailable; management retains origin and signature gates", flush=True)

        processes[1].terminate()
        processes[1].wait(timeout=10)
        # The manager's synchronized metadata remains searchable after the owner goes offline.
        offline = operation(a, b, "resourceSearch", body={"page": 1, "pageSize": 20})
        assert offline["totalCount"] == 257 and len(offline["data"]) == 20
        database = next(b["directory"].rglob("bakabase_insideworld*.db"))
        with closing(sqlite3.connect(database)) as connection:
            assert connection.execute('select count(*) from ResourcesV2 where PlayedAt is not null').fetchone()[0] == 0
        print("PASS: local replica search survives owner outage; reads do not update owner play history", flush=True)
        report = {"passed": True, "ownedResourcesPerDevice": 257, "managementPairings": 2, "nodes": [{"url": n["base"], "nodeId": n["id"], "pid": n["pid"]} for n in nodes]}
        (results / "result.json").write_text(json.dumps(report, indent=2))
        print(json.dumps(report), flush=True)
    finally:
        if not args.keep:
            for process in processes:
                if process.poll() is None:
                    process.terminate()
                    try: process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=10)
        for stream in streams: stream.close()
        for label in ("a", "b", "c"):
            log = root / f"{label}.log"
            if log.exists():
                shutil.copyfile(log, results / f"{label}.log")
        if not args.keep:
            shutil.rmtree(root)
    if not args.skip_datasync:
        datasync.run(args.dotnet, REQUEST_DEADLINE, results / "datasync", keep=args.keep)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--timeout", type=int, default=300, help="Overall request/startup deadline in seconds")
    parser.add_argument("--results-directory", type=Path, help="Retain logs/result.json here; fixture databases are deleted")
    parser.add_argument("--skip-datasync", action="store_true", help="Leave out the data sync stage (datasync.py)")
    run(parser.parse_args())
