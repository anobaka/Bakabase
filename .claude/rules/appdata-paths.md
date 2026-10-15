---
paths:
  - "**/Bakabase.Infrastructures/Components/App/**"
  - "**/abstractions/**/FileSystem/**"
  - "**/Migrations/V230/**"
  - "**/legacy/**"
  - "**/modules/**"
  - "**/Bakabase.Service/**"
  - "**/apps/Bakabase.App/**"
  - "**/apps/Bakabase.Shell/**"
---

# AppData Path Rules

## The invariant

User data — DB, configs, covers, caches, anything written at runtime — must
live at `AppService.AppDataDirectory`. **Never** under the application's
install directory.

Why: on Windows the installer is Velopack, which:
- On every **upgrade**, atomically replaces `{install_root}/current/` with
  the new release. Any user file inside `current/` is destroyed.
- On **uninstall** or installer **Repair**, removes the entire install root
  wholesale. Any user file under `{install_root}/` is destroyed.

Issue #1070 is the production incident the upgrade variant caused. To make
both paths safe by construction, the Windows default AppData directory is
**`%LocalAppData%\Bakabase.AppData`** — distinct from the install root
(`%LocalAppData%\Bakabase`) so neither upgrade nor uninstall/repair can
touch it. macOS and Linux use platform-conventional locations under
`~/Library/Application Support/Bakabase` and `$XDG_DATA_HOME/Bakabase`
respectively, both already outside any install tree.

## Do

- Read user-data paths from `AppService.AppDataDirectory` (or the
  resolver / `AppDataPaths` helpers it composes).
- Persist relative paths in the database. `IAppDataPathRelocator` rebases
  them at read time so the user can move their data dir without breaking
  references.
- For env-var override (`BAKABASE_DATA_DIR`) or user-configured DataPath,
  trust `DefaultAppDataPathResolver` — don't re-implement the precedence.
- For "which directory is the data directory" before DI exists, call
  `AppDataLocator.ResolveEffectiveDataDirectory` — the one rule the database
  (`AppStartup`), `app.json` (`AppOptionsManager`), `AppService`, the log file,
  the port memory and the single-instance guard all share. The env var names
  the **anchor**; a `.redirect` inside it is followed like any other. Never
  special-case the env var on one side only: when the guard stopped at the
  variable's directory while the database followed the redirect, two
  processes ended up on one database.

## Don't

- Don't compute paths from `AppContext.BaseDirectory`,
  `Assembly.GetExecutingAssembly().Location`, `Environment.CurrentDirectory`,
  or anything else that ends up under `{install_root}/current/`. These
  resolve into the install tree on Windows.
- Don't hardcode the segment `current` in any path. If you need to detect
  it (e.g. `LegacyInstallAppDataDetector`), use the existing helpers in
  `Bakabase.Abstractions.Components.FileSystem` — do not add new ones.
- Don't store absolute paths in the database for files that live under
  AppData. Store them relative; let the relocator resolve them.
- Don't introduce a new "config dir" that bypasses `AppOptionsManager`. The
  anchor (`app.json` location) is the platform default; everything else
  follows from there.

## If you're tempted

Common shapes of this bug:

```csharp
// WRONG — lands inside current/ on Windows
var path = Path.Combine(AppContext.BaseDirectory, "data", "covers");

// WRONG — same root, different surface
var asm = Assembly.GetExecutingAssembly().Location;
var path = Path.Combine(Path.GetDirectoryName(asm)!, "userdata");

// WRONG — hardcoded current
var path = Path.Combine(installRoot, "current", "AppData");

// RIGHT
var path = Path.Combine(_appService.AppDataDirectory, "data", "covers");
```

If you genuinely need a path inside the install tree (e.g. reading a
read-only asset shipped with the app), that's fine — but don't write to it.

## One instance per data directory

A data directory has exactly one running owner. The guard
(`SingleInstanceGuard`) keys on the **effective** directory — after
`BAKABASE_DATA_DIR` and the anchor redirect, normalised (full path, links
resolved, case-folded on Windows/macOS) — never on the executable or a fixed
name, so a second launch on the same directory hands off to the running
window and exits, while a launch on a different directory is its own
instance.

- The entry point takes the lock **before** `AppService` is touched: nothing
  may create, migrate, log to or relocate the directory first. A refused
  launch must leave no file behind.
- The lock is `{dataDir}/.bakabase.lock`, held open exclusively for the
  process lifetime (`FileShare.None`: share mode on Windows, `flock` on
  Unix). The OS drops it when the process dies — there is no stale-lock
  logic, and there must never be any.
- Anything that copies, backs up or deletes the whole data directory must
  skip that file (it cannot even be read while held). The relocation runner
  and the startup backup already do.
- A relocation holds both the source's and the target's locks until the
  source is emptied; see the `SingleInstanceGuard` remarks.
- The activation channel (pipe / Unix socket) is named from a hash of the
  normalised directory and the user; see `ActivationChannel`. On macOS/Linux
  the socket is an absolute path in a per-user directory that does not come
  from `TMPDIR` (`DARWIN_USER_TEMP_DIR`; `/run/user/{uid}`, else a private
  `/tmp/bakabase-{uid}`), so a launch from ssh, a script or an IDE with its
  own `TMPDIR` still reaches the running window.

`{dataDir}/listening-ports.json` remembers the automatic listening ports so
the main window's origin (and its browser storage) survives a restart; see
`ListeningPortSelector`. It keeps the directory's **preferred** ports (the
first ones it got, never overwritten) apart from the **last used** ones, so a
port another program holds for one launch comes back on the next.

## Where the mechanism lives

| Concern | File |
|---|---|
| Platform default + env var resolution | `Bakabase.Infrastructures/Components/App/DefaultAppDataPathResolver.cs` |
| Glue (resolver + AppOptions + env) | `Bakabase.Infrastructures/Components/App/AppService.cs` |
| Path rebasing across roots | `Bakabase.Abstractions/Components/FileSystem/AppDataPathRelocation.cs` |
| User-driven DataPath change → copy + commit | `Bakabase.Infrastructures/Components/App/Relocation/PendingRelocationRunner.cs` |
| Validation of user-chosen DataPath | `Bakabase.Infrastructures/Components/App/Relocation/DataPathValidator.cs` |
| Legacy `current/AppData` notice | `Bakabase.Infrastructures/Components/App/LegacyInstallAppDataDetector.cs` |
| The one effective-dir rule, without side effects (guard, DB, `app.json`, `AppService`) | `Bakabase.Infrastructures/Components/App/AppDataLocator.cs` |
| One instance per data dir (lock, channel, relocation) | `Bakabase.Infrastructures/Components/App/SingleInstance/` |
| Stable automatic listening ports | `Bakabase.Infrastructures/Components/App/Ports/` |
| One-shot DB path conversion (absolute → relative) | `legacy/Bakabase.Migrations/V230/PathsRelocationMigrator.cs` |

## Tests

Behavior is locked down in `src/tests/Bakabase.Tests/`:
`DefaultAppDataPathResolverTests`, `AppDataPathRelocationTests`,
`DataPathValidatorTests`, `LegacyInstallDetectorTests`, `AppDataLocatorTests`,
`Relocation/PendingRelocationRunnerTests`, `SingleInstance/*`,
`ListeningPorts/*`. Add a unit test there before changing any of the files
above. The single-instance tests start a second process
(`src/tests/Bakabase.Tests.InstanceProbe`), because a lock taken twice by one
process proves nothing about two.

The end-to-end scripts in [`src/tests/upgrade-tests/`](../../src/tests/upgrade-tests/)
are a manual regression guard for this rule — run them before a release if
you've touched anything in this list.

## Desktop data spaces (Profiles)

The desktop app resolves an optional `--profile=<registered ID>` before anything
reads `AppDataAnchor`. A normal launch uses the catalog's default profile. An
existing `BAKABASE_DATA_DIR` launcher retains its prior semantics; opening a
registered profile explicitly removes that inherited override. Do not set the
environment variable to select profiles: Setup would treat the path as fixed.

`DesktopProfileRegistry` stores launch control in the platform default anchor's
**sibling** `<default-anchor>.profiles`, independent of environment overrides and
all importable libraries. It is not an options/configuration directory. Its
`profiles.json` is atomically written under an exclusive cross-process lock;
Unix directories/files are private to the user. Named profile anchors live in
`anchors/<id>` and use the existing `.redirect` mechanism. Never import or move
that catalog as part of a library. Setup rejects targets containing these
controls or another registered space. The historical default anchor is not
moved or rewritten merely by enabling Profiles.

Registering an existing directory does not import, copy or reset its identity.
A new space requires an empty directory and runs the same first-launch Setup;
only initialization creates its new identity. Removing a registration never
deletes its data or browser state. Dedupe by `DataDirectoryIdentity`, not display
path; the existing effective-directory lock remains the final ownership guard.
Profile switching launches/activates another coordinator and closes the current
space only after the target's activation channel is ready. It never swaps a
running host's database root. Restarts, Setup children and updater relaunches
must preserve the selected profile ID; desktop URL schemes cannot supply IDs,
paths or arbitrary actions and use the configured default space.

Only desktop coordinators register an installation lease. Upgrading refuses if
another coordinator from the **same installation** remains open, and reserves
its launch gate while the updating child winds down. A durable update ticket
then tracks the updater by PID plus process start time, so the gap after the
child/coordinator exits remains blocked. A changed loaded build clears the
ticket; a failed update clears only after its owner, coordinator and updater
have all exited. A spawn-before-PID-write crash falls back to detecting that
installation’s exact updater executable. Do not count business or
worker children as separate user instances, and never terminate another space
or a different installation to perform an update.

Windows WebView2 and Linux WebKit use each space's anchor for browser state.
macOS 14+ uses WKWebsiteDataStore's public identifier API; older macOS versions
use a nonpersistent store for named spaces so credentials cannot leak between
spaces. The unchanged default space retains its historical browser store.

### Profile and federation acceptance workflow

Prefer two or more fresh, isolated **local profiles** for the first multi-device
acceptance pass. Each initialized profile is a logical server with a distinct
device identity, data directory, browser store and independently selected port.
Pair the profiles normally, then verify directional management, catalog/resource
sync, merged local search and owner-routed edits. Reopen the same profile to
verify activation, and restart/switch profiles to verify stable identities and
ports. Never clone a live library just to obtain a second test identity.

`Profiles/DesktopProfileProcessTests` runs real separate processes against only
temporary registries/directories: simultaneous spaces, duplicate activation,
exclusive-lock probing and installation update exclusion. `DesktopProfileUpdateTicketTests`
checks update handoff races and failed-update recovery. Keep these distinct from
production-host protocol tests: an isolated headless host validates federation,
not native desktop UI or the Windows installer. Use Anobaka-Home (or another
Windows machine) for the final native upgrade, tray/WebView, mapped local-player
and retained-pairing acceptance after the local passes. Report explicitly which
of those native Windows checks have actually been performed.
