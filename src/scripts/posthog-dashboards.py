#!/usr/bin/env python3
"""Preview or upsert the repository-owned PostHog v2 dashboards. No ingestion key can administer a project."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urljoin, urlparse
from urllib.request import Request, HTTPRedirectHandler, build_opener

CATALOG = Path(__file__).resolve().parents[2] / "docs" / "analytics-dashboards.json"
TAG = "bakabase-analytics-v2"


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None  # Never forward a personal API key through a redirect.


class Client:
    def __init__(self, host: str, project: int, key: str):
        parsed = urlparse(host)
        if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password or parsed.path not in ("", "/") or parsed.query or parsed.fragment:
            raise ValueError("--host must be an HTTPS app origin, e.g. https://eu.posthog.com")
        self.base = host.rstrip("/") + f"/api/projects/{project}/"
        self.origin = host.rstrip("/")
        self.project = project
        self.key = key
        self.opener = build_opener(NoRedirect())

    def request(self, method: str, path: str, body=None):
        url = urljoin(self.base, path)
        if not url.startswith(self.base):
            raise ValueError("Refused an API link outside the selected project")
        request = Request(url, method=method,
                          headers={"Authorization": f"Bearer {self.key}", "Content-Type": "application/json"},
                          data=None if body is None else json.dumps(body).encode())
        try:
            with self.opener.open(request, timeout=30) as response:
                return json.load(response)
        except HTTPError as error:
            raise RuntimeError(f"PostHog {method} returned HTTP {error.code}; completed writes remain and can be resumed") from None
        except URLError:
            raise RuntimeError("PostHog connection failed; no automatic write retry was attempted") from None

    def list(self, path: str):
        items = []
        for _ in range(1000):
            page = self.request("GET", path)
            items.extend(page["results"])
            path = page.get("next")
            if not path:
                return items
        raise RuntimeError("Pagination limit exceeded")


def marker(kind: str, key: str) -> str:
    return f"[{TAG}:{kind}:{key}]"


def owned(items: list, kind: str, key: str):
    identity = marker(kind, key)
    found = [item for item in items if not item.get("deleted") and
             identity in (item.get("description") or "").splitlines() and TAG in (item.get("tags") or [])]
    if len(found) > 1:
        raise ValueError(f"More than one managed {kind} has key {key}; resolve the duplicates before applying")
    return found[0] if found else None


def validate_catalog(catalog: dict):
    """Fail before network access if an edit removes the production/v2 boundary."""
    if catalog.get("schema_version") != 2 or catalog.get("identity_version") != 2:
        raise ValueError("Expected schema and identity version 2")
    dashboard_keys = set()
    for dashboard in catalog["dashboards"]:
        key = dashboard["key"]
        if not re.fullmatch(r"[a-z][a-z0-9_]*", key) or key in dashboard_keys:
            raise ValueError(f"Invalid or duplicate dashboard key: {key}")
        dashboard_keys.add(key)
        insight_keys, boxes = set(), []
        for insight in dashboard["insights"]:
            ikey = insight["key"]
            if not re.fullmatch(r"[a-z][a-z0-9_]*", ikey) or ikey in insight_keys:
                raise ValueError(f"Invalid or duplicate insight key: {key}.{ikey}")
            insight_keys.add(ikey)
            if len(marker("insight", f"{key}.{ikey}") + "\n" + insight["description"]) > 400:
                raise ValueError(f"Insight description exceeds 400 characters: {key}.{ikey}")
            query = insight["query"].get("source", insight["query"])
            if query.get("kind") == "HogQLQuery":
                sql = query["query"]
                required = ("toFloat(properties.schema_version) = 2", "properties.environment = 'production'", "timestamp >= now() - INTERVAL")
                if not all(term in sql for term in required):
                    raise ValueError(f"SQL must bound time and production v2: {key}.{ikey}")
            elif query.get("kind") in ("TrendsQuery", "FunnelsQuery", "RetentionQuery"):
                filters = query.get("properties", [])
                for field, value in (("schema_version", 2), ("environment", "production")):
                    if not any(p.get("key") == field and p.get("value") == value and
                               p.get("type") == "event" and p.get("operator") == "exact" for p in filters):
                        raise ValueError(f"Query must filter production v2: {key}.{ikey}")
                if not query.get("dateRange", {}).get("date_from"):
                    raise ValueError(f"Query requires an explicit date range: {key}.{ikey}")
            else:
                raise ValueError(f"Unsupported query kind: {key}.{ikey}")
            box = insight["layout"]
            if set(box) != {"x", "y", "w", "h"} or any(type(n) is not int for n in box.values()) or not (
                box["x"] >= 0 and box["y"] >= 0 and box["w"] > 0 and box["h"] > 0 and box["x"] + box["w"] <= 12
            ):
                raise ValueError(f"Invalid layout: {key}.{ikey}")
            for other in boxes:
                if (box["x"] < other["x"] + other["w"] and other["x"] < box["x"] + box["w"] and
                    box["y"] < other["y"] + other["h"] and other["y"] < box["y"] + box["h"]):
                    raise ValueError(f"Overlapping layouts: {key}.{ikey}")
            boxes.append(box)


def validate_queries(client: Client, catalog: dict):
    for dashboard in catalog["dashboards"]:
        for insight in dashboard["insights"]:
            query = insight["query"].get("source", insight["query"])
            response = client.request("POST", "query/", {"query": query, "refresh": "force_blocking"})
            status = response.get("query_status") or {}
            if response.get("error") or status.get("error") or status.get("complete") is False or "results" not in response:
                raise RuntimeError(f"Query validation did not complete successfully: {dashboard['key']}.{insight['key']}")


def memberships(insight: dict):
    """Support both legacy dashboards and the current dashboard_tiles response."""
    ids = list(insight.get("dashboards") or [])
    for tile in insight.get("dashboard_tiles") or []:
        value = tile.get("dashboard_id", tile.get("dashboard"))
        if isinstance(value, dict):
            value = value.get("id")
        if isinstance(value, int):
            ids.append(value)
    return list(dict.fromkeys(ids))


def set_layout(client: Client, dashboard_id: int, expected: list):
    actual = client.request("GET", f"dashboards/{dashboard_id}/")
    tiles = actual.get("tiles", [])
    managed = {item["id"] for item in expected}
    # Leave manually added tiles in place and place our block after them.
    offset = 0
    for tile in tiles:
        if (tile.get("insight") or {}).get("id") not in managed:
            box = tile.get("layouts", {}).get("sm")
            if not box:
                raise ValueError("Cannot safely place managed tiles beside an unpositioned manual tile")
            offset = max(offset, box["y"] + box["h"])
    updates = []
    for item in expected:
        matches = [t for t in tiles if (t.get("insight") or {}).get("id") == item["id"]]
        if len(matches) != 1:
            raise ValueError(f"Expected one dashboard tile for managed insight {item['id']}")
        box = dict(item["layout"])
        box["y"] += offset
        updates.append({"id": matches[0]["id"], "layouts": {"sm": box}, "show_description": True})
    client.request("PATCH", f"dashboards/{dashboard_id}/", {"tiles": updates})


def apply(client: Client, catalog: dict):
    validate_catalog(catalog)
    dashboards = client.list("dashboards/")
    insights = client.list("insights/?include_dashboards=true")
    result = []
    for dashboard in catalog["dashboards"]:
        key = dashboard["key"]
        previous = owned(dashboards, "dashboard", key)
        body = {"name": dashboard["name"], "description": marker("dashboard", key) + "\n" + dashboard["description"], "tags": [TAG]}
        if previous:
            body["tags"] = list(dict.fromkeys(previous.get("tags", []) + [TAG]))
            saved = client.request("PATCH", f"dashboards/{previous['id']}/", body)
        else:
            saved = client.request("POST", "dashboards/", body)
            dashboards.append(saved)
        dashboard_id = saved["id"]
        expected = []
        for insight in dashboard["insights"]:
            insight_key = f"{key}.{insight['key']}"
            previous = owned(insights, "insight", insight_key)
            body = {"name": insight["name"], "description": marker("insight", insight_key) + "\n" + insight["description"],
                    "query": insight["query"], "tags": [TAG], "dashboards": [dashboard_id], "saved": True}
            if previous:
                body["tags"] = list(dict.fromkeys(previous.get("tags", []) + [TAG]))
                body["dashboards"] = list(dict.fromkeys(memberships(previous) + [dashboard_id]))
                saved_insight = client.request("PATCH", f"insights/{previous['id']}/", body)
            else:
                saved_insight = client.request("POST", "insights/", body)
                insights.append(saved_insight)
            expected.append({"id": saved_insight["id"], "layout": insight["layout"]})
        set_layout(client, dashboard_id, expected)
        result.append({"name": dashboard["name"], "id": dashboard_id,
                       "url": f"{client.origin}/project/{client.project}/dashboard/{dashboard_id}"})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="https://eu.posthog.com", help="App host, not the ingestion host")
    parser.add_argument("--project", type=int)
    parser.add_argument("--apply", action="store_true", help="Write using POSTHOG_PERSONAL_API_KEY; without this flag only preview")
    parser.add_argument("--validate", action="store_true", help="Execute each query against the selected project before writing")
    args = parser.parse_args()
    catalog = json.loads(CATALOG.read_text())
    validate_catalog(catalog)
    if not args.apply and not args.validate:
        print(json.dumps(catalog, ensure_ascii=False, indent=2))
        return
    key = os.environ.get("POSTHOG_PERSONAL_API_KEY")
    if not args.project or args.project <= 0 or not key:
        parser.error("--project and POSTHOG_PERSONAL_API_KEY are required for project access")
    client = Client(args.host, args.project, key)
    if args.validate or args.apply:
        validate_queries(client, catalog)
        print("All dashboard queries executed successfully.")
    if args.apply:
        print(json.dumps(apply(client, catalog), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
