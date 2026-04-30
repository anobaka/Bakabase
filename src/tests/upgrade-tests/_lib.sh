#!/usr/bin/env bash
# Shared helpers for the macOS / Linux upgrade-test scripts.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
WORK_BASE="${REPO_ROOT}/src/tests/upgrade-tests/work"

# Cross-platform sha256
sha256() {
  if command -v sha256sum >/dev/null; then sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null; then shasum -a 256 "$1" | awk '{print $1}'
  else echo "no sha256 tool" >&2; exit 1
  fi
}

# Recursively count files and total bytes inside a dir, deterministically ordered.
# Output: <count> <total-bytes>
inventory() {
  local dir="$1"
  if [ ! -d "$dir" ]; then echo "0 0"; return; fi
  local count
  count=$(find "$dir" -type f | wc -l | awk '{print $1}')
  local bytes
  if [ "$(uname)" = "Darwin" ]; then
    bytes=$(find "$dir" -type f -exec stat -f%z {} \; | awk '{s+=$1} END{print s+0}')
  else
    bytes=$(find "$dir" -type f -exec stat -c%s {} \; | awk '{s+=$1} END{print s+0}')
  fi
  echo "$count $bytes"
}

# Build & publish the current source tree at the given version.
# Args: $1 = version (e.g. 99.0.0-test1), $2 = output dir, $3 = runtime mode (MACOS|LINUX|WINFORMS), $4 = rid
publish_app() {
  local version="$1" out="$2" mode="$3" rid="$4"
  echo "==> Publishing version=$version mode=$mode rid=$rid → $out" >&2

  rm -rf "$out"
  mkdir -p "$out"

  # Bakabase reads its assembly version via Nerdbank.GitVersioning. We override at publish-time.
  dotnet publish "$REPO_ROOT/src/apps/Bakabase/Bakabase.csproj" \
    -p:RuntimeMode="$mode" \
    -p:Version="$version" \
    -p:AssemblyVersion="$(echo "$version" | cut -d- -f1)" \
    -p:FileVersion="$(echo "$version" | cut -d- -f1)" \
    --self-contained -r "$rid" \
    -o "$out" >&2

  # The actual release pipeline drops the frontend bundle into publish/web; we don't need
  # the frontend for these filesystem-level tests, but the AppService bootstrap looks for it.
  # Fake an empty wwwroot so startup doesn't try to download.
  mkdir -p "$out/web"
  echo "<!doctype html><title>upgrade-test</title>" > "$out/web/index.html"
}

# vpk pack the published dir.
# Args: $1 = publish dir, $2 = vpk output dir, $3 = version, $4 = main exe name, $5 = mode (MACOS|WINFORMS|LINUX)
vpk_pack() {
  local publish="$1" out="$2" version="$3" main="$4" mode="$5"
  echo "==> vpk pack version=$version → $out" >&2

  rm -rf "$out"
  mkdir -p "$out"

  local plist_arg=""
  local icon_arg=""
  if [ "$mode" = "WINFORMS" ]; then
    icon_arg="--icon $publish/Assets/favicon.ico"
  elif [ "$mode" = "MACOS" ]; then
    icon_arg="--icon $publish/Assets/app.icns"
    plist_arg="--plist $publish/Info.plist"
  fi

  vpk pack \
    --packId Bakabase \
    --packVersion "$version" \
    --packDir "$publish" \
    --mainExe "$main" \
    --outputDir "$out" \
    $icon_arg \
    $plist_arg >&2
}

# Simulate Velopack's atomic swap of current/ — what an upgrade does on disk.
#   install_root/current/{whole publish output}
# After:
#   install_root/current/{publish output of newer version}
# Files OUTSIDE install_root/current/ (the canonical AppData location) are NOT
# touched; that's the property under test.
simulate_velopack_swap() {
  local install_current="$1" new_publish="$2"
  echo "==> Simulating Velopack swap: $install_current ⇐ $new_publish" >&2

  rm -rf "${install_current}.tmp"
  cp -R "$new_publish" "${install_current}.tmp"
  rm -rf "$install_current"
  mv "${install_current}.tmp" "$install_current"
}

# Seed deterministic fixture content under the AppData root.
seed_fixture() {
  local appdata="$1"
  mkdir -p "$appdata/data/covers" "$appdata/configs"

  # Two binary fixtures + one config json. Sizes / contents are arbitrary but stable.
  printf 'fixture-A\n' > "$appdata/data/covers/cover-a.bin"
  dd if=/dev/zero of="$appdata/data/covers/cover-b.bin" bs=1024 count=4 2>/dev/null
  printf '{"version":"test","key":"value"}' > "$appdata/configs/test.json"
  printf 'sentinel\n' > "$appdata/sentinel.txt"

  # The marker that simulates a real DB; integrity is byte-level here, not sqlite-level.
  dd if=/dev/urandom of="$appdata/bakabase_insideworld.db" bs=1024 count=8 2>/dev/null
}

# Capture the full inventory of an AppData dir as a sorted manifest.
# Output file: $2 (lines like "<sha256>  <relative-path>  <size>")
capture_manifest() {
  local appdata="$1" out="$2"
  : > "$out"
  if [ ! -d "$appdata" ]; then return; fi
  ( cd "$appdata" && find . -type f | sort | while read -r f; do
      local size
      if [ "$(uname)" = "Darwin" ]; then size=$(stat -f%z "$f"); else size=$(stat -c%s "$f"); fi
      local hash
      hash=$(sha256 "$f")
      printf '%s\t%s\t%d\n' "$hash" "$f" "$size" >> "$out"
    done )
}

assert_manifests_equal() {
  local before="$1" after="$2"
  if ! diff -u "$before" "$after"; then
    echo "FAIL: AppData diverged across upgrade." >&2
    return 1
  fi
  echo "OK: AppData byte-identical (manifest match)." >&2
}

mkdir -p "$WORK_BASE"
