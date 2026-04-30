#!/usr/bin/env bash
# macOS local upgrade test. Verifies that ~/Library/Application Support/Bakabase
# survives an upgrade simulated as a Velopack-style atomic swap of current/.
#
# Usage:  ./run-macos.sh [--scenario B|C|D]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "$SCRIPT_DIR/_lib.sh"

SCENARIO="B"
while [ $# -gt 0 ]; do
  case "$1" in
    --scenario) SCENARIO="$2"; shift 2 ;;
    *) echo "Unknown arg: $1"; exit 2 ;;
  esac
done

if [ "$(uname)" != "Darwin" ]; then
  echo "This script is for macOS only. Use run-linux.sh on Linux." >&2; exit 2
fi

# Use a per-run scratch HOME so we don't pollute the developer's real
# ~/Library/Application Support/Bakabase. We point HOME at it for the duration.
RUN_ID="$(date +%Y%m%d%H%M%S)-$$"
RUN_DIR="$WORK_BASE/macos-$SCENARIO-$RUN_ID"
mkdir -p "$RUN_DIR"
SCRATCH_HOME="$RUN_DIR/home"
mkdir -p "$SCRATCH_HOME/Library/Application Support"
INSTALL_ROOT="$RUN_DIR/install"
mkdir -p "$INSTALL_ROOT/current"

VERSION_A="99.0.0-test-$(date +%H%M%S)"
VERSION_B="99.0.1-test-$(date +%H%M%S)"
PUBLISH_A="$RUN_DIR/publish-A"
PUBLISH_B="$RUN_DIR/publish-B"

trap 'echo; echo "Workspace: $RUN_DIR"' EXIT

# ── Build both versions ───────────────────────────────────────────────────────
publish_app "$VERSION_A" "$PUBLISH_A" MACOS osx-arm64
publish_app "$VERSION_B" "$PUBLISH_B" MACOS osx-arm64

# ── Stage version A as the "installed" current/ ───────────────────────────────
cp -R "$PUBLISH_A/." "$INSTALL_ROOT/current/"

# ── Decide where AppData lives based on scenario ──────────────────────────────
case "$SCENARIO" in
  B)
    APPDATA="$SCRATCH_HOME/Library/Application Support/Bakabase"
    ;;
  C)
    APPDATA="$RUN_DIR/custom-data/Bakabase"
    mkdir -p "$APPDATA"
    # Simulate the user having configured AppOptions.DataPath to this location.
    mkdir -p "$SCRATCH_HOME/Library/Application Support/Bakabase"
    cat > "$SCRATCH_HOME/Library/Application Support/Bakabase/app.json" <<EOF
{ "App": { "DataPath": "$APPDATA", "Version": "$VERSION_A", "Language": "en-US" } }
EOF
    ;;
  D)
    APPDATA="$RUN_DIR/env-mounted-data"
    mkdir -p "$APPDATA"
    export BAKABASE_DATA_DIR="$APPDATA"
    ;;
  *) echo "Unknown scenario $SCENARIO"; exit 2 ;;
esac

# ── Seed fixture ──────────────────────────────────────────────────────────────
seed_fixture "$APPDATA"
BEFORE="$RUN_DIR/manifest.before"
capture_manifest "$APPDATA" "$BEFORE"
echo "Seeded $(wc -l < "$BEFORE") files in $APPDATA"

# ── Simulate Velopack atomic swap ─────────────────────────────────────────────
simulate_velopack_swap "$INSTALL_ROOT/current" "$PUBLISH_B"

# ── Assertion: AppData unchanged ──────────────────────────────────────────────
AFTER="$RUN_DIR/manifest.after"
capture_manifest "$APPDATA" "$AFTER"
assert_manifests_equal "$BEFORE" "$AFTER"

echo
echo "=== macOS upgrade test PASS [scenario=$SCENARIO] ==="
echo "AppData kept at: $APPDATA"
echo "Install root:    $INSTALL_ROOT (current/ atomically replaced from $PUBLISH_A → $PUBLISH_B)"
