#!/usr/bin/env bash
set -euo pipefail
# Invoke through src/scripts/with-scratch.sh on development machines.
if [[ $# -ne 1 ]]; then echo 'Usage: build.sh /absolute/output/bakabase-baidu-pcs[.exe]' >&2; exit 2; fi
case "$1" in /*) ;; *) echo 'Output must be absolute' >&2; exit 2;; esac
bridge_sources=$(cd -- "$(dirname -- "$0")" && pwd)
bridge_build=$(mktemp -d "${TMPDIR:?Set a private temporary directory}/baidu-bridge.XXXXXX")
trap 'rm -rf -- "$bridge_build"' EXIT
curl -fL 'https://codeload.github.com/qjfoidnh/BaiduPCS-Go/zip/225bdd3b6cb298601c4d5ef7104c3e08cd1d692d' -o "$bridge_build/source.zip"
python3 - "$bridge_build" "$bridge_sources" <<'PY'
import hashlib, pathlib, shutil, sys, zipfile
build, sources = map(pathlib.Path, sys.argv[1:])
archive = build / 'source.zip'
assert hashlib.sha256(archive.read_bytes()).hexdigest() == '6da5fbee22d3747a8dbc9e78d833f775de839c1ed77f6526642c79f4f6d189cf', 'Upstream archive checksum mismatch'
with zipfile.ZipFile(archive) as z:
    z.extractall(build / 'source')
root = next((build / 'source').iterdir())
cmd = root / 'cmd' / 'bakabase-bridge'
cmd.mkdir(parents=True)
for name in ('main.go', 'main_test.go'):
    shutil.copyfile(sources / name, cmd / name)
PY
bridge_source="$bridge_build/source/BaiduPCS-Go-225bdd3b6cb298601c4d5ef7104c3e08cd1d692d"
BAIDUPCS_GO_CONFIG_DIR="$bridge_build" go -C "$bridge_source" test -mod=readonly ./cmd/bakabase-bridge
CGO_ENABLED=0 go -C "$bridge_source" build -trimpath -mod=readonly -o "$1" ./cmd/bakabase-bridge
