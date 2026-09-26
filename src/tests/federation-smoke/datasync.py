#!/usr/bin/env python3
"""Data sync across three production Service hosts (data sync spec §13.9).

A and B are desktop apps, C is a headless server that turns definitions sharing on through
BAKABASE_DATASYNC_SHARING. Every step goes through each host's own loopback API — `/data-sync/*`
as its window would, `/federation/local/*` for the identity reset — and C's approvals through
its headless CLI (`dotnet Bakabase.Service.dll federation datasync …`), exactly as `docker exec`
runs it. `run.py` calls `run()` after its own checks; this file also runs on its own.

Build Bakabase.Federation.TestHost first. Only temporary fixture directories are used.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

# Enum values as the API writes them (src/web/src/sdk/constants.ts).
TWO_WAY = 2
ACTIVE, AWAITING_ACCESS, AWAITING_REVIEW, WAITING_FOR_PEER_REVIEW, PAUSED, ACCESS_REVOKED = 1, 2, 3, 4, 5, 9
PEER_RESET = 3
FIELD_CONFLICT, CHILD_DELETED_IN_USE = 1, 5
KEEP_LOCAL = 1
RESOLVED_ELSEWHERE = 2
PLAN_CREATE, PLAN_LINK = 1, 4
RESOLUTION_LINK = 3
RESUME = 1
INCOMING = 1
SINGLE_CHOICE, NUMBER, MULTILEVEL, TAGS, SINGLE_LINE_TEXT = 3, 5, 15, 16, 1
REVIEW_APPLIED = 3

SHARED = ("Artist", "Genre", "Mood", "Studio", "Series", "Label")
COLOURS = ("Red", "Green", "Blue")
TAG_COUNT = 2000
DATA_SYNC_SOURCE = "DataSync"


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


class Smoke:
    def __init__(self, dotnet, deadline, results):
        self.dotnet = dotnet
        self.deadline = deadline
        self.results = results
        self.repo = Path(__file__).resolve().parents[3]
        self.bin = self.repo / "src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0"
        self.root = Path(tempfile.mkdtemp(prefix="bakabase-datasync-smoke-"))
        self.nodes = {}
        self.cli_log = []

    # ---- hosts ---------------------------------------------------------------------------------

    def start(self, label, name, desktop, extra=None):
        """Starts (or restarts, on the same port and data directory) one TestHost."""
        node = self.nodes.get(label) or {"label": label, "port": free_port(), "name": name,
                                         "directory": self.root / label, "desktop": desktop, "starts": 0}
        self.nodes[label] = node
        node["base"] = f"http://127.0.0.1:{node['port']}"
        env = dict(os.environ)
        for variable in ("BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW", "BAKABASE_DATASYNC_SHARING",
                         "BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA", "BAKABASE_FEDERATION_TEST_NODE_NAME",
                         "BAKABASE_FEDERATION_TEST_SERVER_NAME", "BAKABASE_FEDERATION_TEST_WEB_ROOT",
                         "BAKABASE_FEDERATION_TEST_LAN_PORT"):
            env.pop(variable, None)
        env["BAKABASE_NODE_NAME"] = name
        if desktop:
            env["BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW"] = node["base"]
        env.update(extra or {})
        node["extra"] = dict(extra or {})
        node["starts"] += 1
        # A restart must not read the last start's signal; the host deletes it too, but only once it runs.
        (node["directory"] / "ready").unlink(missing_ok=True)
        log = (self.root / f"{label}-{node['starts']}.log").open("w")
        node["log"] = log
        node["process"] = subprocess.Popen(
            [self.dotnet, str(self.bin / "Bakabase.Federation.TestHost.dll"), str(node["port"]),
             str(node["directory"]), "3"],
            cwd=self.repo, stdout=log, stderr=subprocess.STDOUT, env=env)
        return node

    def wait_ready(self, *labels):
        end = min(time.monotonic() + 90, self.deadline)
        while not all((self.nodes[label]["directory"] / "ready").exists() for label in labels):
            exited = [label for label in labels if self.nodes[label]["process"].poll() is not None]
            if exited:
                raise AssertionError(f"Host {exited} exited during startup; see its log in {self.results}")
            if time.monotonic() > end:
                raise AssertionError(f"Startup of {labels} did not finish")
            time.sleep(0.2)
        for label in labels:
            node = self.nodes[label]
            node["id"] = self.api(node, "/federation/local/peers")["identity"]["nodeId"]

    def stop(self, label):
        node = self.nodes[label]
        process = node["process"]
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        node["log"].close()

    def restart(self, label, extra=None):
        self.stop(label)
        node = self.nodes[label]
        # The port was ours a moment ago; wait until the old listener has let it go. Connections it leaves in
        # TIME_WAIT do not count: Kestrel binds with SO_REUSEADDR, as this probe does.
        end = time.monotonic() + 15
        while True:
            try:
                with socket.socket() as probe:
                    probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                    probe.bind(("127.0.0.1", node["port"]))
                break
            except OSError:
                if time.monotonic() > end:
                    raise
                time.sleep(0.2)
        self.start(label, node["name"], node["desktop"], extra)
        self.wait_ready(label)

    def close(self, keep):
        for label in list(self.nodes):
            if not keep:
                self.stop(label)
            elif not self.nodes[label]["log"].closed:
                self.nodes[label]["log"].flush()
        for log in self.root.glob("*.log"):
            shutil.copyfile(log, self.results / log.name)
        (self.results / "cli.txt").write_text("\n".join(self.cli_log))
        if not keep:
            shutil.rmtree(self.root, ignore_errors=True)

    # ---- HTTP ------------------------------------------------------------------------------------

    def raw(self, node, path, method="GET", body=None, expected=200):
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("Data sync smoke deadline exceeded")
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(node["base"] + path, data=data, method=method,
                                         headers={"Content-Type": "application/json"})
        try:
            response = urllib.request.urlopen(request, timeout=min(30, remaining))
        except urllib.error.HTTPError as error:
            response = error
        with response:
            content = response.read()
            if response.status != expected:
                raise AssertionError(f"{node['label']}: {method} {path}: HTTP {response.status}, "
                                     f"expected {expected}: {content[:1000]!r}")
            return json.loads(content) if content else None

    def api(self, node, path, method="GET", body=None):
        """The `data` of an answer whose response code is 0; `/federation/local` answers are returned whole."""
        answer = self.raw(node, path, method, body)
        if isinstance(answer, dict) and isinstance(answer.get("code"), int):
            assert answer["code"] == 0, f"{node['label']}: {method} {path}: {str(answer)[:2000]}"
            return answer.get("data")
        return answer

    def ds(self, node, path, method="GET", body=None, check=True):
        """A `/data-sync` call, which must answer no problem unless `check` is off."""
        data = self.api(node, "/data-sync/" + path, method, body)
        problem = data if isinstance(data, dict) and set(data) == {"code", "detail"} else \
            data.get("problem") if isinstance(data, dict) else None
        assert not check or problem is None, f"{node['label']}: {method} /data-sync/{path}: problem {problem}"
        return data

    def cli(self, node, *arguments, expected=0):
        """The headless CLI, as `docker exec <c> dotnet Bakabase.Service.dll federation datasync …` runs it."""
        command = [self.dotnet, str(self.bin / "Bakabase.Service.dll"), "federation", "datasync", *arguments,
                   "--port", str(node["port"])]
        # In the host's own environment, as `docker exec` runs it inside the container.
        result = subprocess.run(command, capture_output=True, text=True, timeout=60, cwd=self.repo,
                                env={**os.environ, **node["extra"]})
        self.cli_log.append(f"$ federation datasync {' '.join(arguments)}   # {node['label']}, "
                            f"exit {result.returncode}\n{result.stdout}{result.stderr}")
        assert result.returncode == expected, \
            f"CLI {arguments} on {node['label']}: exit {result.returncode}\n{result.stdout}\n{result.stderr}"
        return result.stdout

    # ---- reading ---------------------------------------------------------------------------------

    def properties(self, node):
        return {p["name"]: p for p in self.api(node, "/custom-property/all")}

    @staticmethod
    def labels(prop):
        options = prop.get("options") or {}
        if prop["type"] == TAGS:
            return sorted(f"{t.get('group') or ''}/{t['name']}" for t in options.get("tags") or [])
        if prop["type"] == MULTILEVEL:
            found = []

            def walk(nodes, prefix):
                for item in nodes or []:
                    found.append(prefix + item["label"])
                    walk(item.get("children"), prefix + item["label"] + "/")
            walk(options.get("data"), "")
            return sorted(found)
        return sorted(c["label"] for c in options.get("choices") or [])

    def shape(self, node):
        """Every definition as a peer sees it: names, types and option labels."""
        return {name: (prop["type"], tuple(self.labels(prop))) for name, prop in self.properties(node).items()}

    def link(self, node, peer):
        return next((link for link in self.ds(node, "links") if link["peerNodeId"] == peer["id"]), None)

    def inbox(self, node, open_only=True):
        return self.ds(node, f"inbox?openOnly={'true' if open_only else 'false'}&take=500")

    def sync_now(self, node, peer):
        link = self.link(node, peer)
        assert link, f"{node['label']} has no link to {peer['label']}"
        # A nudge: a fetch already running answers Busy, and the next nudge tries again.
        return self.ds(node, "sync-now", "POST", {"linkId": link["id"]}, check=False)

    def wait(self, description, check, nudge=None, bound=60, every=5):
        """Polls `check` until it answers something truthy; `nudge` (a `sync_now`) is pressed every `every` s."""
        end = min(time.monotonic() + bound, self.deadline)
        nudged = 0.0
        while True:
            answer = check()
            if answer:
                return answer
            now = time.monotonic()
            if now > end:
                raise AssertionError(f"Timed out waiting for {description}")
            if nudge and now - nudged >= every:
                nudge()
                nudged = now
            time.sleep(0.5)

    def evidence(self, name):
        for label, node in self.nodes.items():
            if node["process"].poll() is not None:
                continue
            try:
                snapshot = {
                    "overview": self.ds(node, "overview"),
                    "links": self.ds(node, "links"),
                    "requests": self.ds(node, "requests"),
                    "readers": self.ds(node, "readers"),
                    "inbox": self.inbox(node, open_only=False),
                    "history": self.ds(node, "history"),
                }
            except Exception as error:  # evidence must never hide the failure that asked for it
                snapshot = {"error": repr(error)}
            (self.results / f"{name}-{label}.json").write_text(json.dumps(snapshot, indent=2, ensure_ascii=False))


# ---- fixture data -------------------------------------------------------------------------------

def choice(name, labels):
    return {"name": name, "type": SINGLE_CHOICE,
            "options": json.dumps({"Choices": [{"Value": str(uuid.uuid4()), "Label": label} for label in labels]})}


def seed_a():
    properties = [
        choice("Artist", COLOURS),
        choice("Genre", ("Horror", "Comedy", "Drama")),
        choice("Mood", ("Calm", "Tense")),
        *(choice(name, COLOURS) for name in SHARED[3:]),
        {"name": "Big tags", "type": TAGS, "options": json.dumps({"Tags": [
            {"Group": f"Group {i % 40}", "Name": f"Tag {i:04d}", "Value": f"00000000-0000-4000-9000-{i:012d}"}
            for i in range(TAG_COUNT)]})},
        {"name": "Places", "type": MULTILEVEL, "options": json.dumps({"Data": [
            {"Value": f"00000000-0000-4000-a000-{i:012d}", "Label": f"Region {i}", "Color": "#336699",
             "Children": [{"Value": f"00000000-0000-4000-a000-{i:06d}{j:06d}", "Label": f"City {i}.{j}",
                           "Color": "#336699"} for j in range(1, 4)]}
            for i in range(1, 6)]})},
    ]
    for i in range(1, 100 - len(properties) + 1):
        if i % 3 == 0:
            properties.append(choice(f"Prop {i:02d}", ("One", "Two")))
        else:
            properties.append({"name": f"Prop {i:02d}", "type": NUMBER if i % 3 == 1 else SINGLE_LINE_TEXT})
    assert len(properties) == 100
    return properties


def seed_b():
    # Same names and types as A's first six, each with an option A does not have.
    own = {"Genre": ("Horror", "Comedy", "Drama"), "Mood": ("Calm", "Tense")}
    return [choice(name, (*own.get(name, COLOURS), f"B extra {i + 1}")) for i, name in enumerate(SHARED)]


def put_property(smoke, node, prop, name=None, labels=None, drop=None):
    """Writes a custom property as its own UI does: the whole property, options included."""
    options = dict(prop.get("options") or {})
    choices = [c for c in options.get("choices") or [] if c["label"] != drop]
    if labels:
        choices = choices + [{"value": str(uuid.uuid4()), "label": label} for label in labels]
    options["choices"] = choices
    smoke.api(node, f"/custom-property/{prop['id']}", "PUT",
              {"name": name or prop["name"], "type": prop["type"], "options": json.dumps(options)})


# ---- the stage ------------------------------------------------------------------------------------

def run(dotnet, deadline, results, keep=False):
    results = Path(results)
    results.mkdir(parents=True, exist_ok=True)
    smoke = Smoke(dotnet, deadline, results)
    started = time.monotonic()
    print(f"Data sync fixture directory: {smoke.root}; retained results: {results}", flush=True)
    passed = False
    try:
        steps(smoke)  # each step leaves its evidence: link views, requests, readers, inbox and history
        passed = True
    finally:
        try:
            if not passed:
                smoke.evidence("failure")
        finally:
            smoke.close(keep)
    report = {"passed": True, "seconds": round(time.monotonic() - started, 1),
              "nodes": {label: {"nodeId": node["id"], "name": node["name"], "desktop": node["desktop"]}
                        for label, node in smoke.nodes.items()}}
    (results / "result.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)


def steps(smoke):
    a = smoke.start("a", "smoke-a", desktop=True)
    b = smoke.start("b", "smoke-b", desktop=True)
    c = smoke.start("c", "smoke-nas", desktop=False, extra={"BAKABASE_DATASYNC_SHARING": "true"})
    smoke.wait_ready("a", "b", "c")
    for number, step, nodes in ((0, step0_hosts, (a, b, c)), (1, step1_seed, (a, b)),
                                (2, step2_a_links_with_c, (a, c)), (3, step3_b_links_with_c, (a, b, c)),
                                (4, step4_concurrent_rename, (a, b, c)), (5, step5_in_use_option, (a, b, c)),
                                (6, step6_epoch_change, (a, c)), (7, step7_newer_schema, (b, c)),
                                (8, step8_headless_silence, (a, b, c))):
        started = time.monotonic()
        passed = step(smoke, *nodes)
        print(f"PASS: data sync {number}: {passed} ({time.monotonic() - started:.1f} s)", flush=True)
        smoke.evidence(f"step{number}")


def step0_hosts(smoke, a, b, c):
    for node in (a, b):
        overview = smoke.ds(node, "overview")
        assert overview["isHeadless"] is False and overview["sharingEnabled"] is False, overview
    # C is headless, and BAKABASE_DATASYNC_SHARING turned its sharing on with nobody at a window.
    overview = smoke.wait("C's sharing turned on by BAKABASE_DATASYNC_SHARING",
                          lambda: (o := smoke.ds(c, "overview"))["sharingEnabled"] and o, bound=20)
    assert overview["isHeadless"] is True and overview["deviceName"] == "smoke-nas", overview
    status = smoke.cli(c, "status")
    assert "headless" in status and "Definitions sharing: on" in status, status
    # Off lasts only until the next start while the variable is set.
    off = smoke.cli(c, "share", "off")
    assert "BAKABASE_DATASYNC_SHARING is set" in off, off
    assert smoke.ds(c, "overview")["sharingEnabled"] is False
    smoke.restart("c", {"BAKABASE_DATASYNC_SHARING": "true"})
    smoke.wait("C's sharing turned on again at its next start",
               lambda: smoke.ds(c, "overview")["sharingEnabled"], bound=20)
    for node in (a, b):
        smoke.ds(node, "sharing", "PUT", {"enabled": True, "enablePairedRemoteAccess": True})
        assert smoke.ds(node, "overview")["sharingEnabled"] is True
    return ("desktops and a headless server; BAKABASE_DATASYNC_SHARING turns sharing on at every start, "
            "the CLI turns it off until then")


def step1_seed(smoke, a, b):
    smoke.api(a, "/custom-property/batch", "POST", seed_a())
    for i in range(1, 5):
        smoke.api(a, "/extension-group", "POST", {"name": f"Group {i}", "extensions": [f".g{i}a", f".g{i}b"]})
    smoke.api(b, "/custom-property/batch", "POST", seed_b())
    shape = smoke.shape(a)
    assert len(shape) == 100 and len(shape["Big tags"][1]) == TAG_COUNT, len(shape)
    assert len(smoke.shape(b)) == 6
    return ("A holds 100 properties (a 2,000-tag property, a multilevel one) and 4 extension groups; "
            "B holds 6 of the same names")


def approve_on_cli(smoke, source, requester):
    """The headless server's owner approves a two-way request from its own machine."""
    listed = smoke.wait(f"{requester['label']}'s request on {source['label']}",
                        lambda: [r for r in smoke.ds(source, "requests")
                                 if r["direction"] == INCOMING and r["nodeId"] == requester["id"]
                                 and r["status"] == "awaitingApproval"], bound=20)
    request_id = listed[0]["requestId"]
    printed = smoke.cli(source, "requests")
    assert f"{request_id}: from {requester['name']}" in printed and "keep in step both ways" in printed, printed
    approved = smoke.cli(source, "approve", request_id)
    assert "Approved." in approved and f"now also receives from {requester['name']}" in approved, approved
    return request_id


def review_and_apply(smoke, node, peer, expected_counts=None, link_exact=False):
    link = smoke.wait(f"{node['label']}'s review of {peer['label']}",
                      lambda: (found := smoke.link(node, peer)) and found["state"] == AWAITING_REVIEW
                      and found["reviewId"] and found,
                      nudge=lambda: smoke.sync_now(node, peer))
    review = smoke.ds(node, f"reviews/{link['reviewId']}")
    plan = review["plan"]
    counts = {(c["kind"], c["type"]): c["count"] for c in plan["summary"]["counts"]}
    if expected_counts is not None:
        assert counts == expected_counts, counts
    decisions = []
    if link_exact:
        assert plan["summary"]["bulkLinkEligibleCount"] == counts.get(("customProperty", PLAN_LINK), 0), plan["summary"]
        for kind in plan["kinds"]:
            for item in kind["items"]:
                if item["bulkLinkEligible"]:
                    candidate = next(c for c in item["candidates"] if c["localKey"] == item["defaultTargetLocalKey"])
                    decisions.append({"itemId": item["itemId"], "resolution": RESOLUTION_LINK,
                                      "targetLocalKey": item["defaultTargetLocalKey"], "newName": None,
                                      "excludedChangeIds": [], "reviewToken": candidate["reviewToken"]})
    started = smoke.ds(node, f"reviews/{link['reviewId']}/apply", "POST",
                       {"decisions": decisions, "backupBeforeDestructive": False})
    assert started["taskId"], started
    smoke.wait(f"{node['label']}'s review of {peer['label']} applied",
               lambda: smoke.ds(node, f"reviews/{link['reviewId']}")["state"] == REVIEW_APPLIED
               and smoke.link(node, peer)["state"] == ACTIVE)
    return counts


def step2_a_links_with_c(smoke, a, c):
    created = smoke.ds(a, "links", "POST", {"address": c["base"], "mode": TWO_WAY, "kinds": []})
    assert created["link"]["state"] == AWAITING_ACCESS and created["requestId"], created
    assert created["link"]["peerNodeId"] == c["id"]
    approve_on_cli(smoke, c, a)
    # C read A back through A's reciprocal offer, and waits for A's first review (§8.3).
    c_link = smoke.link(c, a)
    assert c_link["state"] == WAITING_FOR_PEER_REVIEW and c_link["mode"] == TWO_WAY, c_link
    # A's review is empty: C has nothing yet. Applying it completes the counterpart.
    assert review_and_apply(smoke, a, c) == {}
    smoke.wait("C's first pull from A: 100 properties and 4 extension groups",
               lambda: (found := smoke.link(c, a))["state"] == ACTIVE and found["lastSyncedAt"]
               and len(smoke.properties(c)) == 100 and len(smoke.api(c, "/extension-group")) == 4,
               nudge=lambda: smoke.sync_now(c, a))
    assert smoke.shape(c) == smoke.shape(a), "C must hold exactly A's definitions"
    readers = smoke.ds(c, "readers")
    assert [r["nodeId"] for r in readers] == [a["id"]], readers
    return ("A asks C for a two-way link; C approves on its CLI and reads A back; A's empty review; "
            "C's first pull creates all 100 properties")


def step3_b_links_with_c(smoke, a, b, c):
    created = smoke.ds(b, "links", "POST", {"address": c["base"], "mode": TWO_WAY, "kinds": []})
    assert created["link"]["state"] == AWAITING_ACCESS, created
    approve_on_cli(smoke, c, b)
    counts = review_and_apply(smoke, b, c, link_exact=True)
    assert counts == {("customProperty", PLAN_CREATE): 94, ("customProperty", PLAN_LINK): 6,
                      ("extensionGroup", PLAN_CREATE): 4}, counts
    # C pulls B's extra options, and A pulls them from C.
    extra = {f"B extra {i + 1}" for i in range(len(SHARED))}

    def has_extras(node):
        shape = smoke.shape(node)
        return {label for name in SHARED for label in shape[name][1]} >= extra

    smoke.wait("C has B's extra options", lambda: has_extras(c), nudge=lambda: smoke.sync_now(c, b))
    smoke.wait("A has B's extra options through C", lambda: has_extras(a), nudge=lambda: smoke.sync_now(a, c))
    smoke.wait("B in step with C", lambda: smoke.shape(b) == smoke.shape(c), nudge=lambda: smoke.sync_now(b, c))
    assert smoke.shape(a) == smoke.shape(b) == smoke.shape(c)
    assert len(smoke.shape(a)) == 100
    return ("B links two-way with C (94 created, 6 linked as exact matches); B's extra options reach C "
            "and A; all three hold equal definitions")


def open_items(smoke, node, item_type=None):
    items = smoke.inbox(node)["items"]
    return [i for i in items if item_type is None or i["type"] == item_type]


def data_sync_notifications(smoke, node, *peers):
    found = []
    for source in (DATA_SYNC_SOURCE, *(f"{DATA_SYNC_SOURCE}:{peer['id']}" for peer in peers)):
        answer = smoke.raw(node, "/notification?" + urllib.parse.urlencode({"source": source, "pageSize": 100}))
        found += answer["data"] or []
    return found


def step4_concurrent_rename(smoke, a, b, c):
    for node in (a, b):
        smoke.ds(node, "paused", "PUT", {"paused": True})
    assert "Every link is paused." in smoke.cli(c, "pause")
    assert smoke.ds(c, "overview")["allPaused"] is True
    put_property(smoke, a, smoke.properties(a)["Artist"], name="Artists")
    put_property(smoke, b, smoke.properties(b)["Artist"], name="作者")
    # C takes A's rename first, then meets B's: the order matters, so C's link to B waits meanwhile.
    assert "Paused." in smoke.cli(c, "pause", b["id"])
    assert "Links resumed." in smoke.cli(c, "resume")
    smoke.wait("C has A's rename", lambda: "Artists" in smoke.properties(c), nudge=lambda: smoke.sync_now(c, a))
    assert smoke.link(c, b)["state"] == PAUSED, "C's link to B waited"
    assert "Resumed." in smoke.cli(c, "resume", b["id"])
    conflicts = smoke.wait("C's conflict between the two renames",
                           lambda: open_items(smoke, c, FIELD_CONFLICT), nudge=lambda: smoke.sync_now(c, b))
    assert len(conflicts) == 1 and len(open_items(smoke, c)) == 1, smoke.inbox(c)
    assert "Artists" in smoke.properties(c), "a conflict changes nothing until someone decides"
    assert not data_sync_notifications(smoke, c, a, b), "a headless server creates no notifications"
    # A sees that C waits for a decision.
    smoke.ds(a, "paused", "PUT", {"paused": False})
    smoke.wait("A sees C's open decision",
               lambda: ((smoke.link(a, c) or {}).get("peerAttention") or {}).get("openDecisions") == 1,
               nudge=lambda: smoke.sync_now(a, c))
    status = smoke.cli(c, "status")
    assert "1 change(s) need a decision here" in status, status
    # B meets the same conflict and keeps its own name.
    smoke.ds(b, "paused", "PUT", {"paused": False})
    item = smoke.wait("B's conflict", lambda: open_items(smoke, b, FIELD_CONFLICT),
                      nudge=lambda: smoke.sync_now(b, c))[0]
    assert KEEP_LOCAL in item["allowedActions"], item
    started = smoke.ds(b, "inbox/resolve", "POST", {"items": [{
        "itemId": item["id"], "action": KEEP_LOCAL, "token": item["token"], "customValue": None,
        "targetLocalKey": None, "targetRecordKey": None, "newName": None}], "backupBeforeDestructive": False})
    assert started["taskId"], started
    smoke.wait("B resolved its conflict", lambda: not open_items(smoke, b))
    assert "作者" in smoke.properties(b)
    # C takes B's decision: its own item closes as decided elsewhere, by B.
    smoke.wait("C's item closed by B's decision", lambda: not open_items(smoke, c),
               nudge=lambda: smoke.sync_now(c, b))
    closed = [i for i in smoke.inbox(c, open_only=False)["items"] if i["id"] == conflicts[0]["id"]][0]
    assert closed["closure"] == RESOLVED_ELSEWHERE and closed["closedByName"] == "smoke-b", closed
    assert "作者" in smoke.properties(c)
    smoke.wait("A has B's name", lambda: "作者" in smoke.properties(a), nudge=lambda: smoke.sync_now(a, c))
    smoke.wait("A sees nothing waiting on C",
               lambda: ((smoke.link(a, c) or {}).get("peerAttention") or {}).get("openDecisions") == 0,
               nudge=lambda: smoke.sync_now(a, c))
    for node in (a, b, c):
        assert not open_items(smoke, node), (node["label"], smoke.inbox(node))
    assert smoke.shape(a) == smoke.shape(b) == smoke.shape(c)
    return ("a concurrent rename reaches C's inbox with no notification there; A sees it waiting on C; "
            "B decides, which closes C's item and reaches A")


def step5_in_use_option(smoke, a, b, c):
    genre = smoke.properties(b)["Genre"]
    horror = next(o["value"] for o in genre["options"]["choices"] if o["label"] == "Horror")
    smoke.api(b, "/resource/1/property-value", "PUT",
              {"propertyId": genre["id"], "isCustomProperty": True, "value": horror, "isBizValue": False})
    put_property(smoke, a, smoke.properties(a)["Genre"], drop="Horror")
    has_horror = lambda node: "Horror" in smoke.shape(node)["Genre"][1]  # noqa: E731
    smoke.wait("C removes the unused option by itself", lambda: not has_horror(c),
               nudge=lambda: smoke.sync_now(c, a))
    assert not open_items(smoke, c), smoke.inbox(c)
    items = smoke.wait("B holds the option it uses", lambda: open_items(smoke, b, CHILD_DELETED_IN_USE),
                       nudge=lambda: smoke.sync_now(b, c))
    assert len(items) == 1 and items[0]["payload"]["usageCount"] == 1, items
    assert has_horror(b) and not has_horror(a) and not has_horror(c)
    return ("an option deleted on A leaves C, where nothing used it, and waits on B, where a resource "
            "uses it")


def step6_epoch_change(smoke, a, c):
    before = smoke.shape(c)
    old_epoch = smoke.api(a, "/federation/local/peers")["identity"]["libraryEpoch"]
    smoke.api(a, "/federation/local/peers/identity/reset", "POST", {"asNewNode": False})
    identity = smoke.api(a, "/federation/local/peers")["identity"]
    assert identity["nodeId"] == a["id"] and identity["libraryEpoch"] != old_epoch
    # A reset turns definitions sharing off (§7.1.4); A turns it back on, and edits something.
    assert smoke.ds(a, "overview")["sharingEnabled"] is False
    smoke.ds(a, "sharing", "PUT", {"enabled": True})
    info = smoke.raw(a, "/federation/v1/info")
    assert info["libraryEpoch"] == identity["libraryEpoch"], info
    put_property(smoke, a, smoke.properties(a)["Prop 06"], labels=["After the reset"])
    seen = set()

    def reset_seen():
        link = smoke.link(c, a)
        seen.add((link["state"], link["lastErrorCode"]))
        return link["state"] == PAUSED and link["pausedReason"] == PEER_RESET and link

    # C's session to A may have been verified in the last minute (PeerSessionFactory reuses one for a minute). The
    # grant A's reset revoked is then refused on it, and C verifies the session again, whose info shows the new epoch:
    # the reset reads as one at once, never as a revoked grant first.
    link = smoke.wait("C pauses its link to the reset A", reset_seen, nudge=lambda: smoke.sync_now(c, a))
    assert smoke.shape(c) == before, "nothing is applied from a peer that was reset"
    revoked = [entry for entry in seen if entry[0] == ACCESS_REVOKED or entry[1] == "AccessRevoked"]
    assert not revoked, f"C read A's reset as a revoked grant on the way: {sorted(seen, key=str)}"
    assert link["lastErrorCode"] is None, link
    return "A's identity reset (new epoch in its /info) pauses C's link (PeerReset); A's edit after it is not applied"


def step7_newer_schema(smoke, b, c):
    smoke.restart("b", {"BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA": "Mood"})
    mood_before = smoke.shape(c)["Mood"]
    properties = smoke.properties(b)
    put_property(smoke, b, properties["Mood"], labels=["Joyful"])
    put_property(smoke, b, properties["Prop 03"], labels=["Three"])
    smoke.wait("C takes B's other edit", lambda: "Three" in smoke.shape(c)["Prop 03"][1],
               nudge=lambda: smoke.sync_now(c, b))
    link = smoke.wait("C holds B's newer-schema record",
                      lambda: (found := smoke.link(c, b))["heldCount"] == 1 and found,
                      nudge=lambda: smoke.sync_now(c, b))
    assert smoke.shape(c)["Mood"] == mood_before, "a record of a newer schema is held, never half-applied"
    assert link["state"] == ACTIVE, link
    return "a record of a newer schema is held on C while the rest of B's pull applies"


def step8_headless_silence(smoke, a, b, c):
    assert not data_sync_notifications(smoke, c, a, b), "a headless server creates no notifications"
    # The desktops did announce data sync, so the sources above are the ones in use.
    assert data_sync_notifications(smoke, a, c) and data_sync_notifications(smoke, b, c)
    # B's held option stayed on B: nothing brought it back to the others.
    assert all("Horror" not in smoke.shape(node)["Genre"][1] for node in (a, c))
    # What waits where, as the CLI tells the headless server's owner: B still has its held option to decide on.
    status = smoke.cli(c, "status")
    assert re.search(r"smoke-a \(\w+\): TwoWay, Paused \(PeerReset\)", status), status
    assert re.search(r"smoke-b \(\w+\): TwoWay, Active.*\n    1 wait for a decision there", status), status
    return "the headless server announced nothing, the desktops did; the CLI shows what waits on each peer"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--keep", action="store_true", help="Leave the hosts and their data for manual checks")
    parser.add_argument("--timeout", type=int, default=300, help="Overall deadline in seconds")
    parser.add_argument("--results-directory", type=Path, help="Retain logs, evidence and result.json here")
    options = parser.parse_args()
    run(options.dotnet, time.monotonic() + options.timeout,
        options.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-datasync-results-")), options.keep)
