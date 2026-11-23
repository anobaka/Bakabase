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
import hashlib, json, pathlib, shutil, sys, zipfile
build, sources = map(pathlib.Path, sys.argv[1:])
archive = build / 'source.zip'
if hashlib.sha256(archive.read_bytes()).hexdigest() != '6da5fbee22d3747a8dbc9e78d833f775de839c1ed77f6526642c79f4f6d189cf':
    raise SystemExit('Upstream archive checksum mismatch')
with zipfile.ZipFile(archive) as z:
    z.extractall(build / 'source')
root = next((build / 'source').iterdir())
for patch in json.loads((sources / 'upstream-patch.json').read_bytes()):
    file = (root / patch['path']).resolve()
    if root.resolve() not in file.parents:
        raise SystemExit('Unsafe upstream patch path')
    original = file.read_bytes()
    if hashlib.sha256(original).hexdigest() != patch['sha256']:
        raise SystemExit('Upstream patch input checksum mismatch')
    text = original.decode('utf-8')
    for edit in patch['edits']:
        before, after = edit['before'], edit['after']
        if not before or text.count(before) != 1:
            raise SystemExit('Upstream patch input is ambiguous')
        text = text.replace(before, after, 1)
    file.write_bytes(text.encode('utf-8'))
cmd = root / 'cmd' / 'bakabase-bridge'
cmd.mkdir(parents=True)
for file in sources.glob("*.go"):
    shutil.copyfile(file, cmd / file.name)
PY
bridge_source="$bridge_build/source/BaiduPCS-Go-225bdd3b6cb298601c4d5ef7104c3e08cd1d692d"
BAIDUPCS_GO_CONFIG_DIR="$bridge_build" go -C "$bridge_source" test -mod=readonly ./cmd/bakabase-bridge
CGO_ENABLED=0 GOOS="${BAKABASE_BRIDGE_GOOS:-$(go env GOOS)}" GOARCH="${BAKABASE_BRIDGE_GOARCH:-$(go env GOARCH)}" \
    go -C "$bridge_source" build -trimpath -mod=readonly -o "$1" ./cmd/bakabase-bridge
