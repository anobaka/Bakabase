# Local development storage

This rule covers agent-created temporary files, test databases, builds, packages,
acceptance installations and evidence. It does not change Bakabase's shipped
defaults or the user's production storage configuration.

## This Mac

On Anobaka's Mac, keep large development outputs on the mounted **ssd1** volume:

| Purpose | Location |
| --- | --- |
| Per-command temporary files | `/Volumes/ssd1/Codex/tmp/bakabase` |
| Logs, packages, backups and acceptance evidence | `/Volumes/ssd1/Codex/artifacts/<task>-<date>` |
| Existing desktop acceptance installation and workspaces | `/Volumes/ssd1/BakabaseDesktopAcceptance` |
| Existing local Docker deployment data | `/Volumes/ssd1/BakabaseServer` |

Check that `/Volumes/ssd1` is mounted and the intended directories are writable
before starting. If unavailable, stop the disk-heavy operation and explain the
missing volume; do not silently fall back to the internal disk or create a
replacement mount-point directory. These paths are this machine's development
preferences, not portable application or Compose defaults.

For tests, builds, code generation and packaging, run commands through the
repository's `src/scripts/with-scratch.sh`:

```sh
./src/scripts/with-scratch.sh --root /Volumes/ssd1/Codex/tmp/bakabase -- dotnet test <project>
```

The wrapper sets `TMPDIR`, `TMP` and `TEMP` only for that command and its children,
using a private, short directory for Unix socket compatibility. It prints the
run directory and leaves it available for inspection. Put explicit report,
download, publish and archive destinations on SSD too: the environment cannot
redirect hardcoded `/tmp` paths or output paths passed to tools. Repository-local
`bin`, `obj`, `TestResults` and frontend output are fine when the checkout itself
is on SSD. Use the wrapper from the current checkout; if an older checkout lacks
it, use the wrapper accompanying this rule.

Do not create new bulky `BakabaseTest*`, `bakabase-*` or acceptance trees under
`/tmp`, `/var/folders`, `~/Downloads` or the home directory. Existing home aliases
may be retained for compatibility; verify their physical destinations before use.
Never repurpose `HOME` or `CODEX_HOME`, change the system-wide temporary directory,
or move `/private/tmp` or `/private/var/folders` as a whole.

## Lifetime and migration

- Record the task's scratch/evidence paths. After tests and their child processes
  have exited, remove unneeded generated fixtures from that task's own scratch
  directory; retain useful logs and failing evidence. Do not delete other tasks'
  artifacts, run age-only global cleanup, or assume `BakabaseTests_*` is inactive
  merely because of its name.
- Before relocating existing data, check active processes and open files. Stop
  owned application instances gracefully, copy while their data is quiescent,
  verify contents and metadata, and only then retire the original. Follow the
  [AppData lock and Profiles rules](appdata-paths.md). Do not copy ownership lock
  files. Preserve identities, pairings, mappings and profile controls.
- Keep migration manifests and recovery instructions on SSD. A compatibility
  symlink may preserve an old entry point, but must not replace a system-managed
  parent directory. Retire old data only after the destination has been verified.
  Match an installation's processes by canonical executable paths: after moving
  the acceptance installation, a coordinator may retain its old symlink path
  while its child reports the physical SSD path. Do not reuse an old update
  script that only matches the original path string.
- Temporary directories can contain credentials or copied settings. Keep scratch
  and audit directories private to the user; do not commit their contents.
- The Mac's `~/.codex/AGENTS.md` links to this shared policy for future chats.
  Before removing or archiving the referenced checkout, update that link to an
  available checkout containing this rule. Keep the policy in `.claude`; do not
  duplicate its contents in a separate Codex policy tree.

## Other machines and CI

Use a writable local scratch root appropriate to that machine; do not assume
ssd1 exists. The wrapper accepts `--root` or `BAKABASE_SCRATCH_ROOT` and requires
an existing root. Windows native tasks should scope `TEMP`/`TMP` to the command
and restore the prior environment afterwards. Never apply this Mac's filesystem
layout to Anobaka-Home, users' installations or CI defaults.
