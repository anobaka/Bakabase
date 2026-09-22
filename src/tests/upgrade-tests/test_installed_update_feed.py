#!/usr/bin/env python3
"""Verify real loopback feed isolation and refusal of unverified packages."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import time
import unittest
import urllib.error
import urllib.request

SPEC = importlib.util.spec_from_file_location("feed", Path(__file__).with_name("installed-update-feed.py"))
feed = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(feed)


class InstalledFeedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.old, self.new = "0.0.1-acceptance.12.1", "0.0.2-updater.34.1"
        self.manifest = {"passed": True, "rid": "osx-arm64", "oldVersion": self.old,
                         "newVersion": self.new, "sourceSHA": "a" * 40, "roles": {}}
        for role, identity in feed.ROLES.items():
            entry = {"role": role, "channel": "osx", "payloadUnchanged": True,
                     "marker": {"role": role, "sourceSHA": "a" * 40, "oldVersion": self.old,
                                "newVersion": self.new, "nonce": "b" * 32}, "packageChecksums": {}}
            for kind, version in (("old", self.old), ("new", self.new)):
                path = self.root / f"{identity}-{version}-full.nupkg"
                payload = (role + kind).encode() * 100
                path.write_bytes(payload)
                entry[kind + "FullPackage"] = str(path)
                entry[kind + "Manifest"] = {"id": identity, "rid": "osx-arm64", "version": version}
                entry["packageChecksums"][kind + "Full"] = {
                    "path": str(path), "fileName": path.name, "sizeBytes": len(payload),
                    "sha1": hashlib.sha1(payload).hexdigest(), "sha256": hashlib.sha256(payload).hexdigest()}
            self.manifest["roles"][role] = entry
        self.path = self.root / "updates.json"
        self.path.write_text(json.dumps(self.manifest))
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def start(self):
        server = feed.Feed(self.path, "osx-arm64", self.old)
        self.addCleanup(server.close)
        return server

    def get(self, url):
        with self.opener.open(url, timeout=3) as response:
            return response.read()

    def test_activation_is_per_product_and_download_matches_recorded_bytes(self):
        server = self.start()
        unified = server.environment["BAKABASE_UPDATE_URL"] + "/osx-arm64/"
        client = server.environment["BAKABASE_CLIENT_UPDATE_URL"] + "/osx-arm64/"
        self.assertEqual(json.loads(self.get(unified + "releases.osx.json"))["Assets"][0]["Version"], self.old)
        server.activate("unified")
        asset = json.loads(self.get(unified + "releases.osx.json?local-cache-buster=1"))["Assets"][0]
        self.assertEqual(asset["Version"], self.new)
        self.assertEqual(json.loads(self.get(client + "releases.osx.json"))["Assets"][0]["Version"], self.old)
        payload = self.get(unified + asset["FileName"])
        self.assertEqual(hashlib.sha256(payload).hexdigest().upper(), asset["SHA256"])
        self.assertEqual(hashlib.sha1(payload).hexdigest().upper(), asset["SHA1"])
        deadline = time.monotonic() + 2
        while not server.deliveries[-1]["completed"] and time.monotonic() < deadline:
            time.sleep(0.01)
        self.assertEqual(server.deliveries, [{"role": "unified", "kind": "new", "file": asset["FileName"],
                                            "bytes": len(payload), "completed": True}])
        server.deactivate("unified")
        self.assertEqual(json.loads(self.get(unified + "releases.osx.json"))["Assets"][0]["Version"], self.old)
        with self.assertRaises(ValueError):
            server.deactivate("unified")
        server.activate("unified")
        with self.assertRaises(ValueError):
            server.activate("unified")

    def test_feed_refuses_cross_product_unpublished_and_arbitrary_paths(self):
        server = self.start()
        url = server.environment["BAKABASE_UPDATE_URL"] + "/osx-arm64/"
        unknown = (Path(self.manifest["roles"]["client"]["oldFullPackage"]).name,
                   Path(self.manifest["roles"]["unified"]["newFullPackage"]).name,
                   "releases.win.json", "../../../updates.json", "%2e%2e/updates.json")
        for path in unknown:
            with self.subTest(path=path), self.assertRaises(urllib.error.HTTPError) as caught:
                self.get(url + path)
            self.assertEqual(caught.exception.code, 404)
        self.assertEqual(server.deliveries, [])

    def test_corrupted_or_replaced_package_cannot_start_server(self):
        path = Path(self.manifest["roles"]["client"]["newFullPackage"])
        path.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "size mismatch"):
            self.start()

    def test_role_version_rid_and_payload_provenance_are_mandatory(self):
        for key, value in (("passed", False), ("rid", "win-x64"), ("oldVersion", "wrong"),
                           ("newVersion", "1.0.0"), ("sourceSHA", "short")):
            bad = dict(self.manifest, **{key: value})
            with self.subTest(key=key), self.assertRaises(ValueError):
                feed.validate_manifest(bad, "osx-arm64", self.old)
        for change in (lambda e: e.update(payloadUnchanged=False),
                       lambda e: e["newManifest"].update(id="Bakabase.Client"),
                       lambda e: e["marker"].update(role="client"),
                       lambda e: e.update(channel="acceptance")):
            bad = copy.deepcopy(self.manifest)
            change(bad["roles"]["unified"])
            with self.assertRaises(ValueError):
                feed.validate_manifest(bad, "osx-arm64", self.old)

    def test_digest_and_filename_contract_is_not_just_size(self):
        entry = copy.deepcopy(self.manifest["roles"]["unified"])
        for field in ("sha1", "sha256", "fileName", "path"):
            bad = copy.deepcopy(entry)
            bad["packageChecksums"]["oldFull"][field] = "incorrect"
            with self.subTest(field=field), self.assertRaises(ValueError):
                feed.validate_package(bad, "old")


if __name__ == "__main__":
    unittest.main()
