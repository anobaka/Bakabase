---
paths:
  - "**/Bakabase.Infrastructures/Components/App/**"
  - "**/abstractions/**/FileSystem/**"
  - "**/Migrations/V230/**"
  - "**/legacy/**"
  - "**/modules/**"
  - "**/Bakabase.Service/**"
  - "**/apps/Bakabase/**"
---

# AppData Path Rules

## The invariant

User data — DB, configs, covers, caches, anything written at runtime — must
live at `IAppService.AppDataDirectory`. **Never** under the application's
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

- Read user-data paths from `IAppService.AppDataDirectory` (or the
  resolver / `AppDataPaths` helpers it composes).
- Persist relative paths in the database. `IAppDataPathRelocator` rebases
  them at read time so the user can move their data dir without breaking
  references.
- For env-var override (`BAKABASE_DATA_DIR`) or user-configured DataPath,
  trust `DefaultAppDataPathResolver` — don't re-implement the precedence.

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

## Where the mechanism lives

| Concern | File |
|---|---|
| Platform default + env var resolution | `Bakabase.Infrastructures/Components/App/DefaultAppDataPathResolver.cs` |
| Glue (resolver + AppOptions + env) | `Bakabase.Infrastructures/Components/App/AppService.cs` |
| Path rebasing across roots | `Bakabase.Abstractions/Components/FileSystem/AppDataPathRelocation.cs` |
| User-driven DataPath change → copy + commit | `Bakabase.Infrastructures/Components/App/Relocation/PendingRelocationRunner.cs` |
| Validation of user-chosen DataPath | `Bakabase.Infrastructures/Components/App/Relocation/DataPathValidator.cs` |
| Legacy `current/AppData` notice | `Bakabase.Infrastructures/Components/App/LegacyInstallAppDataDetector.cs` |
| One-shot DB path conversion (absolute → relative) | `legacy/Bakabase.Migrations/V230/PathsRelocationMigrator.cs` |

## Tests

Behavior is locked down in `src/tests/Bakabase.Tests/`:
`DefaultAppDataPathResolverTests`, `AppDataPathRelocationTests`,
`DataPathValidatorTests`, `LegacyInstallDetectorTests`,
`Relocation/PendingRelocationRunnerTests`. Add a unit test there before
changing any of the files above.

The end-to-end scripts in [`src/tests/upgrade-tests/`](../../src/tests/upgrade-tests/)
are a manual regression guard for this rule — run them before a release if
you've touched anything in this list.
