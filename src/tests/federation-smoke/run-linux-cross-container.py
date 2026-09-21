#!/usr/bin/env python3
"""Cross-build a committed revision locally, then execute real Linux x64 checks.

Requires a macOS/Linux build host with .NET 9 SDK and an already pulled
linux/amd64 ASP.NET 9 image.
No SDK is installed in the container. Only a disposable source clone is built;
container mounts of source/artifacts are read-only. This is not a substitute for
native Linux CI when Docker runs x64 through emulation on an ARM host.
"""
import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid

REPOSITORY = Path(__file__).resolve().parents[3]
CLASSES = (
    'Bakabase.Tests.AppDataProfileTests',
    'Bakabase.Tests.DefaultAppDataPathResolverTests',
    'Bakabase.Tests.AppDataPathRelocationTests',
    'Bakabase.Tests.DataPathValidatorTests',
    'Bakabase.Tests.LegacyInstallDetectorTests',
    'Bakabase.Tests.Relocation.PendingRelocationRunnerTests',
    'Bakabase.Tests.RemoteAccess.ClientUpdateSourceTests',
    'Bakabase.Tests.RemoteAccess.ClientPipelineTests',
)
CONTAINER_CHECKS = r'''import json, os, pathlib, shutil, subprocess, tempfile, time, xml.etree.ElementTree as ET
results = pathlib.Path('/results')
config = json.loads((results / 'container-config.json').read_text())
report = {'stages': [], 'passed': False}
def check(name, command, limit, environment=None):
    started = time.monotonic()
    item = {'name': name, 'passed': False}
    with (results / (name + '.log')).open('w') as log:
        try:
            run = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT,
                                 timeout=limit, env=environment)
            item['exitCode'] = run.returncode
            item['passed'] = run.returncode == 0
        except subprocess.TimeoutExpired:
            item['error'] = 'Stage deadline exceeded'
    item['elapsedSeconds'] = round(time.monotonic() - started, 2)
    report['stages'].append(item)
    (results / 'container-result.json').write_text(json.dumps(report, indent=2))
    print(('PASS ' if item['passed'] else 'FAIL ') + name, flush=True)
    return item['passed']
if config['smoke']:
    check('smoke', ['python3', '/work/src/tests/federation-smoke/run.py', '--timeout', '300',
                    '--results-directory', '/results/smoke'], 330)
# Legacy Debug AppService initialization writes beside the test assembly. Keep
# host mounts read-only and make disposable container-local execution copies.
test_copy = tempfile.TemporaryDirectory(prefix='bakabase-test-outputs-')
writable_tests = pathlib.Path(test_copy.name)
if config['player']:
    shutil.copytree('/artifacts/player', writable_tests / 'player')
    check('player', ['dotnet', str(writable_tests / 'player/Bakabase.Modules.Player.Tests.dll'),
                    '--minimum-expected-tests', '1', '--report-trx',
                    '--results-directory', '/results/player'], 180)
if config['compatibility']:
    shutil.copytree('/work/src/tests/Bakabase.Tests/bin/Debug/net9.0/linux-x64', writable_tests / 'compatibility')
    for name in config['classes']:
        with tempfile.TemporaryDirectory(prefix='bakabase-compat-') as scratch:
            environment = dict(os.environ, TMPDIR=scratch, TMP=scratch, TEMP=scratch)
            check(name, ['dotnet', str(writable_tests / 'compatibility/Bakabase.Tests.dll'),
                         '--filter', 'FullyQualifiedName~' + name + '.',
                         '--minimum-expected-tests', '1', '--report-trx',
                         '--results-directory', '/results/compatibility/' + name], 180, environment)
report['testCounts'] = {}
for kind in ('player', 'compatibility'):
    counts = {'passed': 0, 'failed': 0, 'notExecuted': 0, 'total': 0}
    for path in (results / kind).rglob('*.trx'):
        counters = ET.parse(path).find('.//{*}Counters')
        if counters is not None:
            for key in counts:
                counts[key] += int(counters.get(key, '0'))
    report['testCounts'][kind] = counts
test_copy.cleanup()
report['passed'] = all(item['passed'] for item in report['stages'])
(results / 'container-result.json').write_text(json.dumps(report, indent=2))
raise SystemExit(0 if report['passed'] else 1)
'''
CONTAINER_ENTRY = r'''#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive DOTNET_CLI_TELEMETRY_OPTOUT=1 TESTINGPLATFORM_TELEMETRY_OPTOUT=1
uname -a >/results/platform.txt
dotnet --info >>/results/platform.txt
cat /proc/cpuinfo >>/results/platform.txt
apt-get update -qq
apt-get install -y --no-install-recommends python3 >/results/container-dependencies.log 2>&1
rm -rf /var/lib/apt/lists/*
exec python3 /results/container-checks.py
'''


def checked(command, **kwargs):
    kwargs.setdefault('timeout', 30)
    return subprocess.check_output(command, text=True, **kwargs).strip()



def check_budget(started, timeout, floor, paths):
    if time.monotonic() - started > timeout:
        raise RuntimeError('Overall deadline exceeded')
    for path in paths:
        if shutil.disk_usage(path).free < floor:
            raise RuntimeError(f'Host disk free space below safety floor: {path}')


def stop_process_group(process):
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        pass
    finally:
        # The leader can exit before descendants that ignore SIGTERM. Address
        # the owned process group even after wait() observes the leader exit.
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait(timeout=10)


def remove_owned_containers(names):
    try:
        result = subprocess.run(['docker', 'rm', '-f', *names], capture_output=True, text=True, timeout=30)
        # --rm already removes completed containers. Other errors remain visible.
        errors = [line for line in result.stderr.splitlines() if 'No such container' not in line]
        return errors if result.returncode and errors else []
    except (OSError, subprocess.SubprocessError) as error:
        return [str(error)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ref', default='HEAD')
    parser.add_argument('--dotnet', default='dotnet', help='Local build SDK executable')
    parser.add_argument('--image', default='mcr.microsoft.com/dotnet/aspnet:9.0-noble')
    parser.add_argument('--results-directory', type=Path)
    parser.add_argument('--with-player', action='store_true')
    parser.add_argument('--skip-smoke', action='store_true', help='Run only the explicitly selected optional test suites')
    parser.add_argument('--with-compatibility', action='store_true')
    parser.add_argument('--timeout', type=int, default=1200, help='Overall build plus container deadline')
    parser.add_argument('--minimum-free-gib', type=int, default=5)
    args = parser.parse_args()
    if args.skip_smoke and not (args.with_player or args.with_compatibility):
        parser.error('--skip-smoke requires --with-player or --with-compatibility')
    if os.name != 'posix':
        parser.error('Cross-build runner requires a macOS or Linux build host')
    if args.timeout < 1 or args.minimum_free_gib < 1:
        parser.error('timeout and disk floor must be positive')
    # The caller chooses/pulls an official runtime image explicitly. Never download
    # an SDK (or implicitly substitute an architecture) on their behalf.
    metadata = json.loads(checked(['docker', 'image', 'inspect', '--platform', 'linux/amd64', args.image]))[0]
    if metadata['Os'] != 'linux' or metadata['Architecture'] != 'amd64':
        parser.error('--image must already exist as a linux/amd64 ASP.NET 9 image')
    docker_platform = checked(['docker', 'info', '--format', '{{.OSType}}/{{.Architecture}}'])
    commit = checked(['git', 'rev-parse', '--verify', args.ref + '^{commit}'], cwd=REPOSITORY)
    results = (args.results_directory or Path(tempfile.mkdtemp(prefix='bakabase-linux-x64-results-'))).resolve()
    results.mkdir(parents=True, exist_ok=True)
    # Reusing old results could count stale TRX files as current successes.
    if any((results / item).exists() for item in ('result.json', 'container-result.json', 'player', 'compatibility', 'smoke')):
        parser.error('--results-directory must not contain previous run results')
    # Containerd may expose a platform config ID via inspect that is not a
    # runnable image alias. Pin the manifest-list digest plus --platform instead.
    if not metadata.get('RepoDigests'):
        parser.error('--image must be a pulled runtime image with an immutable RepoDigest')
    image_reference = metadata['RepoDigests'][0]
    report = {'commit': commit, 'buildPlatform': platform.platform(), 'executionPlatform': 'linux/amd64',
              'dockerPlatform': docker_platform, 'emulated': docker_platform not in ('linux/x86_64', 'linux/amd64'),
              'image': args.image, 'imageReference': image_reference, 'imageId': metadata['Id'], 'imageRepoDigests': metadata.get('RepoDigests', []),
              'selectedChecks': {'smoke': not args.skip_smoke, 'player': args.with_player, 'compatibility': args.with_compatibility},
              'passed': False, 'stages': []}
    started = time.monotonic()
    name = 'bakabase-federation-cross-' + uuid.uuid4().hex[:12]
    environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', MSBUILDDISABLENODEREUSE='1')
    floor = args.minimum_free_gib * 1024 ** 3
    budget_paths = [results, Path(tempfile.gettempdir())]

    def budget():
        check_budget(started, args.timeout, floor, budget_paths)

    def checked_local(command, **kwargs):
        budget()
        kwargs['timeout'] = max(0.001, min(30, args.timeout - (time.monotonic() - started)))
        value = checked(command, **kwargs)
        budget()
        return value

    def cleanup(names):
        errors = remove_owned_containers(names)
        if errors:
            report.setdefault('cleanupErrors', []).extend(errors)
            report['passed'] = False

    def bounded(label, command, cwd=None):
        budget()
        item = {'name': label, 'passed': False}
        stage_start = time.monotonic()
        with (results / (label + '.log')).open('w') as output:
            process = subprocess.Popen(command, cwd=cwd, stdout=output, stderr=subprocess.STDOUT,
                                       env=environment, start_new_session=True)
            try:
                while process.poll() is None:
                    budget()
                    time.sleep(0.5)
                budget()
                item['exitCode'] = process.returncode
                item['passed'] = process.returncode == 0
            finally:
                stop_process_group(process)
                item['elapsedSeconds'] = round(time.monotonic() - stage_start, 2)
                report['stages'].append(item)
        if not item['passed']:
            raise RuntimeError(f'{label} failed; see {results / (label + ".log")}')
        print('PASS ' + label, flush=True)

    try:
        budget()
        bounded('runtime-preflight', ['docker', 'run', '--rm', '--pull', 'never',
                '--name', name + '-preflight', '--label', 'bakabase.federation-verification=' + commit,
                '--platform', 'linux/amd64', '--cpus', '1', '--memory', '512m',
                image_reference, 'dotnet', '--info'])
        if 'Microsoft.AspNetCore.App 9.' not in (results / 'runtime-preflight.log').read_text():
            raise RuntimeError('Selected image does not contain the required ASP.NET 9 runtime')
        with tempfile.TemporaryDirectory(prefix='bakabase-linux-x64-source-') as temporary:
            scratch = Path(temporary)
            source = scratch / 'source'
            artifacts = scratch / 'artifacts'
            artifacts.mkdir()
            bounded('clone', ['git', 'clone', '--local', '--no-hardlinks', '--no-checkout', str(REPOSITORY), str(source)])
            checked_local(['git', 'checkout', '--detach', commit], cwd=source, stderr=subprocess.DEVNULL)
            for line in checked_local(['git', 'ls-tree', '-r', commit], cwd=REPOSITORY).splitlines():
                metadata_line, path = line.split('\t', 1)
                mode, kind, sha = metadata_line.split()
                if mode != '160000':
                    continue
                archive = scratch / (sha + '.tar')
                checked_local(['git', 'archive', '-o', str(archive), sha], cwd=REPOSITORY / path)
                (source / path).mkdir(parents=True, exist_ok=True)
                checked_local(['tar', '-xf', str(archive), '-C', str(source / path)])
                archive.unlink()
            flags = ['--configuration', 'Debug', '--self-contained', 'false', '-r', 'linux-x64',
                     '-p:RuntimeMode=DOCKER', '-p:UseSharedCompilation=false', '-m:1', '-nr:false', '--nologo']
            host_project = 'src/tests/Bakabase.Federation.TestHost/Bakabase.Federation.TestHost.csproj'
            if not args.skip_smoke:
                bounded('host-cross-publish', [args.dotnet, 'publish', host_project, *flags,
                                             '-o', str(artifacts / 'host')], source)
            if args.with_player:
                bounded('player-cross-publish', [args.dotnet, 'publish',
                        'src/tests/Bakabase.Modules.Player.Tests/Bakabase.Modules.Player.Tests.csproj',
                        *flags, '-o', str(artifacts / 'player')], source)
            if args.with_compatibility:
                # Build outputs are executable by MSTest directly. Publishing this
                # test graph duplicates Service apphost files (NETSDK1152).
                bounded('compatibility-cross-build', [args.dotnet, 'build',
                        'src/tests/Bakabase.Tests/Bakabase.Tests.csproj', *flags], source)
            (results / 'container-entry.sh').write_text(CONTAINER_ENTRY)
            (results / 'container-checks.py').write_text(CONTAINER_CHECKS)
            (results / 'container-config.json').write_text(json.dumps({
                'smoke': not args.skip_smoke, 'player': args.with_player, 'compatibility': args.with_compatibility, 'classes': CLASSES}))
            # run.py intentionally retains its ordinary build-output convention;
            # mount the cross-published output there without modifying that script.
            host_mount = '/work/src/tests/Bakabase.Federation.TestHost/bin/Debug/net9.0'
            (source / host_mount.removeprefix('/work/')).mkdir(parents=True, exist_ok=True)
            command = ['docker', 'run', '--rm', '--pull', 'never', '--name', name,
                       '--label', 'bakabase.federation-verification=' + commit,
                       '--platform', 'linux/amd64', '--cpus', '4', '--memory', '4g', '--pids-limit', '512',
                       '--stop-timeout', '10', '-v', str(source) + ':/work:ro',
                       '-v', str(artifacts) + ':/artifacts:ro', '-v', str(results) + ':/results',
                       '-w', '/work']
            if not args.skip_smoke:
                command += ['-v', str(artifacts / 'host') + ':' + host_mount + ':ro']
            command += [image_reference, 'bash', '/results/container-entry.sh']
            try:
                bounded('container-run', command)
            finally:
                cleanup([name])
            report['containerChecks'] = json.loads((results / 'container-result.json').read_text())
            report['passed'] = report['containerChecks']['passed'] and not report.get('cleanupErrors')
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        report['error'] = str(error)
        print(str(error), file=sys.stderr)
        if (results / 'container-result.json').is_file():
            report['containerChecks'] = json.loads((results / 'container-result.json').read_text())
    finally:
        # Address only this invocation's unique container; never prune shared images.
        cleanup([name, name + '-preflight'])
        report['elapsedSeconds'] = round(time.monotonic() - started, 2)
        (results / 'result.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
