#!/usr/bin/env bash
# Linux local upgrade test. Verifies that the canonical Linux AppData location
# (~/.local/share/Bakabase, or $BAKABASE_DATA_DIR / $XDG_DATA_HOME/Bakabase if set)
# survives an upgrade simulated as a Velopack-style atomic swap of current/.
#
# Usage:  ./run-linux.sh [--scenario B|C|D]

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

if [ "$(uname)" != "Linux" ]; then
  echo "This script is for Linux. Use run-macos.sh / run-windows.ps1 elsewhere." >&2; exit 2
fi

RUN_ID="$(date +%Y%m%d%H%M%S)-$$"
RUN_DIR="$WORK_BASE/linux-$SCENARIO-$RUN_ID"
mkdir -p "$RUN_DIR"
SCRATCH_HOME="$RUN_DIR/home"
mkdir -p "$SCRATCH_HOME/.local/share"
INSTALL_ROOT="$RUN_DIR/install"
mkdir -p "$INSTALL_ROOT/current"

VERSION_A="99.0.0-test-$(date +%H%M%S)"
VERSION_B="99.0.1-test-$(date +%H%M%S)"
PUBLISH_A="$RUN_DIR/publish-A"
PUBLISH_B="$RUN_DIR/publish-B"

trap 'echo; echo "Workspace: $RUN_DIR"' EXIT

publish_app "$VERSION_A" "$PUBLISH_A" LINUX linux-x64
publish_app "$VERSION_B" "$PUBLISH_B" LINUX linux-x64

cp -R "$PUBLISH_A/." "$INSTALL_ROOT/current/"

case "$SCENARIO" in
  B)
    # Default: ~/.local/share/Bakabase (XDG_DATA_HOME unset).
    APPDATA="$SCRATCH_HOME/.local/share/Bakabase"
    ;;
  C)
    # User configured DataPath via app.json.
    APPDATA="$RUN_DIR/custom-data/Bakabase"
    mkdir -p "$SCRATCH_HOME/.local/share/Bakabase"
    cat > "$SCRATCH_HOME/.local/share/Bakabase/app.json" <<EOF
{ "App": { "DataPath": "$APPDATA", "Version": "$VERSION_A", "Language": "en-US" } }
EOF
    ;;
  D)
    # Docker / headless: BAKABASE_DATA_DIR overrides everything.
    APPDATA="$RUN_DIR/env-mounted-data"
    export BAKABASE_DATA_DIR="$APPDATA"
    ;;
  *) echo "Unknown scenario $SCENARIO"; exit 2 ;;
esac

mkdir -p "$APPDATA"
seed_fixture "$APPDATA"
BEFORE="$RUN_DIR/manifest.before"
capture_manifest "$APPDATA" "$BEFORE"
echo "Seeded $(wc -l < "$BEFORE") files in $APPDATA"

simulate_velopack_swap "$INSTALL_ROOT/current" "$PUBLISH_B"

AFTER="$RUN_DIR/manifest.after"
capture_manifest "$APPDATA" "$AFTER"
assert_manifests_equal "$BEFORE" "$AFTER"

echo
echo "=== Linux upgrade test PASS [scenario=$SCENARIO] ==="
echo "AppData kept at: $APPDATA"
echo "Install root:    $INSTALL_ROOT"
