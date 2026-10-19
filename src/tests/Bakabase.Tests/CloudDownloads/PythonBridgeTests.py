"""Offline protocol fixtures. No account, browser, live share, or downloads required."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[4] / "src/apps/Bakabase.Service/Components/CloudDownloads/WebProviders/CloudPythonBridge.py"
spec = importlib.util.spec_from_file_location("bridge", SOURCE)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


class FakeExtractor:
    api_token = "private-fixture-token"

    def __init__(self, pages):
        self.pages = pages
        self.requests = []

    def _generate_website_token(self, lang):
        return "fixture-website-token"

    def request_api(self, endpoint, params, headers):
        self.requests.append((params, headers))
        return self.pages[int(params["page"]) - 1]


class BridgeTests(unittest.TestCase):
    def test_paginates_more_than_1000_entries(self):
        pages = [{"type": "folder", "childrenCount": 1001, "children": {str(i): {} for i in range(1000)}},
                 {"type": "folder", "childrenCount": 1001, "children": {"1000": {}}}]
        extractor = FakeExtractor(pages)
        result = bridge.gofile_content(extractor, "folder", "secret")
        self.assertEqual(1001, len(result["children"]))
        self.assertEqual(["1", "2"], [r[0]["page"] for r in extractor.requests])
        self.assertEqual(hashlib.sha256(b"secret").hexdigest(), extractor.requests[1][0]["password"])

    def test_detects_incomplete_or_repeated_page(self):
        for second in ({}, {"same": {}}):
            with self.subTest(second=second):
                extractor = FakeExtractor([{"type": "folder", "childrenCount": 2, "children": {"same": {}}},
                                           {"type": "folder", "childrenCount": 2, "children": second}])
                with self.assertRaises(bridge.BridgeError):
                    bridge.gofile_content(extractor, "folder", None)

    def test_does_not_claim_complete_when_count_missing_or_changes(self):
        for pages in ([{"type": "folder", "children": {}}],
                      [{"type": "folder", "childrenCount": 2, "children": {"one": {}}},
                       {"type": "folder", "childrenCount": 3, "children": {"two": {}}}]):
            with self.subTest(pages=pages), self.assertRaises(bridge.BridgeError):
                bridge.gofile_content(FakeExtractor(pages), "folder", None)

    def test_rejects_unsafe_names_and_urls(self):
        for path in ("../x", "/absolute", "C:/x", "folder/../x", "folder/CON", "x\x00y"):
            with self.subTest(path=path), self.assertRaises(bridge.BridgeError):
                bridge.relative_path(path)
        for provider, url in (("gofile", "https://gofile.io.attacker.test/d/x"),
                              ("gofile", "https://gofile.io/other"),
                              ("googleDrive", "http://drive.google.com/file/d/x/view"),
                              ("googleDrive", "https://drive.google.com/"),
                              ("googleDrive", "https://drive.google.com/open")):
            with self.subTest(url=url), self.assertRaises(bridge.BridgeError):
                bridge.validate_url(provider, url)

    def test_receipts_resume_without_overwriting_and_detect_changed_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output, state = root / "output", root / "state"
            request = {"provider": "googleDrive", "url": "https://drive.google.com/file/d/fixture/view",
                       "output": str(output), "state": str(state)}
            calls = []
            def download(**options):
                calls.append(options)
                Path(options["output"]).write_bytes(b"complete")
                options["progress"](8, 8)
                return options["output"]
            class Session:
                pass
            modules = {"requests": types.SimpleNamespace(Session=Session), "gdown": types.SimpleNamespace(download=download)}
            entries = [{"id": "fixture", "path": "folder/file.zip", "size": None}]
            with patch.dict(sys.modules, modules), patch.object(bridge, "google_manifest", return_value=(entries, None)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()) as stdout:
                bridge.run(request)
                bridge.run(request)
                self.assertEqual(1, len(calls))
                messages = [json.loads(line) for line in stdout.getvalue().splitlines()]
                self.assertEqual(2, sum(m["type"] == "result" for m in messages))
                self.assertEqual(b"complete", (output / "folder/file.zip").read_bytes())
                (output / "folder/file.zip").write_bytes(b"user-edit")
                with self.assertRaises(bridge.BridgeError):
                    bridge.run(request)
                self.assertEqual(b"user-edit", (output / "folder/file.zip").read_bytes())

    def test_empty_directory_returns_an_explicit_unsupported_error(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            class Session:
                pass
            with patch.dict(sys.modules, {"requests": types.SimpleNamespace(Session=Session)}), patch.object(bridge, "google_manifest", return_value=([], None)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()):
                with self.assertRaisesRegex(bridge.BridgeError, "empty directories are not supported"):
                    bridge.run({"provider": "googleDrive", "url": "https://drive.google.com/drive/folders/fixture",
                                "output": str(root / "output"), "state": str(root / "state")})

    def test_proxy_is_explicit_and_percent_encodes_credentials_without_netrc(self):
        session = types.SimpleNamespace(trust_env=True, proxies={})
        bridge.configure_session(session, {"url": "socks5://127.0.0.1:1080", "username": "WORK\\alice@example.test", "password": "p:a@ss"})
        self.assertFalse(session.trust_env)
        expected = "socks5://WORK%5Calice%40example.test:p%3Aa%40ss@127.0.0.1:1080"
        self.assertEqual({"http": expected, "https": expected}, session.proxies)
        direct = types.SimpleNamespace(trust_env=True, proxies={})
        bridge.configure_session(direct, None)
        self.assertFalse(direct.trust_env)
        self.assertEqual({}, direct.proxies)

    def test_truncated_gofile_body_is_not_published_and_can_retry(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            request = {"provider": "gofile", "url": "https://gofile.io/d/fixture",
                       "output": str(root / "output"), "state": str(root / "state")}
            entries = [{"id": "fixture", "path": "folder/file.zip", "size": 5,
                        "url": "https://store1.gofile.io/file.zip", "md5": None}]
            class Response:
                is_redirect = False
                body = b"abc"
                def __enter__(self): return self
                def __exit__(self, *args): pass
                def raise_for_status(self): pass
                def iter_content(self, size): yield self.body
            response = Response()
            session = types.SimpleNamespace(get=lambda *args, **kwargs: response)
            class Session:
                pass
            with patch.dict(sys.modules, {"requests": types.SimpleNamespace(Session=Session)}), patch.object(bridge, "gofile_manifest", return_value=(entries, session)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()):
                with self.assertRaises(bridge.BridgeError):
                    bridge.run(request)
                self.assertFalse((root / "output/folder/file.zip").exists())
                response.body = b"abcde"
                bridge.run(request)
                self.assertEqual(b"abcde", (root / "output/folder/file.zip").read_bytes())

    @unittest.skipIf(sys.platform == "win32", "Windows symlink creation requires developer mode")
    def test_symlink_destination_is_rejected_before_network_or_writes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            outside = root / "outside"
            outside.mkdir()
            output = root / "output"
            output.symlink_to(outside, target_is_directory=True)
            with self.assertRaises(bridge.BridgeError):
                bridge.run({"provider": "googleDrive", "url": "https://drive.google.com/file/d/fixture/view",
                            "output": str(output), "state": str(root / "state")})
            self.assertEqual([], list(outside.iterdir()))

    def test_pinned_gallery_library_contract_without_network(self):
        try:
            import gallery_dl
            from gallery_dl.extractor.gofile import GofileFolderExtractor
        except ImportError:
            self.skipTest("Optional gallery-dl runtime is not installed in this interpreter")
        fixture = {"type": "folder", "id": "folder", "name": "Root", "childrenCount": 1,
                   "children": {"file": {"type": "file", "id": "file", "name": "file.txt", "size": 3,
                                         "link": "https://store1.gofile.io/download/web/file/file.txt"}}}
        with patch.object(GofileFolderExtractor, "_create_account", return_value="fixture-token"), patch.object(GofileFolderExtractor, "request_api", return_value=fixture):
            entries, session = bridge.gofile_manifest({"url": "https://gofile.io/d/fixture"})
            self.assertEqual("Root/file.txt", entries[0]["path"])
            self.assertFalse(session.trust_env)
            session.close()

    def test_never_overwrites_unrelated_existing_file(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output, state = root / "output", root / "state"
            output.mkdir()
            (output / "file.zip").write_bytes(b"user-file")
            class Session:
                pass
            with patch.dict(sys.modules, {"requests": types.SimpleNamespace(Session=Session)}), patch.object(bridge, "google_manifest", return_value=([{"id": "fixture", "path": "file.zip", "size": None}], None)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()):
                with self.assertRaises(bridge.BridgeError):
                    bridge.run({"provider": "googleDrive", "url": "https://drive.google.com/file/d/fixture/view",
                                "output": str(output), "state": str(state)})
            self.assertEqual(b"user-file", (output / "file.zip").read_bytes())


if __name__ == "__main__":
    unittest.main()
