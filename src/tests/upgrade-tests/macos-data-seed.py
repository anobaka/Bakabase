#!/usr/bin/env python3
"""API-created original v349 data; never writes SQLite or launches a product.

DTOs and enum values checked at historical-release.RELEASE_SHA:
86d76392b1f715fa08d952a81d96053ec363e294 (ResourceController,
CustomPropertyController, CollectionController, ResourceMaterializeResultViewModel,
ResourceAdditionalItem, ResourceProperty, PropertyPool and PropertyValueScope).
"""
import hashlib
import http.client
import importlib.util
import json
import math
import os
from pathlib import Path
import time
import urllib.parse

SPEC = importlib.util.spec_from_file_location("macos_seed_historical", Path(__file__).with_name("historical-release.py"))
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
require = release.require
MAX_REQUEST_BYTES = 64 * 1024
MAX_RESPONSE_BYTES = 2 * 1024 * 1024
REQUEST_TIMEOUT = 5
MAX_CALLS = 48
NAME = "旧版保留：子文件 Ω"
INTRODUCTION = "旧版手写介绍\n第二行：不应丢失。"
CUSTOM_NAME = "旧版自定义文字"
CUSTOM_VALUE = "中文值 Ω / 旧版 349"
COLLECTION_NAME = "旧版收藏集合"
COLLECTION_DESCRIPTION = "固定顺序与忽略状态"
FILE_BYTES = "Bakabase 原版数据保留夹具\nfixed source bytes 349\n".encode("utf-8")
REQUIRED_COUNTS = {"ResourcesV2": 3, "ReservedPropertyValues": 3, "CustomProperties": 1,
                   "CustomPropertyValues": 1, "PlayHistories": 1, "Collections": 1,
                   "CollectionResourceMappings": 2}


class Api:
    """Numeric loopback only; no proxy lookup, redirects or request retries."""
    def __init__(self, app, evidence):
        require(type(app.get("port")) is int and 0 < app["port"] < 65536, "Invalid fixture loopback port")
        self.port, self.evidence = app["port"], evidence
        self.deadline = time.monotonic() + 120
        self.calls = 0

    def call(self, method, path, payload=None):
        require(method in ("GET", "POST", "PUT"), "Unsupported fixture request method")
        require(isinstance(path, str) and path.startswith("/") and not path.startswith("//")
                and not any(ord(c) < 32 for c in path) and "#" not in path
                and not urllib.parse.urlsplit(path).netloc, "Invalid fixture API path")
        self.calls += 1
        require(self.calls <= MAX_CALLS, "Fixture API request count exceeded")
        remaining = min(REQUEST_TIMEOUT, self.deadline - time.monotonic())
        require(remaining > 0, "Fixture API deadline exceeded")
        body = None if payload is None else json.dumps(payload, ensure_ascii=False, allow_nan=False).encode("utf-8")
        require(body is None or len(body) <= MAX_REQUEST_BYTES, "Fixture request body exceeded bound")
        entry = {"method": method, "path": path, "request": payload}
        self.evidence.append(entry)
        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=remaining)
        end = time.monotonic() + remaining
        try:
            connection.request(method, path, body=body, headers={"Content-Type": "application/json", "Connection": "close"})
            response = connection.getresponse()
            entry["httpStatus"] = response.status
            require(response.status == 200, "Fixture API HTTP status was not 200")
            declared = response.getheader("Content-Length")
            require(declared is None or (declared.isdecimal() and int(declared) <= MAX_RESPONSE_BYTES),
                    "Fixture response body exceeded bound")
            chunks, size = [], 0
            while True:
                remaining = end - time.monotonic()
                require(remaining > 0, "Fixture API response deadline exceeded")
                if connection.sock is not None:
                    connection.sock.settimeout(remaining)
                chunk = response.read1(min(65536, MAX_RESPONSE_BYTES + 1 - size))
                if not chunk:
                    break
                chunks.append(chunk)
                size += len(chunk)
                require(size <= MAX_RESPONSE_BYTES, "Fixture response body exceeded bound")
            def reject_constant(_):
                raise ValueError("Non-finite fixture JSON")
            value = json.loads(b"".join(chunks), parse_constant=reject_constant)
            entry["response"] = value
            require(isinstance(value, dict) and type(value.get("code")) is int and value["code"] == 0,
                    "Fixture API returned an unsuccessful envelope")
            return value.get("data")
        finally:
            connection.close()


def _positive_id(value):
    require(type(value) is int and value > 0, "Expected a positive original resource identity")
    return value


def _guard(app):
    require(app.get("rid") in ("osx-x64", "osx-arm64"), "Data seed is macOS-only")
    release.hosted(app["rid"])


def _prepare_source(root):
    root = Path(root)
    require(root.is_absolute() and root == root.resolve() and not root.exists(),
            "Source media directory must be new, absolute and without symlinks")
    runner = Path(os.environ["RUNNER_TEMP"]).resolve()
    require(root.is_relative_to(runner) and root != runner, "Source media must be inside the owned runner temporary directory")
    folder = root / "旧版 媒体库"
    folder.mkdir(parents=True)
    path = folder / "样本 一.txt"
    with path.open("xb") as stream:
        stream.write(FILE_BYTES)
    return folder, path


def _property(resource, pool, property_id):
    properties = resource.get("properties")
    require(isinstance(properties, dict), "Resource properties are missing")
    prop = properties.get(str(pool), {}).get(str(property_id))
    require(isinstance(prop, dict) and isinstance(prop.get("values"), list), "Expected seeded property is missing")
    manual = [v for v in prop["values"] if v.get("scope") == 0]
    require(len(manual) == 1 and manual[0].get("bizValue") is not None, "Manual property value is absent or ambiguous")
    return manual[0]["bizValue"]


def _semantics(api, seed):
    ids = seed["resourceIds"]
    require(len(ids) == 3 and len(set(ids)) == 3 and all(type(i) is int and i > 0 for i in ids), "Invalid seed resource inventory")
    path = "/resource/keys?" + urllib.parse.urlencode([("ids", i) for i in ids] + [("additionalItems", 288)])
    resources = api.call("GET", path)
    require(isinstance(resources, list) and len(resources) == 3 and
            {r.get("id") for r in resources} == set(ids), "API lost or duplicated seeded resources")
    by_id = {r["id"]: r for r in resources}
    parent, child, placeholder = (by_id[i] for i in ids)
    require(parent.get("path") == seed["directoryPath"] and parent.get("isFile") is False,
            "Original directory resource changed")
    require(child.get("path") == seed["mediaFiles"][0]["path"] and child.get("isFile") is True
            and child.get("parentId") == ids[0], "Original file path or parent relationship changed")
    require(placeholder.get("path") is None and placeholder.get("hasLocalPath") is False,
            "Pathless resource was materialized")
    require(child.get("pinned") is True and isinstance(child.get("playedAt"), str)
            and child["playedAt"] and not child["playedAt"].startswith("0001-"), "Pin or play history is absent")
    # v349 only applies reserved Name to DisplayName through a name template.
    # This fixture has no ResourceProfile, so the materialized file displays
    # FileName while its independent manual Name retains the Unicode value.
    require(child.get("displayName") == Path(seed["mediaFiles"][0]["path"]).name
            and _property(child, 2, 27) == NAME
            and _property(child, 2, 12) == INTRODUCTION, "Manual name or introduction changed")
    rating = _property(child, 2, 13)
    require(type(rating) in (float, int) and math.isfinite(rating) and rating == 4.5, "Manual rating changed")
    require(_property(child, 4, seed["customPropertyId"]) == CUSTOM_VALUE, "Custom Unicode value changed")
    definitions = api.call("GET", "/custom-property/ids?ids=" + str(seed["customPropertyId"]))
    require(isinstance(definitions, list) and len(definitions) == 1 and
            definitions[0].get("id") == seed["customPropertyId"] and definitions[0].get("name") == CUSTOM_NAME
            and definitions[0].get("type") == 1, "Custom property definition changed")
    collection = api.call("GET", "/collection/" + str(seed["collectionId"]))
    require(isinstance(collection, dict) and collection.get("id") == seed["collectionId"]
            and collection.get("name") == COLLECTION_NAME and collection.get("description") == COLLECTION_DESCRIPTION
            and collection.get("color") == "#345678" and collection.get("autoAcquire") is False,
            "Collection definition changed")
    members = api.call("GET", f"/collection/{seed['collectionId']}/memberships")
    require(isinstance(members, list) and len(members) == 2 and
            {m.get("resourceId") for m in members} == {ids[1], ids[2]}, "Collection membership changed")
    normalized_members = sorted(({k: m.get(k) for k in ("collectionId", "resourceId", "origin", "order", "isIgnored")}
                                 for m in members), key=lambda m: m["resourceId"])
    expected = [{"collectionId": seed["collectionId"], "resourceId": i, "origin": 1,
                 "order": order, "isIgnored": i == ids[2]} for order, i in enumerate((ids[2], ids[1]))]
    require(normalized_members == sorted(expected, key=lambda m: m["resourceId"]),
            "Collection order, manual origin or ignored state changed")
    normalized = {"resources": [{k: r.get(k) for k in ("id", "path", "isFile", "parentId", "displayName", "pinned", "playedAt")}
                                for r in (parent, child, placeholder)],
                  "childProperties": {"name": NAME, "introduction": INTRODUCTION, "rating": rating, "customText": CUSTOM_VALUE},
                  "customProperty": {k: definitions[0][k] for k in ("id", "name", "type")},
                  "collection": {k: collection.get(k) for k in ("id", "name", "description", "color", "autoAcquire")},
                  "members": normalized_members}
    if "baselineSemantics" in seed:
        require(normalized == seed["baselineSemantics"], "Seeded API meaning differs from the old-version baseline")
    return normalized


def verify_semantics(app, seed):
    """Return stable meaning only; keep the original read responses in seed evidence."""
    _guard(app)
    evidence = []
    seed.setdefault("verificationApiEvidence", []).append(evidence)
    result = _semantics(Api(app, evidence), seed)
    source_root = Path(seed["sourceRoot"])
    require(source_root.is_absolute() and source_root == source_root.resolve(), "Source media root changed")
    for media in seed["mediaFiles"]:
        path = Path(media["path"])
        require(path == path.resolve() and path.is_relative_to(source_root) and path.is_file()
                and type(media["sizeBytes"]) is int and 0 < media["sizeBytes"] <= 4096
                and path.stat().st_size == media["sizeBytes"], "Original source media changed")
        with path.open("rb") as stream:
            data = stream.read(4097)
        require(len(data) == media["sizeBytes"] and hashlib.sha256(data).hexdigest() == media["sha256"],
                "Original source media changed")
    return result


def seed(apps, report, source_root):
    require(set(apps) == {"unified", "client"}, "Both original product roles are required")
    for app in apps.values():
        _guard(app)
    result = {"passed": False, "oldVersion": release.OLD_VERSION, "oldSourceSHA": release.RELEASE_SHA,
              "apiEvidence": [], "requiredTables": list(REQUIRED_COUNTS), "minimumTableRows": dict(REQUIRED_COUNTS),
              "coverage": ["directory and child path", "pathless resource", "manual name/introduction/rating",
                           "custom Unicode text", "pin", "played timestamp/history", "collection membership/order/ignored",
                           "source-file SHA256"]}
    report["macosDataSeed"] = result
    apis = {role: Api(app, result["apiEvidence"]) for role, app in apps.items()}
    for role, api in apis.items():
        info = api.call("GET", "/app/info" if role == "unified" else "/client/app/info")
        require(isinstance(info, dict) and info.get("coreVersion" if role == "unified" else "version") == release.OLD_VERSION,
                "Only the original published version may create the data seed")
        require(Path(info["appDataPath" if role == "unified" else "dataDirectory"]).resolve() == apps[role]["data"].resolve(),
                "Original product used an unexpected data directory")
    api = apis["unified"]
    api.call("POST", "/app/terms", {})
    folder, media = _prepare_source(source_root)
    result.update(sourceRoot=str(folder.parent), directoryPath=str(folder), mediaFiles=[{"path": str(media), "sizeBytes": len(FILE_BYTES),
                                                        "sha256": hashlib.sha256(FILE_BYTES).hexdigest()}])
    created = api.call("POST", "/resource/placeholder", {"items": [{"title": t} for t in
                       ("旧版父目录", "旧版子文件初始名称", "旧版无路径资源")], "acquireImmediately": False})
    require(isinstance(created, list) and len(created) == 3 and
            all(isinstance(v, dict) and v.get("created") is True and not v.get("error") for v in created),
            "The old program did not create all three original resources")
    ids = [_positive_id(v.get("resourceId")) for v in created]
    require(len(set(ids)) == 3, "Original resource IDs are not unique")
    result["resourceIds"] = ids
    for resource_id, path in ((ids[0], folder), (ids[1], media)):
        materialized = api.call("POST", f"/resource/{resource_id}/materialize", {"path": str(path), "mergeIfOccupied": False})
        require(isinstance(materialized, dict) and materialized.get("materialized") is True
                and materialized.get("merged") is False and materialized.get("path") == str(path),
                "The old program did not materialize the exact unmerged resource")
    # v349 StandardValueExtensions.DeserializeAsStandardValue returns String
    # unchanged and parses Decimal from invariant text; neither uses JSON here.
    for property_id, value in ((27, NAME), (12, INTRODUCTION), (13, "4.5")):
        api.call("PUT", f"/resource/{ids[1]}/property-value", {"propertyId": property_id, "isCustomProperty": False,
                 "value": value, "isBizValue": False})
    prop = api.call("POST", "/custom-property", {"name": CUSTOM_NAME, "type": 1, "options": None})
    require(isinstance(prop, dict), "Original custom property was not returned")
    result["customPropertyId"] = _positive_id(prop.get("id"))
    api.call("PUT", f"/resource/{ids[1]}/property-value", {"propertyId": result["customPropertyId"], "isCustomProperty": True,
             "value": CUSTOM_VALUE, "isBizValue": False})
    api.call("PUT", f"/resource/{ids[1]}/pin?pin=true")
    api.call("POST", f"/resource/{ids[1]}/played-at")
    collection = api.call("POST", "/collection", {"name": COLLECTION_NAME, "description": COLLECTION_DESCRIPTION,
                          "color": "#345678", "autoAcquire": False, "order": 3})
    require(isinstance(collection, dict), "Original collection was not returned")
    result["collectionId"] = _positive_id(collection.get("id"))
    api.call("POST", f"/collection/{result['collectionId']}/members", {"resourceIds": ids[1:]})
    api.call("PUT", f"/collection/{result['collectionId']}/members/order", {"resourceIds": [ids[2], ids[1]]})
    api.call("PUT", f"/collection/{result['collectionId']}/members/{ids[2]}/ignored?ignored=true")
    result["baselineSemantics"] = _semantics(api, result)
    result["passed"] = True
    return result
