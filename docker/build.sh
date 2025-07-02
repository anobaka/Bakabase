#!/bin/bash
set -e

TAG="latest"
DOCKERFILE="Bakabase.CI/docker/Dockerfile"

usage() {
  echo "Usage: ./build.sh [-t|--tag <tag>]"
  echo "  -t, --tag   镜像标签 (默认: latest)"
  exit 1
}

# 解析参数
while [[ $# -gt 0 ]]; do
  case "$1" in
    -t|--tag)
      TAG="$2"
      shift 2
      ;;
    -h|--help)
      usage
      ;;
    *)
      echo "Unknown option: $1"
      usage
      ;;
  esac
done

# 获取脚本所在目录
SCRIPT_DIR=$(dirname "$0")
cd "$SCRIPT_DIR/../.." || { echo "Failed to change directory"; exit 1; }

echo "Working directory: $(pwd)"
echo "------------------------------------------"
echo "Building Docker image 'bakabase:$TAG' using Dockerfile '$DOCKERFILE'..."

docker build --progress=plain -f "$DOCKERFILE" -t bakabase:$TAG .
if [ $? -ne 0 ]; then
    echo "Docker build failed"
    exit 1
fi
echo "Docker image 'bakabase:$TAG' built successfully"