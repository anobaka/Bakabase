#!/usr/bin/env python3
"""Three independent production Service hosts; no fake remote APIs or shared AppService statics.

Build Bakabase.Federation.TestHost first. Run with --dotnet /path/to/dotnet.
Only temporary fixture directories are used. --keep leaves the three hosts for manual UI checks.
"""
import argparse
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
            node["status"] = request(node["base"], "/federation/local/peers")
            node["id"] = node["status"]["identity"]["nodeId"]
            assert node["status"]["browsingEnabled"] is False, "New installations must not opt into federation browsing"
            disabled = request(node["base"], "/federation/local/queries", "POST",
                               {"nodeIds": [node["id"]], "query": {}}, expected=403)
            assert disabled["code"] == "BrowsingDisabled"
            request(node["base"], "/federation/local/peers/browsing", "PUT", {"enabled": True})
        assert len({node["id"] for node in nodes}) == 3
        a, b, c = nodes

        def pair(reader, source):
            invite = request(source["base"], "/federation/local/peers/invite", "POST")
            result = request(reader["base"], "/federation/local/peers/connect", "POST",
                             {"address": source["base"], "code": invite["code"]})
            assert result["outcome"] == "granted", result["outcome"]

        def query(node, ids, page_size=50, **filters):
            return request(node["base"], "/federation/local/queries", "POST",
                           {"nodeIds": ids, "pageSize": page_size, "query": filters})

        def release(node, page):
            request(node["base"], f'/federation/local/queries/{page["sessionId"]}', "DELETE", expected=204)

        pair(a, b)
        pair(b, c)
        direct = query(a, [b["id"]])
        assert direct["totalWithinParticipants"] == 257, "B must not recursively export C"
        assert {item["ref"]["nodeId"] for item in direct["items"]} == {b["id"]}
        release(a, direct)
        no_transitive = request(a["base"], "/federation/local/queries", "POST",
                                {"nodeIds": [c["id"]], "query": {}}, expected=503)
        assert no_transitive["code"] == "PeerUnavailable"
        pair(a, c)
        pair(b, a)
        print("PASS: directed pairing, reverse pairing and no transitive export", flush=True)

        first = query(a, [node["id"] for node in nodes])
        assert first["coverageComplete"] and first["totalWithinParticipants"] == 771
        collected = list(first["items"])
        page = first
        replay_path = None
        while page["nextCursor"]:
            path = f'/federation/local/queries/{page["sessionId"]}/pages?cursor=' + urllib.parse.quote(page["nextCursor"])
            page = request(a["base"], path)
            replay = request(a["base"], path)
            assert replay["items"] == page["items"] and replay["nextCursor"] == page["nextCursor"]
            replay_path = path
            collected.extend(page["items"])
        assert len(collected) == 771
        refs = [(item["ref"]["nodeId"], item["ref"]["libraryEpoch"], item["ref"]["resourceId"]) for item in collected]
        assert len(set(refs)) == 771
        assert collected == sorted(collected, key=lambda item: (item["normalizedSortKey"], item["ref"]["nodeId"],
                                                                 item["ref"]["libraryEpoch"], item["ref"]["resourceId"]))
        assert sum(item["ref"]["resourceId"] == 1 for item in collected) == 3
        release(a, first)
        bad = request(a["base"], "/federation/local/queries", "POST",
                      {"nodeIds": [a["id"]], "query": {"unsupportedFilter": True}}, expected=422)
        assert bad["code"] == "UnsupportedQuery"
        print("PASS: 771 rows, identical local IDs, deterministic multi-block pagination and replay, strict filters", flush=True)

        remote_ref = next(item["ref"] for item in collected if item["ref"]["nodeId"] == b["id"] and item["ref"]["resourceId"] == 1)
        detail = request(a["base"], "/federation/local/resources/resolve", "POST", {"refs": [remote_ref]})["resources"][0]
        assert detail["displayName"] == "Shared title" and detail["assets"]
        asset = detail["assets"][0]
        assert "path" not in asset and "key" not in json.dumps(detail).lower()
        ticket = request(a["base"], "/federation/local/playback-sessions", "POST",
                         {"assetRef": {"resourceRef": remote_ref, "assetId": asset["assetId"]}, "mode": "preview"})
        media_path = urllib.parse.urlsplit(ticket["url"]).path
        status, headers, content = request(a["base"], media_path, headers={"Range": "bytes=0-63"}, expected=206, raw=True)
        assert len(content) == 64 and content[:4] == b"RIFF"
        _, head_headers, content = request(a["base"], media_path, method="HEAD", raw=True)
        assert not content and int(head_headers["Content-Length"]) == 16044
        _, invalid_headers, _ = request(a["base"], media_path, headers={"Range": "bytes=999999-"}, expected=416, raw=True)
        assert "Content-Range" in invalid_headers
        print("PASS: authenticated media proxy, HEAD, byte range, unsatisfiable range", flush=True)

        # Loopback and legacy Unrestricted are deliberately not node-export credentials.
        request(b["base"], "/remote-access/mode", "PUT", {"mode": 2})
        request(b["base"], "/federation/v1/export/mapping-roots", expected=401)
        request(b["base"], "/resource/search", "POST", {}, expected=403,
                headers={"Authorization": "Bakabase-Node malformed"})
        request(a["base"], "/federation/local/peers", expected=403, headers={"Origin": "https://evil.example"})
        request(a["base"], "/federation/local/peers", expected=403, headers={"Host": "evil.example"})
        print("PASS: new grants cannot enter legacy API; loopback/Unrestricted export and Host/Origin checks", flush=True)

        # An already cached page and media ticket must stop working when the source revokes its grant.
        active = query(a, [a["id"], b["id"]], 7)
        cursor_path = f'/federation/local/queries/{active["sessionId"]}/pages?cursor=' + urllib.parse.quote(active["nextCursor"])
        request(a["base"], cursor_path)
        source_status = request(b["base"], "/federation/local/peers")
        grant = next(peer["inboundGrant"]["grantId"] for peer in source_status["peers"] if peer["nodeId"] == a["id"])
        request(b["base"], f"/federation/local/peers/grants/{grant}", "DELETE")
        request(a["base"], cursor_path, expected=(401, 403, 409, 410, 502, 503))
        request(a["base"], media_path, expected=(401, 403, 409, 410))
        release(a, active)
        pair(a, b)
        # Old incarnation references never resolve to a new row with the same int ID.
        request(b["base"], "/federation/local/peers/identity/reset", "POST", {"asNewNode": False})
        request(a["base"], "/federation/local/resources/resolve", "POST", {"refs": [remote_ref]}, expected=(401, 403, 409))
        restored_status = request(b["base"], "/federation/local/peers")
        assert restored_status["identity"]["nodeId"] == b["id"]
        assert restored_status["identity"]["libraryEpoch"] != remote_ref["libraryEpoch"]
        assert not restored_status["sharingEnabled"] and not restored_status["browsingEnabled"]
        invite_denied = request(b["base"], "/federation/local/peers/invite", "POST", expected=403)
        assert invite_denied["code"] == "SharingDisabled"
        request(b["base"], "/federation/local/peers/sharing", "PUT", {"enabled": True})
        request(a["base"], "/federation/local/resources/resolve", "POST", {"refs": [remote_ref]}, expected=(401, 403, 409))
        pair(a, b)
        print("PASS: source revocation invalidates cached pages and streams; epoch rotation invalidates old references", flush=True)

        # Local browsing opt-out releases existing local sessions, but does not revoke
        # a separately granted peer's permission to read this node's shared library.
        request(b["base"], "/federation/local/peers/browsing", "PUT", {"enabled": True})
        pair(b, a)
        active = query(a, [a["id"]], 7)
        active_path = f'/federation/local/queries/{active["sessionId"]}/pages?cursor=' + urllib.parse.quote(active["nextCursor"])
        local_ref = next(item["ref"] for item in collected if item["ref"]["nodeId"] == a["id"] and item["ref"]["resourceId"] == 1)
        local_detail = request(a["base"], "/federation/local/resources/resolve", "POST", {"refs": [local_ref]})["resources"][0]
        local_ticket = request(a["base"], "/federation/local/playback-sessions", "POST",
                               {"assetRef": {"resourceRef": local_ref, "assetId": local_detail["assets"][0]["assetId"]}, "mode": "preview"})
        local_media = urllib.parse.urlsplit(local_ticket["url"]).path
        request(a["base"], "/federation/local/peers/browsing", "PUT", {"enabled": False})
        for path in (active_path, local_media):
            denied = request(a["base"], path, expected=403)
            assert denied["code"] == "BrowsingDisabled"
        still_shared = query(b, [a["id"]])
        assert still_shared["totalWithinParticipants"] == 257
        release(b, still_shared)
        request(a["base"], "/federation/local/peers/browsing", "PUT", {"enabled": True})
        request(a["base"], active_path, expected=410)
        request(a["base"], local_media, expected=(401, 403, 410))
        print("PASS: browsing is opt-in; opt-out releases local sessions without disabling source sharing", flush=True)

        processes[1].terminate()
        processes[1].wait(timeout=10)
        partial = query(a, [node["id"] for node in nodes])
        assert partial["totalWithinParticipants"] == 514 and not partial["coverageComplete"]
        assert any(node["nodeId"] == b["id"] for node in partial["omittedNodes"])
        release(a, partial)
        # Read operations above did not update source play history.
        database = next(b["directory"].rglob("bakabase_insideworld*.db"))
        with sqlite3.connect(database) as connection:
            assert connection.execute('select count(*) from ResourcesV2 where PlayedAt is not null').fetchone()[0] == 0
        print("PASS: offline source is explicitly omitted; other libraries work; remote PlayedAt stays unchanged", flush=True)
        report = {"passed": True, "resources": 771, "nodes": [{"url": n["base"], "nodeId": n["id"], "pid": n["pid"]} for n in nodes]}
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--timeout", type=int, default=300, help="Overall request/startup deadline in seconds")
    parser.add_argument("--results-directory", type=Path, help="Retain logs/result.json here; fixture databases are deleted")
    run(parser.parse_args())
