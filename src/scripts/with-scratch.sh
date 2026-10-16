#!/usr/bin/env bash
# Run a development command with private temporary files on an explicitly chosen disk.
# This affects tools that honor TMPDIR/TMP/TEMP. It cannot relocate paths explicitly
# chosen by a command (for example /tmp/foo, build outputs or a results directory).
# Keep the run directory, including any logs the command writes there, for inspection.
# No exit trap removes files: child processes may still be using them after it exits.

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: with-scratch.sh [--root EXISTING_DIRECTORY] -- COMMAND [ARGUMENT...]

Use --root or BAKABASE_SCRATCH_ROOT to choose an existing writable directory.
There is no fallback to the system disk, and a missing root is never created.
The resolved temporary directory must be at most 50 bytes long to leave room
for .NET diagnostic and other Unix socket filenames (macOS has a 104-byte limit).

The command inherits its original working directory, input and output streams.
TMPDIR, TMP and TEMP point to a new private run directory's t/ subdirectory;
BAKABASE_SCRATCH_RUN_DIR identifies the retained run directory for reports/logs.
The command's exit status is preserved. Remove retained runs only after confirming
that no process still uses them. This wrapper supports macOS and Linux shells.

Examples (create your chosen scratch root separately):
  with-scratch.sh --root /mnt/fast/tmp -- dotnet test
  BAKABASE_SCRATCH_ROOT=/mnt/fast/tmp with-scratch.sh -- python3 smoke.py
USAGE
}

fail() {
  printf 'with-scratch: %s\n' "$1" >&2
  exit 2
}

scratch_root=${BAKABASE_SCRATCH_ROOT:-}
while (($#)); do
  case "$1" in
    --root)
      (($# >= 2)) || fail '--root requires an existing directory.'
      scratch_root=$2
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    --)
      shift
      break
      ;;
    *)
      fail 'Separate the command from wrapper options with --. Use --help for usage.'
      ;;
  esac
done

(($#)) || fail 'No command supplied. Use --help for usage.'
[[ -n "$scratch_root" ]] || fail 'Set --root or BAKABASE_SCRATCH_ROOT; no default disk is used.'
[[ -d "$scratch_root" && -w "$scratch_root" && -x "$scratch_root" ]] ||
  fail "Scratch root must already exist and be writable/searchable: $scratch_root"
scratch_root=$(cd -- "$scratch_root" && pwd -P)

# Check before creating anything. Both BSD and GNU mktemp replace these six Xs
# with six bytes. Count bytes, not characters, for the Unix sockaddr_un limit.
scratch_template="${scratch_root%/}/bb.XXXXXX"
scratch_bytes=$(printf '%s' "$scratch_template/t/" | LC_ALL=C wc -c)
((scratch_bytes <= 50)) ||
  fail "Temporary path would be $scratch_bytes bytes (maximum 50). Choose a shorter root so Unix socket filenames fit."

umask 077
scratch_run=$(mktemp -d "$scratch_template")
mkdir "$scratch_run/t"
export BAKABASE_SCRATCH_RUN_DIR="$scratch_run"
export TMPDIR="$scratch_run/t/"
export TMP="$TMPDIR"
export TEMP="$TMPDIR"
printf 'with-scratch: retained run directory: %s\n' "$scratch_run" >&2

exec "$@"
