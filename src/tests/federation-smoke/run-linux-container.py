#!/usr/bin/env python3
"""Run Linux release checks in an owned temporary Docker container/source clone.

Never mounts the user's checkout writable, starts/stops the Docker daemon, prunes
images, or operates on other containers. Logs survive; only this run's clone and
container are removed. A disk floor and wall-clock deadline bound the work.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

REPOSITORY = Path(__file__).resolve().parents[3]
ENTRY = r'''#!/usr/bin/env bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive DOTNET_CLI_TELEMETRY_OPTOUT=1 TESTINGPLATFORM_TELEMETRY_OPTOUT=1
apt-get update -qq
apt-get install -y --no-install-recommends python3 >/dev/null
rm -rf /var/lib/apt/lists/*
if [[ -d /nuget-ro ]]; then
  mkdir -p /root/.nuget/NuGet
  printf '%s\n' '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><fallbackPackageFolders><add key="host-readonly" value="/nuget-ro"/></fallbackPackageFolders></configuration>' > /root/.nuget/NuGet/NuGet.Config
fi
mkdir -p /results
uname -a > /results/platform.txt
dotnet --info >> /results/platform.txt
failed=0
run_check() {
  local name="$1"; shift
  printf '\n=== %s ===\n' "$name"
  if "$@" >"/results/$name.log" 2>&1; then
    printf 'PASS %s\n' "$name"
    return 0
  else
    local code=$?
    printf 'FAIL %s (exit %s)\n' "$name" "$code"
    tail -20 "/results/$name.log"
    failed=1
    return 1
  fi
}
run_check identities python3 src/tests/upgrade-tests/check-release-contract.py --report /results/product-identities.json || true
run_check guards python3 src/tests/upgrade-tests/test_release_contract.py || true
run_check compatibility python3 src/tests/upgrade-tests/run-compatibility.py --results-directory /results/compatibility || true
run_check protocol dotnet run --project src/tests/Bakabase.Modules.Federation.Tests -- --minimum-expected-tests 1 --report-trx --results-directory /results/protocol || true
run_check player dotnet run --project src/tests/Bakabase.Modules.Player.Tests -- --minimum-expected-tests 1 --report-trx --results-directory /results/player || true
if run_check host-build dotnet build src/tests/Bakabase.Federation.TestHost/Bakabase.Federation.TestHost.csproj; then
  run_check smoke python3 src/tests/federation-smoke/run.py --timeout 300 --results-directory /results/smoke || true
fi
if run_check publish dotnet publish src/apps/Bakabase.Service/Bakabase.Service.csproj -p:RuntimeMode=DOCKER --self-contained false -r "$TEST_RID" -o /work/publish-server; then
  python3 -c 'import shutil; shutil.copytree("/frontend", "/work/publish-server/web")'
  run_check package python3 src/tests/upgrade-tests/check-release-contract.py --role server --publish-dir /work/publish-server --require-web --report /results/server-package.json || true
fi
exit "$failed"
'''


def checked(command, **kwargs):
    return subprocess.run(command, check=True, text=True, capture_output=True, **kwargs).stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ref', default='HEAD', help='Committed source to check; uncommitted source edits are not copied')
    parser.add_argument('--image', default='mcr.microsoft.com/dotnet/sdk:9.0.100-noble')
    parser.add_argument('--platform', choices=('linux/amd64', 'linux/arm64'), default='linux/amd64')
    parser.add_argument('--web-dist', type=Path, default=REPOSITORY / 'src/web/dist')
    parser.add_argument('--nuget-cache', type=Path, default=Path.home() / '.nuget/packages')
    parser.add_argument('--results-directory', type=Path)
    parser.add_argument('--timeout', type=int, default=1800)
    parser.add_argument('--minimum-free-gib', type=int, default=5)
    parser.add_argument('--cpus', type=int, default=4)
    parser.add_argument('--memory', default='4g')
    parser.add_argument('--disable-hardware-intrinsics', action='store_true',
                        help='Diagnostic workaround for virtual CPU/runtime faults; recorded in evidence, never enabled by default')
    args = parser.parse_args()
    if args.timeout < 1 or args.minimum_free_gib < 1 or args.cpus < 1:
        parser.error('timeout, disk floor and cpus must be positive')
    frontend = args.web_dist.resolve()
    if not (frontend / 'index.html').is_file() or not any(frontend.rglob('*.js')):
        parser.error('--web-dist must be an actual production frontend build')
    # Read-only preflight: fail if the owner has not started their Docker daemon.
    docker_platform = checked(['docker', 'info', '--format', '{{.OSType}}/{{.Architecture}}'])
    commit = checked(['git', 'rev-parse', '--verify', args.ref + '^{commit}'], cwd=REPOSITORY)
    results = (args.results_directory or Path(tempfile.mkdtemp(prefix='bakabase-linux-results-'))).resolve()
    results.mkdir(parents=True, exist_ok=True)
    entry = results / 'entry.sh'
    entry.write_text(ENTRY, encoding='utf-8')
    name = 'bakabase-federation-check-' + uuid.uuid4().hex[:12]
    report = {'commit': commit, 'image': args.image, 'platform': args.platform,
              'dockerPlatform': docker_platform, 'cpus': args.cpus, 'memory': args.memory,
              'container': name, 'hardwareIntrinsicsDisabled': args.disable_hardware_intrinsics, 'passed': False}
    started = time.monotonic()
    try:
        with tempfile.TemporaryDirectory(prefix='bakabase-linux-source-') as temporary:
            scratch = Path(temporary)
            floor = args.minimum_free_gib * 1024 ** 3
            if shutil.disk_usage(scratch).free < floor:
                raise RuntimeError('Host disk free space is below the configured safety floor')
            source = scratch / 'source'
            checked(['git', 'clone', '--local', '--no-hardlinks', '--no-checkout', str(REPOSITORY), str(source)])
            checked(['git', 'checkout', '--detach', commit], cwd=source)
            # Materialize the exact parent-pinned submodule trees from local object
            # stores, without network clones or writable mounts of their worktrees.
            links = checked(['git', 'ls-tree', '-r', commit], cwd=REPOSITORY)
            for line in links.splitlines():
                metadata, path = line.split('\t', 1)
                mode, kind, sha = metadata.split()
                if mode != '160000':
                    continue
                archive = scratch / (sha + '.tar')
                checked(['git', 'archive', '--format=tar', '-o', str(archive), sha], cwd=REPOSITORY / path)
                destination = source / path
                destination.mkdir(parents=True, exist_ok=True)
                checked(['tar', '-xf', str(archive), '-C', str(destination)])
                archive.unlink()
            command = ['docker', 'run', '--rm', '--name', name,
                       '--label', 'bakabase.federation-verification=' + commit,
                       '--platform', args.platform, '--cpus', str(args.cpus), '--memory', args.memory,
                       '--pids-limit', '512', '--stop-timeout', '10',
                       '-v', str(source) + ':/work', '-v', str(results) + ':/results',
                       '-v', str(frontend) + ':/frontend:ro', '-w', '/work',
                       '-e', 'TEST_RID=' + ('linux-x64' if args.platform == 'linux/amd64' else 'linux-arm64')]
            if args.disable_hardware_intrinsics:
                command += ['-e', 'DOTNET_EnableHWIntrinsic=0']
            if args.nuget_cache.is_dir():
                command += ['-v', str(args.nuget_cache.resolve()) + ':/nuget-ro:ro']
            command += [args.image, 'bash', '/results/entry.sh']
            print(f'Linux checks: {args.platform}; source {commit}; evidence {results}', flush=True)
            with (results / 'run.log').open('w', encoding='utf-8') as output:
                process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT)
                try:
                    while process.poll() is None:
                        if time.monotonic() - started > args.timeout:
                            raise RuntimeError('Linux verification exceeded its overall deadline')
                        if shutil.disk_usage(scratch).free < floor:
                            raise RuntimeError('Host disk free space fell below the configured safety floor')
                        time.sleep(2)
                    report['exitCode'] = process.returncode
                    report['passed'] = process.returncode == 0
                finally:
                    # Only the unique container created by this invocation is addressed.
                    subprocess.run(['docker', 'rm', '-f', name], capture_output=True, timeout=30)
                    if process.poll() is None:
                        process.terminate()
                        process.wait(timeout=30)
            report['imageDetails'] = json.loads(checked(['docker', 'image', 'inspect', args.image]))[0]
            # Exclude verbose layer history/container defaults; retain reproducible identity.
            report['imageDetails'] = {k: report['imageDetails'].get(k) for k in ('Id', 'RepoDigests', 'Architecture', 'Os')}
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        report['error'] = str(error)
        print(report['error'], file=sys.stderr)
    finally:
        report['elapsedSeconds'] = round(time.monotonic() - started, 1)
        (results / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
