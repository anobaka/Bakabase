#!/usr/bin/env bash
set -euo pipefail

TAG="local"

usage() {
  echo "Usage: ./build.sh [-t|--tag <tag>]"
  echo "  -t, --tag   本地源码镜像标签 (默认: local)"
}

# 解析参数
while [[ $# -gt 0 ]]; do
  case "$1" in
    -t|--tag)
      [[ $# -ge 2 && -n "$2" ]] || { usage; exit 1; }
      TAG="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      usage
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
export BAKABASE_IMAGE="bakabase:$TAG"
exec "$SCRIPT_DIR/source.sh" build
