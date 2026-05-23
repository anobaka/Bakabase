#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# Installs the toolchain this repo needs so builds/tests/linters work in a
# fresh, ephemeral cloud container:
#   1. .NET 9 SDK   (global.json pins 9.0.x; the base image ships .NET 10 only)
#   2. NuGet packages for the C# solution
#   3. Frontend dependencies (Yarn 4 via Corepack) for src/web
#
# Network note: this environment's network policy blocks Microsoft's dotnet
# CDN (dot.net / builds.dotnet.microsoft.com) and repo.yarnpkg.com, but allows
# archive.ubuntu.com, api.nuget.org and registry.npmjs.org. Every step below is
# written around those constraints.
set -euo pipefail

# Only meaningful in the remote (web) environment — local dev machines already
# have their own toolchain installed.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$PROJECT_DIR"

log() { echo "[session-start] $*"; }

# --- .NET 9 SDK: install from the Ubuntu package pool ------------------------
# global.json requires a 9.0.x SDK. Microsoft's CDN is blocked, so install from
# archive.ubuntu.com. Ubuntu 24.04 never carried .NET 9 itself, so we pull the
# .debs built for a newer Ubuntu and install with --force-depends (they declare
# libssl >= 3.3 / libicu76; .NET 9 runs fine on 24.04's libssl 3.0 / libicu74,
# except for one library handled by patch_dotnet9_crypto below).
install_dotnet9_sdk() {
  log "Installing .NET 9 SDK from the Ubuntu pool..."
  local pool="http://archive.ubuntu.com/ubuntu/pool/universe/d/dotnet9"
  local deb_dir="/tmp/dotnet9-debs"
  rm -rf "$deb_dir" && mkdir -p "$deb_dir"

  # The `dotnet9` metapackage filename encodes both versions:
  #   dotnet9_<sdk>-<runtime>-<ubunturev>_amd64.deb
  local listing meta ver sdk_ver rt_ver u_rev
  listing=$(curl -fsSL --retry 4 --retry-delay 2 "$pool/")
  meta=$(echo "$listing" | grep -oE 'dotnet9_[0-9][^"]*_amd64\.deb' | sort -V | tail -1)
  [ -n "$meta" ] || { log "ERROR: no dotnet9 package in the Ubuntu pool."; exit 1; }

  ver=${meta#dotnet9_}; ver=${ver%_amd64.deb}
  sdk_ver=$(echo "$ver" | cut -d- -f1)
  rt_ver=$(echo "$ver" | cut -d- -f2)
  u_rev=$(echo "$ver" | cut -d- -f3-)
  log "Selected .NET SDK $sdk_ver (runtime $rt_ver, $u_rev)."

  local debs="" p
  for p in dotnet-sdk-9.0 dotnet-templates-9.0 netstandard-targeting-pack-2.1-9.0; do
    debs="$debs ${p}_${sdk_ver}-${u_rev}_amd64.deb"
  done
  for p in dotnet-host-9.0 dotnet-hostfxr-9.0 dotnet-runtime-9.0 \
           aspnetcore-runtime-9.0 dotnet-targeting-pack-9.0 \
           aspnetcore-targeting-pack-9.0 dotnet-apphost-pack-9.0; do
    debs="$debs ${p}_${rt_ver}-${u_rev}_amd64.deb"
  done

  local f
  for f in $debs; do
    curl -fsSL --retry 4 --retry-delay 2 --max-time 300 -o "$deb_dir/$f" "$pool/$f"
  done

  dpkg -i --force-depends "$deb_dir"/*.deb > /tmp/session-start-dpkg.log 2>&1 || true
  rm -rf "$deb_dir"

  dotnet --list-sdks 2>/dev/null | grep -qE '^9\.' \
    || { log "ERROR: .NET 9 SDK install failed — see /tmp/session-start-dpkg.log"; exit 1; }
  log ".NET 9 SDK installed."
}

# --- .NET 9 crypto library: replace with Microsoft's portable build ---------
# The Ubuntu .NET 9 build links libSystem.Security.Cryptography.Native.OpenSsl
# directly against OpenSSL >= 3.3, which Ubuntu 24.04 (OpenSSL 3.0) lacks — any
# crypto (NuGet hashing, build, test) then crashes. Microsoft's official build
# of that one library dlopen's OpenSSL at runtime and works on 3.0, so swap it
# in from the Microsoft.NETCore.App runtime NuGet package. Idempotent.
patch_dotnet9_crypto() {
  local rt_dir shim rt_ver nupkg
  rt_dir=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/9.* 2>/dev/null | sort -V | tail -1 || true)
  [ -n "$rt_dir" ] || { log "ERROR: .NET 9 runtime not found."; exit 1; }
  shim="$rt_dir/libSystem.Security.Cryptography.Native.OpenSsl.so"

  ldd "$shim" 2>/dev/null | grep -q 'not found' || return 0  # already good

  rt_ver=$(basename "$rt_dir")
  log "Patching .NET 9 crypto library (Ubuntu build needs a newer OpenSSL)..."
  nupkg="/tmp/msnetcore-runtime.nupkg"
  curl -fsSL --retry 4 --retry-delay 2 --max-time 300 -o "$nupkg" \
    "https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.linux-x64/${rt_ver}/microsoft.netcore.app.runtime.linux-x64.${rt_ver}.nupkg"
  rm -rf /tmp/msnetcore && mkdir -p /tmp/msnetcore
  unzip -o -q "$nupkg" \
    'runtimes/linux-x64/native/libSystem.Security.Cryptography.Native.OpenSsl.so' \
    -d /tmp/msnetcore
  cp /tmp/msnetcore/runtimes/linux-x64/native/libSystem.Security.Cryptography.Native.OpenSsl.so "$shim"
  rm -rf /tmp/msnetcore "$nupkg"

  ldd "$shim" 2>/dev/null | grep -q 'not found' \
    && { log "ERROR: crypto library still broken after patch."; exit 1; }
  log "Crypto library patched (Microsoft portable build $rt_ver)."
}

# --- 1. .NET 9 SDK ----------------------------------------------------------
if dotnet --list-sdks 2>/dev/null | grep -qE '^9\.'; then
  log ".NET 9 SDK already present — skipping install."
else
  install_dotnet9_sdk
fi
patch_dotnet9_crypto

# --- 2. NuGet restore -------------------------------------------------------
log "Restoring NuGet packages for src/Bakabase.sln..."
if dotnet restore src/Bakabase.sln > /tmp/session-start-restore.log 2>&1; then
  log "NuGet restore complete ($(dotnet --version))."
else
  log "ERROR: dotnet restore failed — see /tmp/session-start-restore.log"
  tail -20 /tmp/session-start-restore.log
  exit 1
fi

# --- 3. Frontend dependencies (Yarn 4 via Corepack) -------------------------
# package.json pins yarn@4.9.2. Corepack fetches Yarn from repo.yarnpkg.com by
# default (blocked here), so point it at the npm registry instead.
export COREPACK_ENABLE_DOWNLOAD_PROMPT=0
export COREPACK_NPM_REGISTRY=https://registry.npmjs.org
log "Installing frontend dependencies (Yarn 4 via Corepack)..."
corepack enable
if (cd "$PROJECT_DIR/src/web" && yarn install --immutable) > /tmp/session-start-yarn.log 2>&1; then
  log "Frontend dependencies installed."
else
  log "ERROR: yarn install failed — see /tmp/session-start-yarn.log"
  tail -20 /tmp/session-start-yarn.log
  exit 1
fi

# --- 4. Persist env vars for the rest of the session ------------------------
# So `yarn` invoked later (by Claude or scripts) keeps working.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export COREPACK_ENABLE_DOWNLOAD_PROMPT=0'
    echo 'export COREPACK_NPM_REGISTRY=https://registry.npmjs.org'
  } >> "$CLAUDE_ENV_FILE"
fi

log "Environment ready."
exit 0
