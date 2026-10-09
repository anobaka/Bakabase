#!/usr/bin/env python3
"""Exercise real Setup IPC, draft recovery and mapped import, using temporary libraries only."""
import argparse
import json
import os
from pathlib import Path
import shlex
import shutil
import sqlite3
import tempfile
import time

import server_import_smoke as common
from server_setup_process_smoke import Server, children, alive


def exercise(release, root):
    servers = []
    def start(name):
        server = Server(release, root / name, root / (name + '.log'))
        servers.append(server)
        return server.start().ready()

    def session(server):
        reply = common.ok(server.port, '/app/data-path/import/setup-session', 'POST')
        return {common.SETUP_HEADER: reply['setupToken']}

    def api(server, headers, path, method='GET', body=None):
        code, result = common.request(server.port, '/setup/' + path, method, body, headers)
        common.check(code == 200, f'Setup {path} returned {code}: {result}')
        return result

    try:
        source = start('source')
        source_identity = common.ok(source.port, '/app/analytics-info')['deviceId']
        source.stop()
        with sqlite3.connect(source.data / 'bakabase_insideworld.db') as db:
            db.execute('CREATE TABLE SetupPathSmoke(Id INTEGER PRIMARY KEY, Path TEXT)')
            db.executemany('INSERT INTO SetupPathSmoke VALUES(?,?)', [(1, 'Y:/Movies/a.mkv'), (2, 'Y:/Movies/b.mkv'), (3, 'Z:/Music/c.flac')])
            db.commit()
            db.execute('PRAGMA wal_checkpoint(TRUNCATE)')
        source_hashes = common.digest_tree(source.data)
        target = start('target')
        body = {'operation': 'import', 'sourcePath': str(source.data), 'targetPath': str(target.data)}
        rules = [{'sourcePrefix': 'Y:/', 'targetPrefix': '/qa-media'}]
        headers = session(target)
        draft = api(target, headers, 'draft', 'POST', {'request': body, 'rules': rules})['draft']
        common.check(draft['rules'] == rules, 'Draft did not contain mappings')
        target.stop()
        target = start('target')
        headers = session(target)
        restored = api(target, headers, 'draft')['draft']
        common.check(restored['id'] == draft['id'] and restored['rules'] == rules, 'Draft did not survive process restart')
        common.check(api(target, headers, 'preflight')['phase'] == 'idle', 'Restart restored a stale confirmation')
        parent = target.process.pid
        business = next(pid for pid, command in children(parent).items() if '--bakabase-role=business' in command)
        scan = api(target, headers, 'preflight', 'POST', body)
        deadline = time.monotonic() + 60
        while scan['phase'] == 'scanning' and time.monotonic() < deadline:
            common.check(common.ok(target.port, '/app/analytics-info') is not None, 'Preflight stopped the business service')
            time.sleep(.05)
            scan = api(target, headers, 'preflight')
        common.check(scan['phase'] == 'ready', f'Preflight failed: {scan}')
        tree = api(target, headers, 'preflight/tree?scanId=' + scan['id'])
        common.check(any(node['path'] == 'Y:/' and node['referenceCount'] == 2 for node in tree['nodes']), 'Path aggregation is missing')
        preview = api(target, headers, 'preflight/preview', 'POST', {'scanId': scan['id'], 'rules': rules})
        common.check(preview['matchedReferences'] == 2, 'Preview did not count mapped paths')
        reviewed = dict(body, pathPreflightId=scan['id'], pathPreviewId=preview['previewId'], pathMappings=rules)
        common.check(api(target, headers, 'validate', 'POST', reviewed)['valid'], 'Reviewed plan was rejected')
        committed = api(target, headers, 'apply', 'POST', reviewed)
        common.check(not committed['requiresRestart'], 'Managed import needs a manual restart')
        phases = []
        target.ready(committed['monitorToken'], phases)
        common.check(target.process.pid == parent and not alive(business), 'The coordinator did not replace its business child')
        common.check(common.ok(target.port, '/app/analytics-info')['deviceId'] == source_identity, 'Import did not retain source identity')
        common.check(not (target.data / '.bakabase-setup-draft.json').exists(), 'Completed mapping left its draft behind')
        with sqlite3.connect(f'file:{target.data / "bakabase_insideworld.db"}?mode=ro', uri=True) as db:
            values = dict(db.execute('SELECT Id,Path FROM SetupPathSmoke'))
            common.check(values == {1: '/qa-media/Movies/a.mkv', 2: '/qa-media/Movies/b.mkv', 3: 'Z:/Music/c.flac'}, 'Mapped values differ from the preview')
        common.check(common.digest_tree(source.data) == source_hashes, 'Preflight or import modified source bytes')
        new_headers = session(target)
        api(target, new_headers, 'draft', 'POST', {'request': body, 'rules': rules})
        api(target, new_headers, 'draft/clear', 'POST', {})
        common.check(api(target, new_headers, 'draft')['draft'] is None, 'Explicit draft clear failed')
        return {'passed': True, 'phases': phases, 'checks': ['live business remains available during preflight',
            'draft survives coordinator restart', 'reopening requires a fresh scan and preview', 'authenticated paged tree and preview',
            'parent survives and business PID replaced', 'mapped and unchanged paths match preview',
            'source byte hashes unchanged', 'completed mapping deletes draft', 'explicit clear deletes saved edits']}
    finally:
        for server in reversed(servers):
            server.stop(require_graceful=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--service-dll', required=True, type=Path)
    parser.add_argument('--keep-data', action='store_true')
    args = parser.parse_args()
    root = Path(tempfile.mkdtemp(prefix='bakabase-preflight-smoke-')).resolve()
    try:
        release = root / 'launcher'; release.mkdir()
        dotnet = shutil.which('dotnet')
        common.check(dotnet, 'dotnet is required')
        wrapper = release / 'bakabase-server'
        wrapper.write_text('#!/bin/sh\nexec ' + shlex.quote(dotnet) + ' ' + shlex.quote(str(args.service_dll.resolve())) + ' "$@"\n')
        wrapper.chmod(0o700)
        report = exercise(release, root)
        print(json.dumps(report, indent=2))
        if args.keep_data: print(f'Temporary evidence: {root}')
        else: shutil.rmtree(root)
    except BaseException:
        print(f'Temporary evidence retained after failure: {root}')
        raise


if __name__ == '__main__':
    main()
