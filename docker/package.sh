#!/usr/bin/env bash
# Export a built image and standalone Compose config; no checkout is needed to run it.
set -euo pipefail
[[ $# -ge 1 && $# -le 2 ]] || { echo 'Usage: package.sh NEW_OUTPUT_DIRECTORY [IMAGE]' >&2; exit 2; }
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
output="$1"
image="${2:-${BAKABASE_IMAGE:-bakabase:local}}"
docker image inspect "$image" >/dev/null
mkdir -p "$(dirname "$output")"
mkdir "$output" # Refuse to replace any existing package or user directory.
cp "$script_dir/compose.yaml" "$output/compose.yaml"
cp "$script_dir/compose.local.example.yaml" "$output/compose.local.example.yaml"
cp "$script_dir/compose.sh" "$script_dir/image.sh" "$script_dir/compose-metadata.py" "$output/"
{ printf 'BAKABASE_IMAGE=%s\n' "$image"; tail -n +3 "$script_dir/.env.example"; } > "$output/.env.example"
docker image inspect "$image" --format '{{json .}}' > "$output/image.json"
docker image save --output "$output/bakabase-image.tar" "$image"
cat > "$output/README.txt" <<'EOF'
Bakabase server image package

Load once:
  docker load -i bakabase-image.tar
  cp .env.example .env
  mkdir -p "$HOME/BakabaseServer/appdata"

Review .env and any media/import mounts before starting:
  ./image.sh up -d --no-build --force-recreate
  ./image.sh logs -f server

The start helper requires Python 3 (standard library only) to derive host-path
display information and published HTTP ports from the final Compose configuration.
With a local Unix-socket Docker context on macOS/Linux, it also reads active host
LAN IPv4 addresses for sharing. It does not access docker.sock from the container.
Remote Docker contexts, loopback-only ports, and unknown host networking produce
no automatic LAN address. Linux host detection requires the `ip` command.
Direct `docker compose up -d` also works: paths remain container paths and the UI
can suggest the browser's current server address without host metadata.
Non-start helper commands do not require Python. Address metadata does not enable
LAN access or forward discovery broadcasts; network/firewall settings still apply.

The project is bakabase and its service is server (container bakabase-server-1). Keep the same .env,
AppData mount, and optional local override when replacing a source-built server.
For the optional Apple Silicon/NAS mounts, copy compose.local.example.yaml to
compose.local.yaml; image.sh reads it automatically.

An image upgrade preserves AppData. Downgrading the image does not downgrade its
database; restore a matching backup when returning to an older data version.
EOF
printf 'Image package written to %s\n' "$output"
