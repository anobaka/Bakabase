#!/usr/bin/env bash
# Both deployment modes deliberately share the project, service and local config.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
mode="${1:-image}"
[[ $# == 0 ]] || shift
case "$mode" in image|source) ;; *) echo 'Usage: compose.sh image|source [compose arguments...]' >&2; exit 2 ;; esac
compose=(docker compose --project-directory "$script_dir")
env_file="${BAKABASE_ENV_FILE:-$script_dir/.env}"
if [[ -f "$env_file" ]]; then
  compose+=(--env-file "$env_file")
elif [[ -n "${BAKABASE_ENV_FILE:-}" ]]; then
  printf 'Missing deployment environment file: %s\n' "$env_file" >&2; exit 1
fi
compose+=(-f "$script_dir/compose.yaml")
if [[ "$mode" == source ]]; then
  export BAKABASE_SOURCE_DIR="$repo_root"
  compose+=(-f "$script_dir/compose.source.yaml")
fi
# Set the variable to an empty string to disable the optional local override.
override="${BAKABASE_COMPOSE_OVERRIDE-$script_dir/compose.local.yaml}"
if [[ -n "$override" ]]; then
  if [[ -f "$override" ]]; then
    compose+=(-f "$override")
  elif [[ -n "${BAKABASE_COMPOSE_OVERRIDE:-}" ]]; then
    printf 'Missing deployment override: %s\n' "$override" >&2; exit 1
  fi
fi
if [[ $# == 0 ]]; then set -- up -d; fi
# Keep user-supplied Compose options in both the config query and the real command.
# Command-specific options stay after the command (e.g. `run --env-file ...`).
while [[ $# -gt 0 && "$1" == -* ]]; do
  case "$1" in
    -f|--file|-p|--project-name|--project-directory|--env-file|--profile|--parallel|--ansi|--progress)
      [[ $# -ge 2 ]] || { printf 'Missing value for %s\n' "$1" >&2; exit 2; }
      compose+=("$1" "$2"); shift 2 ;;
    *) compose+=("$1"); shift ;;
  esac
done
case "${1:-}" in
  up|create|run)
    command -v python3 >/dev/null || {
      echo 'Starting containers through this helper requires Python 3 (standard library only) to read Compose deployment information. Install Python 3, or use docker compose directly; direct starts display container paths and do not derive host LAN addresses.' >&2
      exit 1
    }
    exec python3 "$script_dir/compose-metadata.py" "${#compose[@]}" "${compose[@]}" "$@" ;;
esac
exec "${compose[@]}" "$@"
