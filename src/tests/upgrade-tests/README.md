# Velopack Upgrade Tests

End-to-end regression guard for the **AppData survives upgrade** invariant —
the property whose violation caused issue #1070.

## Status: manual / nightly only

These scripts are **not** wired into CI and should not be. They are slow
(two full `dotnet publish` + `vpk pack` cycles per scenario) and require
`vpk` installed on the host.

The primary defense is the C# unit/integration suite in
[`../Bakabase.Tests/`](../Bakabase.Tests/):

| Concern | Test class |
|---|---|
| Default path resolution per platform, env-var override, XDG, debug isolation | `DefaultAppDataPathResolverTests` |
| Path rebasing across roots, longest-prefix match, `/current/` segment stripping | `AppDataPathRelocationTests` |
| DataPath validation (system paths, Velopack install, circular containment, free space) | `DataPathValidatorTests` |
| Legacy install detection + dismissal persistence | `LegacyInstallDetectorTests` |
| Pending relocation: marker parsing, MergeOverwrite, staging, SQLite integrity | `Relocation/PendingRelocationRunnerTests` |

If a behavior of the AppData mechanism is wrong, those tests will catch it
faster and more precisely than these scripts.

## What these scripts still earn their keep on

The unit suite mocks the filesystem and resolver. These scripts exercise the
gaps that mocks cannot reach:

1. **Human-error regressions** — someone adds code that writes user data
   relative to `AppContext.BaseDirectory` or hardcodes `current/`, bypassing
   `IAppService.AppDataDirectory` entirely. See
   [`../../../.claude/rules/appdata-paths.md`](../../../.claude/rules/appdata-paths.md).
2. **Real publish + vpk pack pipeline** — catches packaging regressions
   (missing assets, broken Info.plist, wrong RID) that wouldn't surface in
   unit tests.
3. **Native platform path semantics** — actual `$HOME`, `XDG_DATA_HOME`,
   `%LocalAppData%` resolution as the OS sees them, not as a `Func<>` mock
   reports them.

## When to run

- Before cutting a release.
- After any change to the publish / vpk pipeline (`.csproj`, `Bakabase.Updater`,
  CI `_release.yml`).
- After any change to `DefaultAppDataPathResolver`, `AppService`, or
  `PendingRelocationRunner`.

For day-to-day development, the unit suite is sufficient.

## What each scenario verifies

| Scenario | Verifies |
|---|---|
| `B-self-upgrade` | A new-layout build upgrading to a newer new-layout build leaves `%LocalAppData%\Bakabase` (or platform equivalent) byte-identical |
| `C-custom-datapath` | When `AppOptions.DataPath` points to an external location, that location is byte-identical after upgrade |
| `D-env-var` | When `BAKABASE_DATA_DIR=/data` is set, the data dir is byte-identical after upgrade |

`B` is the core invariant. `C` and `D` are also covered by unit tests but
exercised here against real OS path semantics.

## Why we don't test the **old → new** transition

You can't. The whole reason the relocation work exists is that the old
layout put AppData inside `current/`, which Velopack removes during upgrade.
There is no non-destructive path forward; the announcement (issue #1070)
tells affected users to migrate manually. These scripts only test new → new.

## Prereqs

- .NET SDK matching `global.json` (currently 9.0.313)
- `vpk` CLI (`dotnet tool install -g vpk`)
- yarn + node for the frontend build (`cd src/web && yarn install`)

## Running

```bash
# macOS
./src/tests/upgrade-tests/run-macos.sh

# Linux (host or in a Docker container)
./src/tests/upgrade-tests/run-linux.sh

# Linux via Docker (no host pollution)
./src/tests/upgrade-tests/run-docker.sh

# Windows (PowerShell)
.\src\tests\upgrade-tests\run-windows.ps1
```

Each script accepts `--scenario {B|C|D}` (bash) / `-Scenario {B|C|D}`
(PowerShell). Default is `B`.

## How the simulated upgrade differs from a real one

We do **not** invoke `UpdateManager.CheckForUpdates(...)` from inside the app
— that requires a hosted feed. Instead we replicate Velopack's
filesystem-level behavior: `current/` is atomically replaced with the new
version's `publish/` output. This is the exact sequence that historically
caused data loss.

If you need higher-fidelity testing (delta packs, signing, post-update
hooks), extend `_lib.sh` / `_lib.ps1` to use `vpk` to produce a release feed
and configure each script to point the app at it.

## Failure artefacts

On failure each script copies the install dir + AppData dir to
`./src/tests/upgrade-tests/failures/<scenario>-<timestamp>/` for inspection.
