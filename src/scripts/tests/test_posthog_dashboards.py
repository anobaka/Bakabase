"""Run with python3 -m unittest discover -s src/scripts/tests -p 'test_posthog*.py'."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import re
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("posthog_dashboards", Path(__file__).parents[1] / "posthog-dashboards.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class FakeClient:
    origin = "https://eu.posthog.com"
    project = 123

    def __init__(self):
        self.dashboards = [{"id": 1, "name": "Legacy", "description": "", "tags": [], "tiles": []}]
        self.insights = [{"id": 2, "name": "Legacy", "description": "", "tags": [], "dashboards": [1]}]
        self.next_id = 10
        self.writes = []

    def list(self, path):
        return copy.deepcopy(self.dashboards if path.startswith("dashboards/") else self.insights)

    def request(self, method, path, body=None):
        if path == "query/":
            return {"results": []}
        collection = self.dashboards if path.startswith("dashboards/") else self.insights
        if method == "GET":
            return copy.deepcopy(next(x for x in collection if x["id"] == int(path.split("/")[1])))
        self.writes.append((method, path, copy.deepcopy(body)))
        if method == "POST":
            item = {"id": self.next_id, **copy.deepcopy(body)}
            self.next_id += 1
            if collection is self.dashboards:
                item["tiles"] = []
            collection.append(item)
        else:
            item = next(x for x in collection if x["id"] == int(path.split("/")[1]))
            for key, value in body.items():
                if key == "tiles":
                    for update in value:
                        next(t for t in item["tiles"] if t["id"] == update["id"]).update(copy.deepcopy(update))
                else:
                    item[key] = copy.deepcopy(value)
        if collection is self.insights:
            for did in item.get("dashboards", []):
                dashboard = next(d for d in self.dashboards if d["id"] == did)
                if not any(t.get("insight", {}).get("id") == item["id"] for t in dashboard["tiles"]):
                    dashboard["tiles"].append({"id": self.next_id, "insight": copy.deepcopy(item), "layouts": {}})
                    self.next_id += 1
        return copy.deepcopy(item)


class DashboardTests(unittest.TestCase):
    def setUp(self):
        self.catalog = json.loads(module.CATALOG.read_text())

    def test_catalog_has_six_production_dashboards_and_24_valid_insights(self):
        module.validate_catalog(self.catalog)
        self.assertEqual(6, len(self.catalog["dashboards"]))
        self.assertEqual(24, sum(len(d["insights"]) for d in self.catalog["dashboards"]))
        serialized = json.dumps(self.catalog)
        self.assertNotIn("phc_", serialized)
        self.assertNotIn("phx_", serialized)

    def test_default_is_offline_preview(self):
        with patch.object(module, "Client", side_effect=AssertionError("network access")), patch("sys.argv", ["posthog-dashboards.py"]):
            with contextlib.redirect_stdout(io.StringIO()) as out:
                module.main()
        self.assertEqual(self.catalog, json.loads(out.getvalue()))

    def test_query_property_names_exist_in_current_capture_contracts(self):
        root = Path(__file__).resolve().parents[2]
        contract_paths = [
            'apps/Bakabase.Service/Services/Telemetry/TelemetryEnvironment.cs',
            'apps/Bakabase.Service/Services/Telemetry/TelemetryEventPolicy.cs',
            'apps/Bakabase.Service/Services/Telemetry/ProductTelemetry.cs',
            'web/src/services/analyticsContext.ts',
            'web/src/services/Analytics.ts',
            'apps/mobile/app/lib/core/analytics.dart',
        ]
        contracts = '\n'.join((root / p).read_text() for p in contract_paths)
        # A static spelling guard: server sanitization/common properties, plus client
        # contracts. Semantics and provider-specific availability still need review.
        declared = set(re.findall(r'[\"\']([a-z_$][a-z_0-9$]*)[\"\']', contracts))
        declared.update(re.findall(r'^\s+([a-z_][a-z_0-9]*):', contracts, re.M))
        referenced = set()

        def visit(value):
            if isinstance(value, dict):
                if value.get('type') == 'event':
                    referenced.update(value[k] for k in ('key', 'property') if k in value)
                if 'math_property' in value:
                    referenced.add(value['math_property'])
                for child in value.values():
                    visit(child)
            elif isinstance(value, list):
                for child in value:
                    visit(child)
            elif isinstance(value, str):
                referenced.update(re.findall(r'properties\.([a-z_$][a-z_0-9$]*)', value))

        for dashboard in self.catalog['dashboards']:
            for insight in dashboard['insights']:
                visit(insight['query'])
        self.assertEqual(set(), referenced - declared)

    def test_upsert_twice_preserves_legacy_and_does_not_duplicate(self):
        client = FakeClient()
        first = module.apply(client, self.catalog)
        second = module.apply(client, self.catalog)
        self.assertEqual(first, second)
        self.assertEqual(7, len(client.dashboards))
        self.assertEqual(25, len(client.insights))
        self.assertEqual("Legacy", client.dashboards[0]["name"])
        self.assertFalse(any(path in ("dashboards/1/", "insights/2/") for _, path, _ in client.writes))

    def test_tag_and_exact_marker_are_both_required(self):
        item = {"id": 1, "description": module.marker("dashboard", "runtime"), "tags": []}
        self.assertIsNone(module.owned([item], "dashboard", "runtime"))
        item["tags"] = [module.TAG]
        self.assertEqual(item, module.owned([item], "dashboard", "runtime"))
        item["description"] += " copied example"
        self.assertIsNone(module.owned([item], "dashboard", "runtime"))

    def test_duplicate_ownership_fails(self):
        item = {"description": module.marker("insight", "a.b"), "tags": [module.TAG]}
        with self.assertRaises(ValueError):
            module.owned([item, dict(item)], "insight", "a.b")

    def test_upsert_preserves_other_dashboard_membership_and_tags(self):
        client = FakeClient()
        module.apply(client, self.catalog)
        item = client.insights[1]
        item["dashboards"].append(1)
        item["tags"].append("user-tag")
        module.apply(client, self.catalog)
        self.assertIn(1, item["dashboards"])
        self.assertIn("user-tag", item["tags"])
        self.assertEqual([3, 4, 5], module.memberships({"dashboard_tiles": [{"dashboard_id": 3}, {"dashboard": 4}, {"dashboard": {"id": 5}}]}))

    def test_manual_tile_is_not_moved_or_overlapped(self):
        client = FakeClient()
        module.apply(client, self.catalog)
        dashboard = client.dashboards[1]
        manual = {"id": 999, "insight": None, "layouts": {"sm": {"x": 0, "y": 0, "w": 12, "h": 7}}}
        dashboard["tiles"].append(copy.deepcopy(manual))
        module.apply(client, self.catalog)
        self.assertEqual(manual, dashboard["tiles"][-1])
        self.assertTrue(all(t["layouts"]["sm"]["y"] >= 7 for t in dashboard["tiles"][:-1]))

    def test_incomplete_query_is_not_reported_as_validated(self):
        with self.assertRaises(RuntimeError):
            module.validate_queries(type("Pending", (), {"request": lambda *args: {"query_status": {"complete": False}}})(), self.catalog)

    def test_scope_or_layout_regression_fails_before_any_write(self):
        client = FakeClient()
        self.catalog["dashboards"][0]["insights"][0]["query"]["source"]["properties"] = []
        with self.assertRaises(ValueError):
            module.apply(client, self.catalog)
        self.assertEqual([], client.writes)
        self.setUp()
        self.catalog["dashboards"][0]["insights"][1]["layout"]["x"] = 0
        with self.assertRaises(ValueError):
            module.validate_catalog(self.catalog)

    def test_no_credential_follows_redirect_or_foreign_pagination_link(self):
        client = module.Client("https://eu.posthog.com", 123, "secret")
        with self.assertRaises(ValueError):
            client.request("GET", "https://other.example/api/projects/123/")
        with self.assertRaises(ValueError):
            client.request("GET", "../456/insights/")
        self.assertIsNone(module.NoRedirect().redirect_request(None, None, 302, None, None, "https://other.example"))
        for host in ["http://posthog.example", "https://user:pass@posthog.example", "https://posthog.example/path"]:
            with self.assertRaises(ValueError):
                module.Client(host, 123, "secret")


if __name__ == "__main__":
    unittest.main()
