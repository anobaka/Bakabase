#!/usr/bin/env python3
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock

SPEC = importlib.util.spec_from_file_location('macos_data_seed', Path(__file__).with_name('macos-data-seed.py'))
seed = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(seed)


class FakeResponse:
    def __init__(self, value=None, *, status=200, raw=None, declared=None):
        self.status = status
        self.body = io.BytesIO(raw if raw is not None else json.dumps(value).encode())
        self.declared = declared
    def getheader(self, key):
        return self.declared
    def read1(self, count):
        return self.body.read(count)


class HttpTests(unittest.TestCase):
    def connection(self, response):
        c = Mock()
        c.getresponse.return_value = response
        return c

    def test_exact_loopback_no_environment_proxy_and_response_evidence(self):
        c = self.connection(FakeResponse({'code': 0, 'data': {'retained': True}}))
        evidence = []
        with patch.dict('os.environ', {'HTTP_PROXY': 'http://invalid.invalid:1234'}), \
             patch.object(seed.http.client, 'HTTPConnection', return_value=c) as constructor:
            self.assertEqual({'retained': True}, seed.Api({'port': 12345}, evidence).call('POST', '/app/terms', {}))
        self.assertEqual(('127.0.0.1', 12345), constructor.call_args.args)
        self.assertGreater(constructor.call_args.kwargs['timeout'], 0)
        self.assertLessEqual(constructor.call_args.kwargs['timeout'], 5)
        self.assertEqual({'code': 0, 'data': {'retained': True}}, evidence[0]['response'])
        c.close.assert_called_once()

    def test_nonlocal_paths_and_invalid_ports_never_connect(self):
        with patch.object(seed.http.client, 'HTTPConnection') as constructor:
            for port in (True, 0, 65536, '1234'):
                with self.subTest(port=port), self.assertRaises(AssertionError):
                    seed.Api({'port': port}, [])
            for path in ('http://example.com/a', '//example.com/a', '/ok\r\nHost: evil', '/#fragment'):
                with self.subTest(path=path), self.assertRaises(AssertionError):
                    seed.Api({'port': 1234}, []).call('GET', path)
            constructor.assert_not_called()

    def test_http_redirect_errors_and_unsuccessful_envelopes_fail_once(self):
        for response in (FakeResponse(status=302), FakeResponse(status=503),
                         FakeResponse({'code': 1, 'data': None}), FakeResponse({'code': False}),
                         FakeResponse(raw=b'{"code":0,"data":NaN}')):
            with self.subTest(response=response):
                c = self.connection(response)
                with patch.object(seed.http.client, 'HTTPConnection', return_value=c) as constructor:
                    with self.assertRaises((AssertionError, ValueError)):
                        seed.Api({'port': 1234}, []).call('GET', '/resource/keys')
                constructor.assert_called_once()
                c.request.assert_called_once()
                c.close.assert_called_once()

    def test_request_and_both_response_size_bounds(self):
        with patch.object(seed.http.client, 'HTTPConnection') as constructor:
            with self.assertRaisesRegex(AssertionError, 'request body'):
                seed.Api({'port': 1234}, []).call('POST', '/resource/placeholder', {'value': 'x' * seed.MAX_REQUEST_BYTES})
            constructor.assert_not_called()
        for response in (FakeResponse(declared=str(seed.MAX_RESPONSE_BYTES + 1)),
                         FakeResponse(raw=b'x' * (seed.MAX_RESPONSE_BYTES + 1))):
            c = self.connection(response)
            with patch.object(seed.http.client, 'HTTPConnection', return_value=c):
                with self.assertRaisesRegex(AssertionError, 'response body'):
                    seed.Api({'port': 1234}, []).call('GET', '/resource/keys')
            c.close.assert_called_once()

    def test_exhausted_deadline_does_not_connect(self):
        with patch.object(seed.http.client, 'HTTPConnection') as constructor:
            api = seed.Api({'port': 1234}, [])
            api.deadline = 0
            with self.assertRaisesRegex(AssertionError, 'deadline'):
                api.call('GET', '/app/info')
            constructor.assert_not_called()


class OldApi:
    """In-memory HTTP response fixture, with writes tracked independently."""
    def __init__(self, apps):
        self.apps = apps
        self.calls = []
        self.version = seed.release.OLD_VERSION
        self.created = [{'resourceId': i, 'created': True, 'error': None} for i in (11, 12, 13)]
        self.resources = [dict(id=i, path=None, isFile=False, parentId=None, hasLocalPath=False,
                               displayName=None, pinned=False, playedAt=None, properties={})
                          for i, n in zip((11, 12, 13), ('旧版父目录', '旧版子文件初始名称', '旧版无路径资源'))]
        self.collection = {}
        self.members = []
        self.merge = False

    def factory(self, app, evidence):
        owner = self
        class Instance:
            def call(self, method, path, payload=None):
                data = owner.call(app, method, path, payload)
                evidence.append({'method': method, 'path': path, 'request': payload,
                                 'response': {'code': 0, 'data': copy.deepcopy(data)}})
                return copy.deepcopy(data)
        return Instance()

    def call(self, app, method, path, payload):
        self.calls.append((method, path, copy.deepcopy(payload)))
        if path in ('/app/info', '/client/app/info'):
            return {'coreVersion': self.version, 'version': self.version,
                    'appDataPath': str(app['data']), 'dataDirectory': str(app['data'])}
        if path == '/app/terms': return None
        if path == '/resource/placeholder': return self.created
        if path.endswith('/materialize'):
            i = int(path.split('/')[2]); r = next(r for r in self.resources if r['id'] == i)
            r.update(path=payload['path'], hasLocalPath=True, isFile=i == 12, parentId=11 if i == 12 else None)
            r['displayName'] = Path(payload['path']).name
            return {'materialized': True, 'merged': self.merge, 'path': payload['path']}
        if path.endswith('/property-value'):
            r = self.resources[1]; pool = '4' if payload['isCustomProperty'] else '2'
            # The old String standard value is the exact DTO string. Decimal
            # alone is parsed; JSON decoding all values hides double encoding.
            value = payload['value']
            if not isinstance(value, str): raise AssertionError('Old property value DTO requires a string')
            if not payload['isCustomProperty'] and payload['propertyId'] == 13: value = float(value)
            r['properties'].setdefault(pool, {})[str(payload['propertyId'])] = {'values': [{'scope': 0, 'bizValue': value}]}
            return None
        if path == '/custom-property': return {'id': 7, **payload}
        if path.endswith('/pin?pin=true'):
            self.resources[1]['pinned'] = True; return None
        if path.endswith('/played-at'):
            self.resources[1]['playedAt'] = '2026-09-22T08:00:00'; return None
        if path == '/collection':
            self.collection = {'id': 5, **payload}; return self.collection
        if path == '/collection/5/members':
            self.members = [dict(collectionId=5, resourceId=i, origin=1, order=n, isIgnored=False)
                            for n, i in enumerate(payload['resourceIds'])]; return None
        if path == '/collection/5/members/order':
            for member in self.members: member['order'] = payload['resourceIds'].index(member['resourceId'])
            return None
        if path == '/collection/5/members/13/ignored?ignored=true':
            next(m for m in self.members if m['resourceId'] == 13)['isIgnored'] = True; return None
        if path.startswith('/resource/keys?'): return self.resources
        if path == '/custom-property/ids?ids=7': return [{'id': 7, 'name': seed.CUSTOM_NAME, 'type': 1}]
        if path == '/collection/5': return self.collection
        if path == '/collection/5/memberships': return self.members
        raise AssertionError('Unexpected fixture request: ' + path)


class SeedTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.apps = {role: {'role': role, 'rid': 'osx-arm64', 'port': port, 'data': self.root / role}
                     for role, port in (('unified', 1111), ('client', 2222))}
        self.fake = OldApi(self.apps)
        self.report = {}
        self.addCleanup(patch.stopall)
        patch.object(seed.release, 'hosted').start()
        patch.object(seed, 'Api', side_effect=self.fake.factory).start()
        patch.dict('os.environ', {'RUNNER_TEMP': str(self.root)}).start()

    def run_seed(self):
        return seed.seed(self.apps, self.report, self.root / 'source-media')

    def test_real_api_request_plan_populates_nonempty_baseline_and_readback(self):
        result = self.run_seed()
        self.assertTrue(result['passed'])
        self.assertEqual([11, 12, 13], result['resourceIds'])
        self.assertEqual(set(seed.REQUIRED_COUNTS), set(result['requiredTables']))
        self.assertTrue(all(n > 0 for n in result['minimumTableRows'].values()))
        media = result['mediaFiles'][0]
        self.assertTrue(Path(media['path']).is_relative_to(self.root / 'source-media'))
        self.assertEqual(seed.FILE_BYTES, Path(media['path']).read_bytes())
        self.assertEqual(len(seed.FILE_BYTES), media['sizeBytes'])
        semantics = seed.verify_semantics(self.apps['unified'], result)
        self.assertEqual(result['baselineSemantics'], semantics)
        self.assertEqual('样本 一.txt', semantics['resources'][1]['displayName'])
        self.assertEqual(seed.NAME, semantics['childProperties']['name'])
        self.assertNotIn('apiEvidence', semantics)
        self.assertEqual(7, result['customPropertyId'])
        self.assertEqual(5, result['collectionId'])
        self.assertTrue(result['verificationApiEvidence'][0])
        materialize = [c for c in self.fake.calls if c[1].endswith('/materialize')]
        self.assertEqual(2, len(materialize))
        self.assertTrue(all(c[2]['mergeIfOccupied'] is False for c in materialize))
        placeholder = next(c for c in self.fake.calls if c[1] == '/resource/placeholder')
        self.assertIs(placeholder[2]['acquireImmediately'], False)
        property_values = {(c[2]['isCustomProperty'], c[2]['propertyId']): c[2]['value']
                           for c in self.fake.calls if c[1].endswith('/property-value')}
        self.assertEqual({(False, 27): seed.NAME, (False, 12): seed.INTRODUCTION,
                          (False, 13): '4.5', (True, 7): seed.CUSTOM_VALUE}, property_values)
        self.assertTrue(all(isinstance(value, str) for value in property_values.values()))

    def test_double_encoded_text_values_are_retained_literal_and_rejected(self):
        result = self.run_seed()
        for custom, property_id, expected in ((False, 27, seed.NAME), (False, 12, seed.INTRODUCTION),
                                              (True, 7, seed.CUSTOM_VALUE)):
            with self.subTest(custom=custom, property_id=property_id):
                payload = {'propertyId': property_id, 'isCustomProperty': custom, 'isBizValue': False,
                           'value': json.dumps(expected, ensure_ascii=False)}
                self.fake.call(self.apps['unified'], 'PUT', '/resource/12/property-value', payload)
                self.assertEqual(payload['value'], seed._property(self.fake.resources[1], 4 if custom else 2, property_id))
                with self.assertRaises(AssertionError):
                    seed.verify_semantics(self.apps['unified'], result)
                self.fake.call(self.apps['unified'], 'PUT', '/resource/12/property-value', dict(payload, value=expected))

    def test_new_program_cannot_seed_an_alleged_old_database(self):
        self.fake.version = '2.4.0-beta.400'
        with self.assertRaisesRegex(AssertionError, 'original published'):
            self.run_seed()
        self.assertFalse((self.root / 'source-media').exists())
        self.assertEqual(1, len(self.fake.calls))
        self.assertFalse(self.report['macosDataSeed']['passed'])

    def test_missing_duplicated_or_reused_resource_fails_before_materialization(self):
        for created in ([], self.fake.created[:2], [self.fake.created[0]] * 3,
                        [dict(v, created=False) for v in self.fake.created]):
            with self.subTest(created=created):
                self.fake.created = created
                with self.assertRaises(AssertionError): self.run_seed()
                self.assertFalse(self.report['macosDataSeed']['passed'])
                self.assertFalse(any(c[1].endswith('/materialize') for c in self.fake.calls))
                import shutil
                shutil.rmtree(self.root / 'source-media', ignore_errors=True)

    def test_merge_is_not_accepted_as_creation(self):
        self.fake.merge = True
        with self.assertRaisesRegex(AssertionError, 'unmerged'):
            self.run_seed()
        self.assertFalse(self.report['macosDataSeed']['passed'])

    def test_changed_data_fails_semantic_verification(self):
        result = self.run_seed()
        for mutate in (lambda: self.fake.resources[1].update(parentId=None),
                       lambda: self.fake.resources[1].update(pinned=False),
                       lambda: self.fake.resources[1].update(playedAt=None),
                       lambda: self.fake.resources[2].update(path='/unexpected'),
                       lambda: self.fake.resources[1]['properties']['4'].clear(),
                       lambda: self.fake.members[0].update(order=99),
                       lambda: self.fake.members[1].update(isIgnored=False)):
            saved = copy.deepcopy((self.fake.resources, self.fake.members))
            with self.subTest(mutate=mutate):
                mutate()
                with self.assertRaises(AssertionError): seed.verify_semantics(self.apps['unified'], result)
            self.fake.resources, self.fake.members = saved

    def test_manual_name_is_not_the_untemplated_display_name(self):
        result = self.run_seed()
        self.fake.resources[1]['displayName'] = seed.NAME
        with self.assertRaises(AssertionError):
            seed.verify_semantics(self.apps['unified'], result)

    def test_source_media_mutation_is_detected(self):
        result = self.run_seed()
        Path(result['mediaFiles'][0]['path']).write_bytes(b'damaged')
        with self.assertRaisesRegex(AssertionError, 'source media'):
            seed.verify_semantics(self.apps['unified'], result)

    def test_existing_or_outside_source_directory_is_rejected(self):
        (self.root / 'existing').mkdir()
        for path in (self.root / 'existing', self.root.parent / 'outside-media'):
            with self.subTest(path=path), self.assertRaises(AssertionError): seed._prepare_source(path)

    @unittest.skipIf(os.name == 'nt', 'POSIX symlink fixture; Windows creation may require privileges')
    def test_symlink_source_directory_is_rejected(self):
        (self.root / 'existing').mkdir()
        (self.root / 'linked').symlink_to(self.root / 'existing', target_is_directory=True)
        with self.assertRaises(AssertionError):
            seed._prepare_source(self.root / 'linked' / 'nested')

    def test_hosted_guard_precedes_api_and_file_creation(self):
        with patch.object(seed.release, 'hosted', side_effect=AssertionError('not hosted')):
            with self.assertRaisesRegex(AssertionError, 'not hosted'): self.run_seed()
        self.assertEqual([], self.fake.calls)
        self.assertFalse((self.root / 'source-media').exists())


if __name__ == '__main__':
    unittest.main()
