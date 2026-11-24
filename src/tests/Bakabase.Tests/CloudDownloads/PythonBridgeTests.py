"""Offline protocol fixtures. No account, browser, live share, or downloads required."""
import contextlib
import errno
import hashlib
import importlib.util
import io
import json
import os
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


class RangeSession:
    def __init__(self, status, headers, chunks):
        self.status_code = status
        self.headers = headers
        self.chunks = chunks
        self.requests = []

    def get(self, url, **kwargs):
        self.requests.append(kwargs["headers"])
        return self

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def raise_for_status(self):
        if self.status_code >= 400:
            raise AssertionError("Unexpected unhandled error response")

    def iter_content(self, _size):
        for chunk in self.chunks:
            if isinstance(chunk, Exception):
                raise chunk
            yield chunk


class OriginalRanges:
    def __init__(self, original, failure=None):
        self.original = original
        self.failure = failure
        self.requests = []
        self.entry = {'id': 'same-file', 'url': 'https://pixeldrain.com/api/file/fixture', 'size': len(original),
                      'sha256': hashlib.sha256(original).hexdigest()}

    def get(self, url, **options):
        assert url == self.entry['url'] and options['allow_redirects'] is False
        headers = options['headers']
        assert headers['Accept-Encoding'] == 'identity'
        span = headers.get('Range')
        self.requests.append(span)
        if span is None:
            chunks = {'fresh interruption': [self.original[:4], OSError('interrupted')],
                      'fresh mismatch': [b'Q' * len(self.original)]}.get(self.failure, [self.original])
            return RangeSession(200, {'Content-Length': str(len(self.original))}, chunks)
        a, b = span.removeprefix('bytes=').split('-')
        start = int(a)
        end = int(b) if b else len(self.original) - 1
        data = self.original[start:end + 1]
        status = 206
        response_headers = {'Content-Range': f'bytes {start}-{end}/{len(self.original)}', 'Content-Length': str(len(data))}
        if b:
            if self.failure == '200': status = 200
            if self.failure == 'wrong range': response_headers['Content-Range'] = f'bytes 0-{end}/{len(self.original)}'
            if self.failure == 'short': data = data[:-1]
            if self.failure == 'oversized': data += b'more'
            if self.failure == 'different bytes': data = b'Q' * len(data)
        return RangeSession(status, response_headers, [data])


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
    def test_google_folder_http_errors_use_explicit_status_without_exposing_source(self):
        cls = type("DownloadError", (Exception,), {"__module__": "gdown.exceptions"})
        for status in (401, 403, 404, 410):
            safe = bridge.provider_failure("googleDrive", cls(
                f"Failed to retrieve folder contents for folder ID: private-id (status code {status}). You may need to change permission or have had many accesses. https://private.test/?token=secret"))
            self.assertTrue(safe.startswith("GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED:"))
            self.assertNotIn("private-id", safe)
            self.assertNotIn("secret", safe)
        for error in (cls("Failed to download the following files: 404.bin"),
                      cls("Failed to retrieve folder contents for folder ID: private-id (status code 500)."),
                      Exception("Failed to retrieve folder contents for folder ID: private-id (status code 404).")):
            self.assertTrue(bridge.provider_failure("googleDrive", error).startswith("CLOUD_TOOL_FAILED:"))

    def test_interrupted_streams_are_network_failures_not_generic_tool_failures(self):
        for module, name in (("requests.exceptions", "ChunkedEncodingError"), ("urllib3.exceptions", "ProtocolError")):
            cls = type(name, (Exception,), {"__module__": module})
            safe = bridge.provider_failure("pixeldrain", cls("https://private.test/?token=secret"))
            self.assertTrue(safe.startswith("CLOUD_NETWORK_ERROR:"))
            self.assertNotIn("secret", safe)

    def test_provider_errors_distinguish_actual_google_quota_from_generic_advice(self):
        cls = type("FileURLRetrievalError", (Exception,), {"__module__": "gdown.exceptions"})
        generic = cls("Cannot retrieve the public link. You may need to change permission or have had many accesses. https://private.test/?token=secret")
        actual = cls("Failed to retrieve file url:\n\n Too many users have viewed or downloaded this file recently.\n\n https://private.test/?token=secret")
        self.assertTrue(bridge.provider_failure("googleDrive", actual).startswith("GOOGLE_DOWNLOAD_QUOTA_EXCEEDED:"))
        safe = bridge.provider_failure("googleDrive", generic)
        self.assertTrue(safe.startswith("GOOGLE_DRIVE_SHARE_UNAVAILABLE_OR_ACCESS_REQUIRED:"))
        self.assertNotIn("secret", safe)
        self.assertNotIn("https://", safe)

    def test_typed_gofile_errors_and_transport_errors_do_not_expose_urls(self):
        for name, code in [("NotFoundError", "GOFILE_CONTENT_NOT_FOUND"), ("AuthorizationError", "GOFILE_ACCESS_REQUIRED")]:
            cls = type(name, (Exception,), {"__module__": "gallery_dl.exception"})
            safe = bridge.provider_failure("gofile", cls("https://private.test/?token=secret"))
            self.assertTrue(safe.startswith(code + ":"))
            self.assertNotIn("secret", safe)
        cls = type("SSLError", (Exception,), {"__module__": "requests.exceptions"})
        safe = bridge.provider_failure("gofile", cls("https://private.test/?token=secret"))
        self.assertTrue(safe.startswith("CLOUD_NETWORK_ERROR:"))
        self.assertNotIn("secret", safe)
        self.assertTrue(bridge.provider_failure("gofile", Exception("quota https://private.test/?token=secret")).startswith("CLOUD_TOOL_FAILED:"))

    def test_generic_http_gofile_404_is_not_provider_confirmed_absence(self):
        error = Exception("https://private.test/?token=secret")
        for status in (404, 410):
            error.status = status
            safe = bridge.provider_failure("gofile", error)
            self.assertTrue(safe.startswith("GOFILE_SHARE_NOT_FOUND:"))
            self.assertNotIn("secret", safe)

    def pixel_fixture(self, responses):
        calls = []
        extractor = types.SimpleNamespace(session=object(), initialize=lambda: None, _init_options=lambda: None)
        def request_json(url, **kwargs):
            calls.append((url, kwargs))
            return responses[url.removeprefix("https://pixeldrain.com/api/")]
        extractor.request_json = request_json
        cls = types.SimpleNamespace(from_url=lambda url: extractor)
        config = types.SimpleNamespace(clear=lambda: None, set=lambda *args: None)
        modules = {"gallery_dl": types.SimpleNamespace(config=config),
                   "gallery_dl.extractor.pixeldrain": types.SimpleNamespace(PixeldrainFileExtractor=cls)}
        return modules, calls

    def test_pixeldrain_list_keeps_original_names_and_checks_whole_list_count(self):
        file = {"id": "one", "name": "original.rar", "size": 5, "hash_sha256": hashlib.sha256(b"abcde").hexdigest()}
        album = {"file_count": 1, "files": [file]}
        modules, calls = self.pixel_fixture({"list/fixture": album})
        with patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
            entries, _ = bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/l/fixture"})
            self.assertEqual("original.rar", entries[0]["path"])
            self.assertEqual("https://pixeldrain.com/api/file/one?download", entries[0]["url"])
            self.assertEqual(False, calls[0][1]["allow_redirects"])
            album["file_count"] = 2
            with self.assertRaisesRegex(bridge.BridgeError, "incomplete"):
                bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/l/fixture"})

    def test_pixeldrain_album_item_fragment_selects_only_the_requested_original(self):
        files = [{"id": str(i), "name": f"{i}.bin", "size": 1} for i in range(2)]
        modules, _ = self.pixel_fixture({"list/fixture": {"file_count": 2, "files": files}})
        with patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
            entries, _ = bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/l/fixture#item=1"})
            self.assertEqual(["1.bin"], [e["path"] for e in entries])

    def test_pixeldrain_saved_code_preserves_anonymous_access_checks_for_each_selected_file(self):
        file = {"id": "fixture", "name": "original.rar", "size": 3, "can_download": True}
        modules, calls = self.pixel_fixture({"file/fixture/info": file})
        request = {"url": "https://pixeldrain.com/u/fixture", "accessCode": "from-post", "archivePassword": "archive"}
        with patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
            entries, _ = bridge.pixeldrain_manifest(request)
            self.assertEqual(["original.rar"], [e["path"] for e in entries])
            file["can_download"] = False
            with self.assertRaisesRegex(bridge.BridgeError, "PIXELDRAIN_ACCESS_REQUIRED"):
                bridge.pixeldrain_manifest(request)
            self.assertEqual(["https://pixeldrain.com/api/file/fixture/info"] * 2, [call[0] for call in calls])
            self.assertNotIn("from-post", str(calls))
            self.assertEqual("archive", request["archivePassword"])

    def test_pixeldrain_official_aliases_keep_item_selection_on_the_canonical_api(self):
        files = [{"id": str(i), "name": f"{i}.bin", "size": 1} for i in range(2)]
        for host in ("pixeldra.in", "www.pixeldra.in", "pixeldrain.net", "www.pixeldrain.net"):
            url = f"https://{host}/l/fixture#item=1"
            modules, calls = self.pixel_fixture({"list/fixture": {"file_count": 2, "files": files}})
            with self.subTest(host=host), patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
                bridge.validate_url("pixeldrain", url)
                entries, _ = bridge.pixeldrain_manifest({"url": url})
                self.assertEqual(["1.bin"], [e["path"] for e in entries])
                self.assertEqual("https://pixeldrain.com/api/list/fixture", calls[0][0])
                self.assertEqual("https://pixeldrain.com/api/file/1?download", entries[0]["url"])

    def test_pixeldrain_aliases_do_not_relax_url_validation(self):
        for url in ("https://pixeldra.in.attacker.test/u/fixture", "https://other.pixeldra.in/u/fixture",
                    "https://pixeldrain.net.attacker.test/u/fixture", "https://pixeldra.in/account",
                    "http://pixeldra.in/u/fixture", "https://user:secret@pixeldra.in/u/fixture",
                    "https://pixeldra.in:8443/u/fixture", "https://pixeldra.in/u/fixture#item=1",
                    "https://pixeldra.in/d/fixture/%2e%2e/other"):
            with self.subTest(url=url), self.assertRaises(bridge.BridgeError):
                bridge.validate_url("pixeldrain", url)

    def test_pixeldrain_recurses_filesystem_without_silently_skipping_unavailable_files(self):
        child = {"type": "file", "path": "/fixture/sub/file.bin", "name": "file.bin", "file_size": 3}
        responses = {
            "filesystem/fixture?stat": {"base_index": 0, "path": [{"type": "dir", "path": "/fixture"}],
                "children": [{"type": "dir", "path": "/fixture/sub", "name": "sub"}]},
            "filesystem/fixture/sub?stat": {"base_index": 0, "path": [{"type": "dir", "path": "/fixture/sub"}], "children": [child]}}
        modules, _ = self.pixel_fixture(responses)
        with patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
            entries, _ = bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/d/fixture"})
            self.assertEqual(["sub/file.bin"], [e["path"] for e in entries])
            child["availability"] = "transfer_limit_exceeded"
            with self.assertRaisesRegex(bridge.BridgeError, "PIXELDRAIN_TRANSFER_QUOTA_EXCEEDED"):
                bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/d/fixture"})

    def test_pixeldrain_http_failures_have_stable_codes_and_never_relay_signed_urls(self):
        modules, _ = self.pixel_fixture({})
        extractor = modules["gallery_dl.extractor.pixeldrain"].PixeldrainFileExtractor.from_url("unused")
        for status, code in ((404, "SHARE_UNAVAILABLE"), (410, "SHARE_UNAVAILABLE"),
                             (401, "ACCESS_REQUIRED"), (403, "ACCESS_REQUIRED"), (429, "RATE_LIMITED")):
            error = Exception("HTTP failure for https://pixeldrain.com/private-id?token=secret")
            error.status = status
            extractor.request_json = lambda *a, _error=error, **kw: (_ for _ in ()).throw(_error)
            with self.subTest(status=status), patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
                with self.assertRaisesRegex(bridge.BridgeError, "PIXELDRAIN_" + code) as raised:
                    bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/u/fixture"})
                self.assertNotIn("secret", str(raised.exception))
                self.assertNotIn("https://", str(raised.exception))

    def test_pixeldrain_api_captcha_and_limit_flags_are_not_silently_skipped(self):
        for value, code in (("file_rate_limited_captcha_required", "VERIFICATION_REQUIRED"),
                            ("transfer_limit_exceeded", "TRANSFER_QUOTA_EXCEEDED"),
                            ("download_limit_exceeded", "DOWNLOAD_QUOTA_EXCEEDED"),
                            ("max_concurrent_downloads", "RATE_LIMITED"), ("hotlink_detected", "ACCESS_REQUIRED")):
            file = {"id": "fixture", "name": "a.rar", "size": 3, "availability": value}
            modules, _ = self.pixel_fixture({"file/fixture/info": file})
            with self.subTest(value=value), patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
                with self.assertRaisesRegex(bridge.BridgeError, "PIXELDRAIN_" + code):
                    bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/u/fixture"})

    def test_pixeldrain_filesystem_rejects_nonchild_traversal_and_missing_listings(self):
        child = {"type": "file", "path": "/other/escape.bin", "name": "escape.bin", "file_size": 3}
        stat = {"base_index": 0, "path": [{"type": "dir", "path": "/fixture"}], "children": [child]}
        modules, _ = self.pixel_fixture({"filesystem/fixture?stat": stat})
        with patch.dict(sys.modules, modules), patch.object(bridge.importlib.metadata, "version", return_value="1.32.17"):
            with self.assertRaisesRegex(bridge.BridgeError, "non-child"):
                bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/d/fixture"})
            del stat["children"]
            with self.assertRaisesRegex(bridge.BridgeError, "incomplete"):
                bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/d/fixture"})

    def test_pixeldrain_checksum_failure_is_not_published_and_retry_can_finish(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            request = {"provider": "pixeldrain", "url": "https://pixeldrain.com/u/fixture", "output": str(root / "output"), "state": str(root / "state")}
            entries = [{"id": "fixture", "path": "original.rar", "size": 5, "url": "https://pixeldrain.com/api/file/fixture?download", "sha256": hashlib.sha256(b"abcde").hexdigest()}]
            class Response:
                is_redirect = False
                body = b"wrong"
                def __enter__(self): return self
                def __exit__(self, *args): pass
                def raise_for_status(self): pass
                def iter_content(self, size): yield self.body
            response = Response()
            session = types.SimpleNamespace(get=lambda *args, **kwargs: response)
            class Session:
                pass
            with patch.dict(sys.modules, {"requests": types.SimpleNamespace(Session=Session)}), patch.object(bridge, "pixeldrain_manifest", return_value=(entries, session)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()):
                with self.assertRaisesRegex(bridge.BridgeError, "SHA256"):
                    bridge.run(request)
                self.assertFalse((root / "output/original.rar").exists())
                response.body = b"abcde"
                bridge.run(request)
                self.assertEqual(b"abcde", (root / "output/original.rar").read_bytes())

    def test_pixeldrain_zero_ranges_restore_verified_original_and_preserve_legitimate_zeros(self):
        start = 1024 * 1024 - 19
        original = b'A' * start + b'B' * 180000 + b'C' * 21000 + bytes(78000) + b'D' * 120000
        prefix = len(original) - 25000
        partial = original[:start] + bytes(180000) + original[start + 180000:prefix]
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / 'file.part'
            part.write_bytes(partial)
            session = OriginalRanges(original)
            result = bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None)
            self.assertEqual(session.entry['sha256'], result)
            self.assertEqual(original, part.read_bytes())
            self.assertEqual([f'bytes={start}-{start+179999}', f'bytes={start+201000}-{start+278999}', f'bytes={prefix}-'], session.requests)
            self.assertFalse(part.with_suffix('.repair.json').exists())

    def test_pixeldrain_zero_repair_uses_original_operation_and_publishes_no_duplicate_sibling(self):
        original = b'header' + b'X' * 80000 + b'remaining payload'
        session = OriginalRanges(original)
        sibling = b'already complete'
        entries = [dict(session.entry, id='sibling', path='sibling.rar', size=len(sibling),
                        sha256=hashlib.sha256(sibling).hexdigest()),
                   dict(session.entry, path='current.rar')]
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / 'output'
            state = root / 'private-operation'
            staging = output / ('.bakabase-cloud-' + state.name)
            staging.mkdir(parents=True)
            state.mkdir()
            request = {'provider': 'pixeldrain', 'url': 'https://pixeldrain.com/l/original',
                       'output': str(output), 'state': str(state)}
            identity = hashlib.sha256(json.dumps(['pixeldrain', request['url'], None, str(output.resolve())]).encode()).hexdigest()
            manifest = [{k: e.get(k) for k in ('id', 'path', 'size', 'md5', 'sha256')} for e in entries]
            receipts = {'sibling.rar': {'size': len(sibling), 'sha256': entries[0]['sha256']}}
            (state / 'manifest.json').write_text(json.dumps({'identity': identity, 'files': manifest}))
            (state / 'completed.json').write_text(json.dumps(receipts))
            (output / 'sibling.rar').write_bytes(sibling)
            partial = b'header' + bytes(80000) + b'remaining '
            part = staging / (hashlib.sha256(b'current.rar').hexdigest() + '.part')
            part.write_bytes(partial)
            class Session: pass
            with patch.dict(sys.modules, {'requests': types.SimpleNamespace(Session=Session)}), \
                    patch.object(bridge, 'pixeldrain_manifest', return_value=(entries, session)), \
                    patch.object(bridge, 'PROTOCOL_OUTPUT', io.StringIO()):
                with self.assertRaises(bridge.BridgeError):
                    bridge.run(dict(request, url='https://pixeldrain.com/l/changed'))
                self.assertFalse(session.requests)
                self.assertEqual(partial, part.read_bytes())
                bridge.run(request)
            self.assertEqual(['bytes=6-80005', f'bytes={len(partial)}-'], session.requests)
            self.assertEqual(sibling, (output / 'sibling.rar').read_bytes())
            self.assertEqual(original, (output / 'current.rar').read_bytes())
            saved = json.loads((state / 'completed.json').read_text())
            self.assertEqual(receipts['sibling.rar'], saved['sibling.rar'])
            self.assertEqual(session.entry['sha256'], saved['current.rar']['sha256'])
            self.assertFalse(staging.exists())

    @contextlib.contextmanager
    def pixeldrain_rejected_fixture(self, root, more_members=False):
        output, state = root / 'output', root / 'private-operation'
        staging = output / ('.bakabase-cloud-' + state.name)
        staging.mkdir(parents=True)
        state.mkdir()
        request = {'provider': 'pixeldrain', 'url': 'https://pixeldrain.com/l/original',
                   'output': str(output), 'state': str(state)}
        identity = hashlib.sha256(json.dumps(['pixeldrain', request['url'], None, str(output.resolve())]).encode()).hexdigest()
        session = OriginalRanges(b'original blob')
        sibling = b'complete sibling'
        entries = [dict(session.entry, id='sibling', path='sibling.rar', size=len(sibling),
                        sha256=hashlib.sha256(sibling).hexdigest()),
                   dict(session.entry, path='current.rar')]
        if more_members:
            entries.append(dict(session.entry, id='later', path='later.rar'))
        manifest = [{k: entry.get(k) for k in ('id', 'path', 'size', 'md5', 'sha256')} for entry in entries]
        (state / 'manifest.json').write_text(json.dumps({'identity': identity, 'files': manifest}))
        receipt = {'size': len(sibling), 'sha256': entries[0]['sha256']}
        (state / 'completed.json').write_text(json.dumps({'sibling.rar': receipt}))
        (output / 'sibling.rar').write_bytes(sibling)
        part = staging / (hashlib.sha256(b'current.rar').hexdigest() + '.part')
        part.write_bytes(b'wrong')
        class Session: pass
        with patch.dict(sys.modules, {'requests': types.SimpleNamespace(Session=Session)}), \
                patch.object(bridge, 'pixeldrain_manifest', return_value=(entries, session)), \
                patch.object(bridge, 'PROTOCOL_OUTPUT', io.StringIO()):
            yield request, session, part, output, state, receipt

    def test_pixeldrain_retry_restarts_only_the_rejected_member_and_only_when_explicit(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, state, sibling_receipt = fixture
            with self.assertRaisesRegex(bridge.BridgeError, 'CLOUD_DOWNLOAD_CHECKSUM_MISMATCH'):
                bridge.run(request)
            self.assertEqual(b'wrong', part.read_bytes())
            self.assertFalse(part.with_suffix('.rejected').exists())
            self.assertEqual(['bytes=5-'], session.requests)
            bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual(['bytes=5-', None], session.requests)
            self.assertEqual(session.original, (output / 'current.rar').read_bytes())
            self.assertEqual(b'complete sibling', (output / 'sibling.rar').read_bytes())
            self.assertEqual(sibling_receipt, json.loads((state / 'completed.json').read_text())['sibling.rar'])
            self.assertFalse(part.parent.exists())
            self.assertFalse((state / (part.stem + '.rejected.json')).exists())

    def test_pixeldrain_rejected_prefix_survives_interruption_and_new_prefix_is_resumed(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, _, _ = fixture
            rejected = part.with_suffix('.rejected')
            session.failure = 'fresh interruption'
            with self.assertRaises(OSError):
                bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual(b'wrong', (rejected / 'original').read_bytes())
            self.assertEqual(session.original[:4], part.read_bytes())
            session.failure = None
            bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual([None, 'bytes=4-'], session.requests)
            self.assertEqual(session.original, (output / 'current.rar').read_bytes())
            self.assertFalse(rejected.exists())

    def test_pixeldrain_reconciles_interruption_before_atomic_move_and_after_publish(self):
        for after_publish in (False, True):
            with self.subTest(after_publish=after_publish), tempfile.TemporaryDirectory() as temp, \
                    self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
                request, session, part, output, _, _ = fixture
                destination = output / 'current.rar'
                rejected = part.with_suffix('.rejected')
                unlink = Path.unlink
                rename = os.rename
                def interrupt_unlink(path, *args, **kwargs):
                    if path == part and after_publish and destination.exists():
                        raise OSError('interrupted')
                    return unlink(path, *args, **kwargs)
                def interrupt_rename(source, target, *args, **kwargs):
                    if source == part and not after_publish:
                        raise OSError('interrupted')
                    return rename(source, target, *args, **kwargs)
                with patch.object(Path, 'unlink', interrupt_unlink), patch.object(os, 'rename', interrupt_rename), self.assertRaises(OSError):
                    bridge.run(dict(request, checksumRetryKey='1000'))
                self.assertEqual(b'wrong', (rejected / 'original' if after_publish else part).read_bytes())
                if after_publish:
                    self.assertTrue(part.samefile(destination))
                bridge.run(request)
                self.assertEqual([None], session.requests)
                self.assertEqual(session.original, destination.read_bytes())
                self.assertFalse(part.parent.exists())

    def test_pixeldrain_reuses_completed_files_and_partial_prefix_at_a_new_destination(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            original, session, part, output, state, _ = fixture
            part.write_bytes(session.original[:5])
            _, _, inventory = bridge.pixeldrain_reuse_inventory(original)
            self.assertEqual((2, 2, (output / "sibling.rar").stat().st_size + 5),
                             (inventory["fileCount"], inventory["candidateFileCount"], inventory["candidateBytes"]))
            target = Path(temp) / "new-output"
            new_state = Path(temp) / "new-operation"
            request = dict(original, output=str(target), state=str(new_state), reuseSource={
                "output": str(output), "state": str(state), "inventoryFingerprint": inventory["inventoryFingerprint"]})
            self.assertIsNone(bridge.pixeldrain_reuse_budget(request))
            bridge.run(request)
            self.assertEqual(["bytes=5-"], session.requests)
            self.assertEqual(session.original, (target / "current.rar").read_bytes())
            self.assertEqual((output / "sibling.rar").read_bytes(), (target / "sibling.rar").read_bytes())
            self.assertEqual(session.original[:5], part.read_bytes())
            self.assertEqual(0, bridge.pixeldrain_reuse_budget(request))
            output.rename(Path(temp) / "offline")
            request["reuseSource"]["disableSourceRead"] = True
            bridge.run(request)
            self.assertEqual(["bytes=5-"], session.requests)

    def test_pixeldrain_reuse_resumes_an_interrupted_copy_without_overwriting_existing_bytes(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            original, session, part, output, state, _ = fixture
            part.write_bytes(session.original[:5])
            _, _, inventory = bridge.pixeldrain_reuse_inventory(original)
            target = Path(temp) / "new-output"
            request = dict(original, output=str(target), state=str(Path(temp) / "new-operation"), reuseSource={
                "output": str(output), "state": str(state), "inventoryFingerprint": inventory["inventoryFingerprint"]})
            untouched = bridge.copy_reuse_part

            def interrupt(source, identity, destination, checksum):
                if source == part:
                    destination.write_bytes(session.original[:2])
                    raise OSError("interrupted cross-volume copy")
                untouched(source, identity, destination, checksum)

            with patch.object(bridge, "copy_reuse_part", side_effect=interrupt), self.assertRaisesRegex(OSError, "interrupted"):
                bridge.run(request)
            self.assertEqual([], session.requests)
            self.assertFalse((target / "current.rar").exists())
            self.assertIsNone(bridge.pixeldrain_reuse_budget(request))
            bridge.run(request)
            self.assertEqual(["bytes=5-"], session.requests)
            self.assertEqual(session.original, (target / "current.rar").read_bytes())
            self.assertEqual(session.original[:5], part.read_bytes())

    def test_pixeldrain_reuse_refuses_a_changed_candidate_or_a_sealed_repair(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            original, session, part, output, state, _ = fixture
            part.write_bytes(session.original[:5])
            _, _, inventory = bridge.pixeldrain_reuse_inventory(original)
            target = Path(temp) / "new-output"
            request = dict(original, output=str(target), state=str(Path(temp) / "new-operation"), reuseSource={
                "output": str(output), "state": str(state), "inventoryFingerprint": inventory["inventoryFingerprint"]})
            part.write_bytes(b"stale")
            with self.assertRaisesRegex(bridge.BridgeError, "REUSE_SOURCE_CHANGED"):
                bridge.run(request)
            self.assertEqual([], session.requests)
            self.assertEqual(b"stale", part.read_bytes())
            self.assertFalse((target / "current.rar").exists())
            (state / (part.stem + ".repair.json")).write_text("{}")
            with self.assertRaisesRegex(bridge.BridgeError, "REUSE_SOURCE_CHANGED"):
                bridge.pixeldrain_reuse_inventory(original)

    @unittest.skipUnless(sys.platform == 'linux', 'uses Linux no-replace publication')
    def test_unsupported_hardlinks_publish_and_reuse_the_verified_file(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, state, sibling_receipt = fixture
            part.write_bytes(session.original[:5])
            with patch.object(os, 'link', side_effect=OSError(errno.ENOTSUP, 'hardlinks unavailable')):
                bridge.run(request)
            self.assertEqual(session.original, (output / 'current.rar').read_bytes())
            self.assertFalse(part.parent.exists())
            receipts = json.loads((state / 'completed.json').read_text())
            self.assertEqual(sibling_receipt, receipts['sibling.rar'])
            self.assertEqual(session.entry['sha256'], receipts['current.rar']['sha256'])
            bridge.run(request)
            self.assertEqual(['bytes=5-'], session.requests)

    @unittest.skipUnless(sys.platform == 'linux', 'uses Linux no-replace publication')
    def test_no_replace_publication_preserves_a_concurrent_destination(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, _, _ = fixture
            part.write_bytes(session.original[:5])
            destination = output / 'current.rar'

            def collide(source, target):
                self.assertEqual(part, source)
                self.assertEqual(destination, target)
                target.write_bytes(b'concurrent foreign file')
                raise OSError(errno.ENOTSUP, 'hardlinks unavailable')

            with patch.object(os, 'link', side_effect=collide), \
                    self.assertRaisesRegex(bridge.BridgeError, 'without overwriting'):
                bridge.run(request)
            self.assertEqual(b'concurrent foreign file', destination.read_bytes())
            self.assertEqual(session.original, part.read_bytes())

    @unittest.skipUnless(sys.platform == 'linux', 'uses Linux no-replace publication')
    def test_no_replace_publication_replays_rejected_partial_cleanup_after_move(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, state, _ = fixture
            destination = output / 'current.rar'
            rejected = part.with_suffix('.rejected')
            release = bridge.release_rejected_partial

            def interrupt(*args):
                if args[1] == part:
                    self.assertFalse(part.exists())
                    self.assertEqual(session.original, destination.read_bytes())
                    raise OSError('interrupted after move')
                return release(*args)

            with patch.object(os, 'link', side_effect=OSError(errno.ENOTSUP, 'hardlinks unavailable')), \
                    patch.object(bridge, 'release_rejected_partial', side_effect=interrupt), \
                    self.assertRaisesRegex(OSError, 'interrupted after move'):
                bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual(b'wrong', (rejected / 'original').read_bytes())
            bridge.run(request)
            self.assertEqual([None], session.requests)
            self.assertEqual(session.original, destination.read_bytes())
            self.assertFalse(part.parent.exists())
            self.assertEqual([], list(state.glob('*.rejected.json')))

    def test_publication_does_not_fallback_for_link_permission_or_collision_errors(self):
        for code in (errno.EACCES, errno.EEXIST):
            with self.subTest(code=code), patch.object(os, 'link', side_effect=OSError(code, 'link refused')), \
                    patch.object(bridge, 'rename_noreplace') as rename:
                with self.assertRaises(OSError) as error:
                    bridge.publish_verified_part(Path('source'), Path('destination'), None, Path('state'))
                self.assertEqual(code, error.exception.errno)
                rename.assert_not_called()

    def test_pixeldrain_rejected_retry_refuses_unowned_slot_and_preserves_original_on_fresh_mismatch(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, state, _ = fixture
            rejected = part.with_suffix('.rejected')
            rejected.write_bytes(b'unowned')
            with self.assertRaisesRegex(bridge.BridgeError, 'CLOUD_PARTIAL_IDENTITY_UNVERIFIED'):
                bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual([], session.requests)
            self.assertEqual(b'unowned', rejected.read_bytes())
            self.assertEqual(b'wrong', part.read_bytes())
            rejected.unlink()
            session.failure = 'fresh mismatch'
            with self.assertRaisesRegex(bridge.BridgeError, 'CLOUD_DOWNLOAD_CHECKSUM_MISMATCH'):
                bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual([None], session.requests)
            self.assertEqual(b'wrong', (rejected / 'original').read_bytes())
            self.assertEqual(0, part.stat().st_size)
            self.assertFalse((output / 'current.rar').exists())
            self.assertNotIn('current.rar', json.loads((state / 'completed.json').read_text()))

    def test_pixeldrain_replays_verified_publish_and_preserves_ambiguous_legacy_quarantine(self):
        for replacement in (False, True):
            with self.subTest(replacement=replacement), tempfile.TemporaryDirectory() as temp, \
                    self.pixeldrain_rejected_fixture(Path(temp), more_members=True) as fixture:
                request, session, part, output, state, _ = fixture
                os.utime(part, ns=(1700000000802173200, 1700000000802173200))
                original = bridge.file_identity(part.stat())
                rejected = part.with_suffix('.rejected')
                record = state / (part.stem + '.rejected.json')
                identity = json.loads((state / 'manifest.json').read_text())['identity']
                record.write_text(json.dumps({'version': 1, 'operation': identity, 'member': part.stem,
                                              'size': len(session.original), 'sha256': session.entry['sha256'],
                                              'original': original, 'key': '1000'}))
                os.rename(part, rejected)
                if replacement:
                    rejected.unlink()
                    rejected.write_bytes(b'unrelated')
                else:
                    # SMB recycle adds a name for the same old inode and rounds its mtime.
                    os.link(rejected, Path(temp) / 'recycled')
                    os.utime(rejected, ns=(1700000000000000000, 1700000000000000000))
                    self.assertEqual(original[:3], bridge.file_identity(rejected.stat())[:3])
                    self.assertEqual(2, rejected.stat().st_nlink)
                part.write_bytes(session.original)
                destination = output / 'current.rar'
                os.link(part, destination)
                self.assertEqual(2, destination.stat().st_nlink)
                destination.chmod(0o400)
                receipts = json.loads((state / 'completed.json').read_text())
                receipts['current.rar'] = {'size': len(session.original), 'sha256': session.entry['sha256']}
                (state / 'completed.json').write_text(json.dumps(receipts))
                bridge.run(request)
                self.assertEqual([None], session.requests)  # Only the following member needed a download.
                self.assertEqual(session.original, destination.read_bytes())
                self.assertEqual(session.original, (output / 'later.rar').read_bytes())
                self.assertFalse(part.exists())
                self.assertEqual(b'unrelated' if replacement else b'wrong', rejected.read_bytes())
                self.assertTrue(record.is_file())

    def test_pixeldrain_rejected_retry_checks_verified_siblings_before_touching_the_partial(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
            request, session, part, output, _, _ = fixture
            (output / 'sibling.rar').write_bytes(b'X' * len(b'complete sibling'))
            with self.assertRaisesRegex(bridge.BridgeError, 'not a verified result'):
                bridge.run(dict(request, checksumRetryKey='1000'))
            self.assertEqual([], session.requests)
            self.assertEqual(b'wrong', part.read_bytes())
            self.assertFalse(part.with_suffix('.rejected').exists())

    def test_pixeldrain_rejected_retry_preserves_conflicting_receipt_and_repair_record(self):
        for kind in ('completion', 'repair'):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as temp, \
                    self.pixeldrain_rejected_fixture(Path(temp)) as fixture:
                request, session, part, _, state, sibling = fixture
                if kind == 'completion':
                    record = state / 'completed.json'
                    pinned = {'sibling.rar': sibling,
                              'current.rar': {'size': len(session.original), 'sha256': session.entry['sha256']}}
                else:
                    record = state / (part.stem + '.repair.json')
                    pinned = {'size': len(session.original), 'sha256': session.entry['sha256'], 'ranges': [[0, 2]]}
                record.write_text(json.dumps(pinned))
                with self.assertRaises(bridge.BridgeError):
                    bridge.run(dict(request, checksumRetryKey='1000'))
                self.assertEqual(['bytes=5-'] if kind == 'completion' else [], session.requests)
                self.assertEqual(b'wrong', part.read_bytes())
                self.assertEqual(pinned, json.loads(record.read_text()))
                self.assertFalse(part.with_suffix('.rejected').exists())

    def test_pixeldrain_pins_spent_generation_after_its_member_is_published(self):
        with tempfile.TemporaryDirectory() as temp, self.pixeldrain_rejected_fixture(Path(temp), more_members=True) as fixture:
            request, session, part, output, state, _ = fixture
            later = part.parent / (hashlib.sha256(b'later.rar').hexdigest() + '.part')
            later.write_bytes(b'bad')
            for _ in range(2):
                with self.assertRaisesRegex(bridge.BridgeError, 'CLOUD_DOWNLOAD_CHECKSUM_MISMATCH'):
                    bridge.run(dict(request, checksumRetryKey='1000'))
                self.assertEqual(session.original, (output / 'current.rar').read_bytes())
                self.assertEqual(b'bad', later.read_bytes())
                self.assertFalse(later.with_suffix('.rejected').exists())
                saved = json.loads((state / (part.stem + '.rejected.json')).read_text())
                self.assertEqual('1000', saved['key'])
            self.assertEqual([None, 'bytes=3-', 'bytes=3-'], session.requests)
            bridge.run(dict(request, checksumRetryKey='2000'))
            self.assertEqual([None, 'bytes=3-', 'bytes=3-', None], session.requests)
            self.assertEqual(session.original, (output / 'later.rar').read_bytes())
            self.assertEqual([], list(state.glob('*.rejected.json')))

    def test_pixeldrain_zero_repairs_require_confirmed_ranges_and_correct_whole_content(self):
        original = b'prefix' + b'X'*70000 + b'abcdefghij'
        partial = b'prefix' + bytes(70000) + b'abc'
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / 'file.part'
            for failure in ('200', 'wrong range', 'short', 'oversized', 'different bytes', 'other local corruption'):
                with self.subTest(failure=failure):
                    local = (b'Q'+partial[1:]) if failure=='other local corruption' else partial
                    part.write_bytes(local)
                    session = OriginalRanges(original, failure)
                    with self.assertRaises(bridge.BridgeError):
                        bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None)
                    self.assertEqual(local, part.read_bytes())
                    self.assertFalse(part.with_suffix('.repair.json').exists())
                    self.assertEqual(2 if failure in ('different bytes', 'other local corruption') else 1, len(session.requests))

    def test_pixeldrain_zero_range_budget_preserves_ordinary_files_and_limits_requests(self):
        for original, max_ranges, max_bytes in [(b'a' + bytes(75000) + b'bcde', 16, 70000),
                ((b'a' + bytes(70000))*3 + b'end', 2, 1000000)]:
            with self.subTest(max_ranges=max_ranges), tempfile.TemporaryDirectory() as temp:
                part = Path(temp) / 'file.part'
                part.write_bytes(original[:-2])
                session = OriginalRanges(original)
                with patch.object(bridge, 'MAX_REPAIR_BYTES', max_bytes), patch.object(bridge, 'MAX_REPAIR_INTERVALS', max_ranges):
                    bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None)
                self.assertEqual(original, part.read_bytes())
                self.assertEqual([f'bytes={len(original)-2}-'], session.requests)

    def test_pixeldrain_zero_commit_resumes_interrupted_write_with_no_suffix_request(self):
        original = b'head' + b'X'*70000 + b'end'
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / 'file.part'
            record = Path(temp) / 'receipt.json'
            session = OriginalRanges(original)
            # Power loss after all but 127 bytes of a proven interval were written.
            current = original[:4+70000-127] + bytes(127) + original[-3:]
            part.write_bytes(current)
            journal = {'size':len(original), 'sha256':session.entry['sha256'], 'ranges':[[4,70000]]}
            record.write_text(json.dumps(journal))
            for bad in ('different identity','changed data','oversized range'):
                with self.subTest(bad=bad):
                    data = dict(journal)
                    local = current
                    if bad=='different identity': data['sha256']='a'*64
                    if bad=='changed data': local=current[:4]+b'Y'+current[5:]
                    if bad=='oversized range': data['ranges']=[[4,9999999]]
                    record.write_text(json.dumps(data))
                    part.write_bytes(local)
                    with self.assertRaisesRegex(bridge.BridgeError,'CLOUD_PARTIAL_IDENTITY_UNVERIFIED'):
                        bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None, record)
                    self.assertEqual(local, part.read_bytes())
                    self.assertTrue(record.exists())
            part.write_bytes(b'')
            record.write_text(json.dumps(journal))
            session.requests.clear()
            with self.assertRaisesRegex(bridge.BridgeError, 'CLOUD_PARTIAL_IDENTITY_UNVERIFIED'):
                bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None, record)
            self.assertFalse(session.requests)
            self.assertTrue(record.exists())
            part.write_bytes(current)
            record.write_text(json.dumps(journal))
            session.requests.clear()
            bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_:None, record)
            self.assertEqual(['bytes=4-70003'],session.requests)
            self.assertEqual(original, part.read_bytes())
            self.assertFalse(record.exists())

    def test_http_resumes_a_verified_original_at_the_exact_offset_and_reuses_completed_part(self):
        body = b"abcdefghij"
        entry = {"url": "https://pixeldrain.com/api/file/fixture", "size": len(body), "sha256": hashlib.sha256(body).hexdigest()}
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / "local.part"
            part.write_bytes(body[:4])
            session = RangeSession(206, {"Content-Range": "bytes 4-9/10", "Content-Length": "6"}, [body[4:]])
            events = []
            actual = bridge.http_original(session, entry, part, "pixeldrain", lambda *v: events.append(v))
            self.assertEqual(body, part.read_bytes())
            self.assertEqual(entry["sha256"], actual)
            self.assertEqual([{"Accept-Encoding": "identity", "Range": "bytes=4-"}], session.requests)
            self.assertEqual([(4, 10), (10, 10)], events)
            self.assertEqual(actual, bridge.http_original(session, entry, part, "pixeldrain", lambda *_: None))
            self.assertEqual(1, len(session.requests))

    def test_http_range_refusals_leave_the_original_partial_untouched(self):
        body = b"abcdef"
        entry = {"url": "https://pixeldrain.com/api/file/fixture", "size": 6, "sha256": hashlib.sha256(body).hexdigest()}
        normal = {"Content-Range": "bytes 2-5/6", "Content-Length": "4"}
        cases = [(200, {}, body), (416, {}, b""),
                 (206, {"Content-Range": "bytes 0-5/6"}, body), (206, {"Content-Range": "bytes 2-4/6"}, b"cde"),
                 (206, {"Content-Range": "bytes 2-5/8"}, body[2:]), (206, {}, body[2:]),
                 (206, dict(normal, **{"Content-Encoding": "gzip"}), body[2:]),
                 (206, dict(normal, **{"Content-Length": "5"}), body[2:]), (206, normal, b"wrong"), (206, normal, b"nope")]
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / "local.part"
            for status, headers, payload in cases:
                with self.subTest(status=status, headers=headers, payload=payload):
                    part.write_bytes(body[:2])
                    with self.assertRaises(bridge.BridgeError):
                        bridge.http_original(RangeSession(status, headers, [payload]), entry, part, "pixeldrain", lambda *_: None)
                    self.assertEqual(body[:2], part.read_bytes())
            for existing, checksum in [(body[:2], None), (body + b"long", entry["sha256"]), (b"wrong!", entry["sha256"])]:
                part.write_bytes(existing)
                session = RangeSession(200, {}, [body])
                with self.assertRaises(bridge.BridgeError):
                    bridge.http_original(session, dict(entry, sha256=checksum), part, "pixeldrain", lambda *_: None)
                self.assertEqual([], session.requests)
                self.assertEqual(existing, part.read_bytes())

    def test_http_range_interruption_keeps_new_bytes_and_next_request_fetches_only_the_rest(self):
        body = b"abcdef"
        entry = {"url": "https://pixeldrain.com/api/file/fixture", "size": 6, "sha256": hashlib.sha256(body).hexdigest()}
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / "local.part"
            part.write_bytes(body[:2])
            with self.assertRaises(ConnectionError):
                bridge.http_original(RangeSession(206, {"Content-Range": "bytes 2-5/6"}, [b"c", ConnectionError()]),
                                     entry, part, "pixeldrain", lambda *_: None)
            self.assertEqual(b"abc", part.read_bytes())
            session = RangeSession(206, {"Content-Range": "bytes 3-5/6"}, [b"def"])
            bridge.http_original(session, entry, part, "pixeldrain", lambda *_: None)
            self.assertEqual("bytes=3-", session.requests[0]["Range"])
            self.assertEqual(body, part.read_bytes())

    def test_payload_file_io_counts_suffix_and_in_place_repair_without_the_saved_prefix(self):
        original = b'prefix' + b'X' * 70000 + b'abcdefghij'
        partial = b'prefix' + bytes(70000) + b'abc'
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / 'local.part'
            part.write_bytes(partial)
            session = OriginalRanges(original)
            events = []
            with patch.object(bridge, 'emit', events.append), patch.object(bridge.time, 'monotonic', return_value=0):
                bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None, observe_file_io=True)
            self.assertEqual(original, part.read_bytes())
            self.assertEqual([{'type': 'fileIo', 'state': 'opened'},
                              {'type': 'fileIo', 'state': 'written', 'writtenBytes': 70007},
                              {'type': 'fileIo', 'state': 'closed'}], events)

            events.clear()
            with patch.object(bridge, 'emit', events.append):
                bridge.http_original(session, session.entry, part, 'pixeldrain', lambda *_: None)
            self.assertEqual([], events)

    def test_payload_file_io_flushes_before_a_transfer_error_and_skips_unopened_files(self):
        body = b'abcdef'
        entry = {'url': 'https://pixeldrain.com/api/file/fixture', 'size': len(body), 'sha256': hashlib.sha256(body).hexdigest()}
        with tempfile.TemporaryDirectory() as temp:
            part = Path(temp) / 'local.part'
            part.write_bytes(body[:2])
            session = RangeSession(206, {'Content-Range': 'bytes 2-5/6'}, [b'c', ConnectionError()])
            events = []
            with patch.object(bridge, 'emit', events.append), patch.object(bridge.time, 'monotonic', return_value=0):
                with self.assertRaises(ConnectionError):
                    bridge.http_original(session, entry, part, 'pixeldrain', lambda *_: None, observe_file_io=True)
                self.assertEqual(b'abc', part.read_bytes())
                self.assertEqual([{'type': 'fileIo', 'state': 'opened'},
                                  {'type': 'fileIo', 'state': 'written', 'writtenBytes': 1},
                                  {'type': 'fileIo', 'state': 'closed'}], events)

                events.clear()
                with self.assertRaises(FileNotFoundError):
                    bridge.http_original(session, entry, Path(temp) / 'missing' / 'file.part', 'pixeldrain',
                                         lambda *_: None, observe_file_io=True)
                self.assertEqual([], events)

    def test_payload_file_io_reports_at_byte_and_time_boundaries(self):
        now = [0]
        events = []
        with patch.object(bridge, 'emit', events.append), patch.object(bridge.time, 'monotonic', side_effect=lambda: now[0]):
            with bridge.PayloadWriteObservation() as observation:
                observation.start()
                observation.add(16 * 1024 * 1024)
                observation.add(1)
                self.assertEqual(2, len(events))
                now[0] = 1
                observation.add(2)
                self.assertEqual(3, len(events))
                observation.add(4)
        self.assertEqual([16 * 1024 * 1024 + delta for delta in [0, 3, 7]],
                         [event['writtenBytes'] for event in events if event['state'] == 'written'])

    def test_partial_without_recorded_operation_identity_cannot_be_claimed_by_a_retry(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            staging = root / "out" / ".bakabase-cloud-state"
            staging.mkdir(parents=True)
            part = staging / (hashlib.sha256(b"a.txt").hexdigest() + ".part")
            part.write_bytes(b"a")
            entries = [{"id": "fixture", "path": "a.txt", "size": 2, "url": "https://pixeldrain.com/api/file/fixture", "sha256": hashlib.sha256(b"ab").hexdigest()}]
            request = {"provider": "pixeldrain", "url": "https://pixeldrain.com/u/fixture", "output": str(root / "out"), "state": str(root / "state")}
            class Session:
                pass
            with patch.dict(sys.modules, {"requests": types.SimpleNamespace(Session=Session)}), patch.object(bridge, "pixeldrain_manifest", return_value=(entries, None)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()):
                with self.assertRaisesRegex(bridge.BridgeError, "original operation manifest"):
                    bridge.run(request)
                self.assertFalse((root / "state/manifest.json").exists())
                self.assertEqual(b"a", part.read_bytes())

    def test_legacy_manifest_resumes_only_the_same_operation_and_files_with_verified_original_checksums(self):
        first, second = b"first", b"second"
        entries = [{"id": str(i), "path": str(i) + ".zip", "size": len(body),
                    "url": "https://pixeldrain.com/api/file/" + str(i), "sha256": hashlib.sha256(body).hexdigest()}
                   for i, body in enumerate([first, second])]
        for mode in ["same", "different-operation", "different-name", "different-size", "different-id", "different-checksum", "foreign-receipt"]:
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                output, state = root / "out", root / "state"
                staging = output / ".bakabase-cloud-state"
                staging.mkdir(parents=True)
                state.mkdir()
                request = {"provider": "pixeldrain", "url": "https://pixeldrain.com/l/test", "output": str(output), "state": str(state)}
                identity = hashlib.sha256(json.dumps(["pixeldrain", request["url"], None, str(output.resolve())]).encode()).hexdigest()
                old = [{k: e.get(k) for k in ("id", "path", "size", "md5")} for e in entries]
                if mode == "different-name": old[1]["path"] = "old.zip"
                if mode == "different-size": old[1]["size"] = 9
                if mode == "different-id": old[1]["id"] = "other"
                if mode == "different-checksum": old[1]["sha256"] = hashlib.sha256(b"other").hexdigest()
                saved = {"identity": "other" if mode == "different-operation" else identity, "files": old}
                (state / "manifest.json").write_text(json.dumps(saved))
                original = b"other" if mode == "foreign-receipt" else first
                (output / "0.zip").write_bytes(original)
                receipts = {"0.zip": {"size": len(original), "sha256": hashlib.sha256(original).hexdigest()}}
                (state / "completed.json").write_text(json.dumps(receipts))
                part = staging / (hashlib.sha256(b"1.zip").hexdigest() + ".part")
                part.write_bytes(second[:3])
                session = RangeSession(206, {"Content-Range": "bytes 3-5/6", "Content-Length": "3"}, [second[3:]])
                class Session:
                    pass
                with patch.dict(sys.modules, {"requests": types.SimpleNamespace(Session=Session)}), patch.object(bridge, "pixeldrain_manifest", return_value=(entries, session)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()):
                    if mode == "same":
                        bridge.run(request)
                        self.assertEqual(second, (output / "1.zip").read_bytes())
                        self.assertEqual([{"Accept-Encoding": "identity", "Range": "bytes=3-"}], session.requests)
                        self.assertEqual(entries[1]["sha256"], json.loads((state / "manifest.json").read_text())["files"][1]["sha256"])
                    else:
                        with self.assertRaises(bridge.BridgeError):
                            bridge.run(request)
                        self.assertEqual([], session.requests)
                        self.assertEqual(second[:3], part.read_bytes())
                        self.assertEqual(saved, json.loads((state / "manifest.json").read_text()))
                    self.assertEqual(original, (output / "0.zip").read_bytes())

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

    def test_multiple_files_report_operation_bytes_and_unknown_totals_stay_unknown(self):
        for known in (True, False):
            with self.subTest(known=known), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                request = {"provider": "googleDrive", "url": "https://drive.google.com/drive/folders/fixture",
                           "output": str(root / "output"), "state": str(root / "state")}
                entries = [{"id": str(i), "path": str(i) + ".zip", "size": 8 if known else None} for i in range(2)]
                def download(**options):
                    Path(options["output"]).write_bytes(b"complete")
                    options["progress"](8, 8)
                    return options["output"]
                class Session:
                    pass
                modules = {"requests": types.SimpleNamespace(Session=Session), "gdown": types.SimpleNamespace(download=download)}
                with patch.dict(sys.modules, modules), patch.object(bridge, "google_manifest", return_value=(entries, None)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()) as stdout:
                    bridge.run(request)
                    messages = [json.loads(line) for line in stdout.getvalue().splitlines()]
                    counters = [m for m in messages if "completedBytes" in m]
                    self.assertEqual([8, 8, 16, 16], [m["completedBytes"] for m in counters])
                    self.assertTrue(all(m["totalBytes"] == (16 if known else None) for m in counters))
                    # Resume with the first completed file retained. Its bytes count once, not twice.
                    (root / "output/1.zip").unlink()
                    stdout.truncate(0)
                    stdout.seek(0)
                    bridge.run(request)
                    messages = [json.loads(line) for line in stdout.getvalue().splitlines()]
                    counters = [m for m in messages if "completedBytes" in m]
                    self.assertEqual([16, 16], [m["completedBytes"] for m in counters])

    def test_reused_file_between_transfers_resets_speed_without_counting_it_as_network_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            request = {"provider": "googleDrive", "url": "https://drive.google.com/drive/folders/fixture",
                       "output": str(root / "output"), "state": str(root / "state")}
            entries = [{"id": str(i), "path": str(i) + ".zip", "size": 8} for i in range(3)]
            downloads = []
            def download(**options):
                downloads.append(options["output"])
                Path(options["output"]).write_bytes(b"complete")
                options["progress"](8, 8)
                return options["output"]
            class Session:
                pass
            modules = {"requests": types.SimpleNamespace(Session=Session), "gdown": types.SimpleNamespace(download=download)}
            with patch.dict(sys.modules, modules), patch.object(bridge, "google_manifest", return_value=(entries, None)), patch.object(bridge, "PROTOCOL_OUTPUT", io.StringIO()) as stdout:
                bridge.run(request)
                (root / "output/0.zip").unlink()
                (root / "output/2.zip").unlink()
                stdout.truncate(0)
                stdout.seek(0)
                downloads.clear()
                bridge.run(request)
                progress = [json.loads(line) for line in stdout.getvalue().splitlines()
                            if json.loads(line)["type"] == "progress"]
                self.assertEqual(2, len(downloads), "The verified middle file must not be downloaded again")
                reuse = next(i for i, message in enumerate(progress) if message["stage"] == "reusing")
                self.assertGreater(reuse, 0)
                self.assertLess(reuse, len(progress) - 1)
                self.assertNotIn("completedBytes", progress[reuse])
                self.assertNotIn("totalBytes", progress[reuse])
                self.assertEqual(8, progress[reuse - 1]["completedBytes"])
                self.assertEqual(24, progress[reuse + 1]["completedBytes"])
                self.assertEqual(b"complete", (root / "output/1.zip").read_bytes())

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

    def test_pinned_pixeldrain_library_contract_without_network(self):
        try:
            from gallery_dl.extractor.pixeldrain import PixeldrainFileExtractor
        except ImportError:
            self.skipTest("Optional gallery-dl runtime is not installed in this interpreter")
        fixture = {"success": True, "id": "fixture", "name": "original.rar", "size": 3,
                   "hash_sha256": hashlib.sha256(b"abc").hexdigest()}
        with patch.object(PixeldrainFileExtractor, "request_json", return_value=fixture) as request:
            entries, session = bridge.pixeldrain_manifest({"url": "https://pixeldrain.com/u/fixture"})
            self.assertEqual("original.rar", entries[0]["path"])
            self.assertFalse(session.trust_env)
            request.assert_called_once_with("https://pixeldrain.com/api/file/fixture/info", allow_redirects=False)
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
