#!/usr/bin/env bash
# Run the source Compose stack with the same NBGV version as this checkout.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
git_common_dir="$(git -C "$repo_root" rev-parse --path-format=absolute --git-common-dir)"
git_dir="$(git -C "$repo_root" rev-parse --absolute-git-dir)"
version_mounts=(
  --mount "type=bind,source=$repo_root,target=$repo_root,readonly"
  --mount "type=bind,source=$git_common_dir,target=$git_common_dir,readonly"
)
# A relocated checkout may still refer to the old host symlink paths in .git
# and the worktree's gitdir back-pointer. Preserve those aliases inside the
# container too; neither rewriting the checkout nor copying Git history is needed.
if [[ -f "$repo_root/.git" ]]; then
  IFS= read -r git_reference < "$repo_root/.git"
  git_dir_alias="${git_reference#gitdir: }"
  [[ "$git_dir_alias" == /* ]] || git_dir_alias="$repo_root/$git_dir_alias"
  common_alias="$git_dir_alias"
  if [[ -f "$git_dir/commondir" ]]; then
    IFS= read -r common_reference < "$git_dir/commondir"
    if [[ "$common_reference" == /* ]]; then
      common_alias="$common_reference"
    else
      common_alias="$git_dir_alias/$common_reference"
    fi
  fi
  common_alias="$(cd -L "$common_alias" && pwd -L)"
  if [[ "$common_alias" != "$git_common_dir" ]]; then
    version_mounts+=(--mount "type=bind,source=$git_common_dir,target=$common_alias,readonly")
  fi
fi
if [[ -f "$git_dir/gitdir" ]]; then
  IFS= read -r worktree_reference < "$git_dir/gitdir"
  worktree_alias="${worktree_reference%/.git}"
  if [[ "$worktree_alias" == /* && "$worktree_alias" != "$repo_root" ]]; then
    version_mounts+=(--mount "type=bind,source=$repo_root,target=$worktree_alias,readonly")
  fi
fi
version_key="$(cat "$repo_root/docker/Version.csproj" "$repo_root/docker/Dockerfile.source" | git hash-object --stdin)"
version_image="bakabase-build-version:$version_key"

if ! docker image inspect "$version_image" >/dev/null 2>&1; then
  docker build --target version-tools -f "$repo_root/docker/Dockerfile.source" \
    -t "$version_image" "$repo_root"
fi
BAKABASE_VERSION="$(docker run --rm \
  "${version_mounts[@]}" \
  "$version_image" "-p:GitVersionBaseDirectory=$repo_root")"
export BAKABASE_VERSION
if [[ ! "$BAKABASE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
  printf 'Could not resolve the source version: %s\n' "$BAKABASE_VERSION" >&2
  exit 1
fi
printf 'Source version: %s\n' "$BAKABASE_VERSION"

if [[ $# == 0 ]]; then set -- up -d --build; fi
exec "$repo_root/docker/compose.sh" source "$@"
