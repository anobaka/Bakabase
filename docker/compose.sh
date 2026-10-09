#!/usr/bin/env bash
# Both deployment modes deliberately share the project, service and local config.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
mode="${1:-image}"
[[ $# == 0 ]] || shift
case "$mode" in image|source) ;; *) echo 'Usage: compose.sh image|source [compose arguments...]' >&2; exit 2 ;; esac
compose=(docker compose --project-directory "$repo_root/docker")
env_file="${BAKABASE_ENV_FILE:-$repo_root/docker/.env}"
if [[ -f "$env_file" ]]; then
  compose+=(--env-file "$env_file")
elif [[ -n "${BAKABASE_ENV_FILE:-}" ]]; then
  printf 'Missing deployment environment file: %s\n' "$env_file" >&2; exit 1
fi
compose+=(-f "$repo_root/docker/compose.yaml")
if [[ "$mode" == source ]]; then
  export BAKABASE_SOURCE_DIR="$repo_root"
  compose+=(-f "$repo_root/docker/compose.source.yaml")
fi
# Set the variable to an empty string to disable the optional local override.
override="${BAKABASE_COMPOSE_OVERRIDE-$repo_root/docker/compose.local.yaml}"
if [[ -n "$override" ]]; then
  if [[ -f "$override" ]]; then
    compose+=(-f "$override")
  elif [[ -n "${BAKABASE_COMPOSE_OVERRIDE:-}" ]]; then
    printf 'Missing deployment override: %s\n' "$override" >&2; exit 1
  fi
fi
if [[ $# == 0 ]]; then set -- up -d; fi
exec "${compose[@]}" "$@"
