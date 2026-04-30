#!/usr/bin/env bash
# Drives the Linux upgrade test inside an isolated Docker container.
# Reuses run-linux.sh so the test logic itself stays in one place.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

SCENARIO="${1:-B}"

IMAGE_TAG="bakabase-upgrade-test:latest"

cat <<'DOCKERFILE' > "$SCRIPT_DIR/Dockerfile.linux-test"
FROM mcr.microsoft.com/dotnet/sdk:9.0
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl git diffutils \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /work
ENTRYPOINT ["/bin/bash"]
DOCKERFILE

docker build -f "$SCRIPT_DIR/Dockerfile.linux-test" -t "$IMAGE_TAG" "$SCRIPT_DIR"

# Mount the repo read-write (the publish step writes ./src/tests/upgrade-tests/work/).
docker run --rm \
  -v "$REPO_ROOT:/work" \
  -w /work \
  "$IMAGE_TAG" \
  -lc "./src/tests/upgrade-tests/run-linux.sh --scenario $SCENARIO"
