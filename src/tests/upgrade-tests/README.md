# Upgrade and release compatibility checks

The automatic checks preserve the identities of `Bakabase` and the legacy
`Bakabase.Client`. They do not publish, contact an updater feed, install a GUI
package, or migrate a user's data.

## Automatic checks (CI on Windows, Linux and both macOS architectures)

```bash
python src/tests/upgrade-tests/check-release-contract.py
python src/tests/upgrade-tests/test_release_contract.py
python src/tests/upgrade-tests/run-compatibility.py --dotnet /path/to/dotnet
```

`run-compatibility.py` builds the real test assembly and executes eight existing
classes covering separate AppData profiles/environment variables, defaults,
relocation with SQLite integrity and interrupted moves, legacy detection, updater
feed isolation, and the legacy client's real HTTP migration-hints export. Each
class runs in its own process and disposable temporary directory. Logs and TRX
are retained; no test or empty batch may silently pass. Use `--no-build` only when
the current test assembly is already built, and `--results-directory` to choose
the evidence directory. The .NET version comes from the repository `global.json`.

The package gate checks **actual publish output**, including transitive
`.deps.json` libraries, not just project references:

```bash
python src/tests/upgrade-tests/check-release-contract.py --role server --publish-dir /path/to/service-publish --require-web
python src/tests/upgrade-tests/check-release-contract.py --role unified --publish-dir /path/to/desktop-publish --require-web
python src/tests/upgrade-tests/check-release-contract.py --role client --publish-dir /path/to/client-publish
```

The real web bundle must have been copied into `publish/web` for `--require-web`.
The client must have no `web` directory. Service must carry no Client, Shell,
Avalonia or YARP; the unified desktop must carry no Client/YARP; the legacy client
must carry no Service, Federation library host or migrations assembly. Package ID,
main executable, bundle ID, single-instance ID, AppData names, artifact names and
updater prefixes are checked against their existing release identities. A failure
is a reason to review a compatibility change, not to automatically update the
expected identity.

These checks run in `.github/workflows/ci.yml`; `_build.yml` also runs the actual
publish-content gate before producing installer artifacts. No feed is changed.
See [release readiness](../../../docs/multi-device-library-release-readiness.md)
for the native installation/GUI and mixed-device checks that remain required.

## Real isolated Velopack upgrade on macOS

`run-velopack-macos.py` starts a real old desktop application, creates two resources
through its HTTP API, and invokes its existing update check/download/restart
controllers against a loopback-only feed. The actual Velopack `UpdateManager`
downloads and verifies the package; the actual `UpdateMac` replaces the `.app`.
The runner then starts the replaced executable with the same explicit AppData,
requires both resource responses to contain exactly the two unique created IDs,
compares their fields, verifies a data-file hash, and saves consistent SQLite
backups with `PRAGMA integrity_check` results and exactly two `ResourcesV2` rows.
It never simulates the
upgrade by copying a new directory over the old one.

Close other native Bakabase instances first because the application retains its
production single-instance identity. Supply actual `vpk pack` artifacts, with an
older package version and the same `Bakabase` package identity, plus a provenance
JSON containing the exact old/new source commits and pinned submodule commits:

```bash
dotnet build src/tests/upgrade-tests/VelopackIsolationHook/VelopackIsolationHook.csproj \
  -c Release -o /tmp/bakabase-upgrade-hook
python3 src/tests/upgrade-tests/test_velopack_runner.py
python3 src/tests/upgrade-tests/run-velopack-macos.py \
  --old-portable /path/to/old/Bakabase-federation-test-Portable.zip \
  --new-package /path/to/new/Bakabase-0.0.2-federation.4-federation-test-full.nupkg \
  --hook /tmp/bakabase-upgrade-hook/VelopackIsolationHook.dll \
  --provenance /path/to/source-provenance.json \
  --results-directory /tmp/bakabase-real-upgrade-new-run
```

The results directory must not already exist. The script bounds extracted archive
size, serves only the selected feed/package, caps feed traffic, and rejects an
unrelated running native application. It retains application/updater logs, source
and binary hashes, resource responses, SQLite snapshots and `report.json`; by
default it removes its installed bundle, packages and AppData after stopping its
own processes. `--keep-work` retains those directories for inspection. Background
HTTP clients receive a child-process-only denying proxy with a loopback exemption;
no OS proxy or `HOME` setting is changed. This is not a network sandbox.

The test-only .NET startup hook pins Velopack 1.2.0 and sets a custom locator before
the real application `Program` runs. It keeps packages and managed/native update
logs under the fixture, and adds `--norestart --silent` to the real apply command.
It rejects unexpected restart arguments. The runner performs the final direct
binary start so AppData remains explicit; **LaunchServices automatic restart is
not covered**. Pre-initializing the locator causes Velopack's "Run was called more
than once" diagnostic, although the application still calls `Run` once; the hook
is not referenced or shipped by a product project. macOS-managed WebKit and saved
state caches are outside this AppData/Velopack isolation boundary and are not
deleted. Default user cache placement, signed/notarized installers, quarantine,
system installation and production feeds still require separate acceptance.

The 2026-09-21 macOS arm64 execution used real baseline source
`f1fa1469f32f794f17895f5ba886cc14ddd42883`, with Infrastructure
`a3715f40d7c470ec6610ab1a1fdfb8ce21231893` and LazyMortal
`f35a158a8395c09216451f77d04e6fea33b9e77b`, and current source
`51601e31f454ba6d38877f0d002a69a276c881ef`. The old source and submodules were
exported with `git archive` into `/private/tmp`; no full repository clone was
needed. Its old frontend was built from that export. The old self-contained
`osx-arm64` publish used `RuntimeMode=MACOS` and `GitVersionBaseDirectory` pointing
to a checkout at exactly the baseline commit. Use canonical `/private/tmp` paths
for restore and publish to avoid MSBuild project-reference differences through
the `/tmp` symlink. The old pack command was:

```bash
vpk pack --packId Bakabase --packVersion 0.0.1-upgrade.1 \
  --packDir /private/tmp/old-publish --mainExe Bakabase \
  --outputDir /private/tmp/old-packages \
  --icon /private/tmp/old-publish/Assets/app.icns \
  --plist /private/tmp/old-publish/Info.plist \
  --channel federation-test --noInst true
```

The old plist was unchanged. These are **synthetic test-version portable packages
of real old/new source**, not downloaded historical signed releases. Observed
core versions were `2.4.0-beta.342` → `2.4.0-beta.345`; package versions were
`0.0.1-upgrade.1` → `0.0.2-federation.4`. Both SQLite snapshots retained two
resources and 127 EF migrations; both integrity checks passed. The first successful
run used two feed requests (89,888,838 bytes); the final automatic-cleanup run
included another application feed check, for three requests and 89,889,144 bytes.
UpdateMac logged successful replacement with `Restart: false`, all owned
application/updater processes exited, and the final run removed its work data.
After review, response-shape/identity and exact SQLite row-count assertions were
added to prevent matching empty responses from passing. All five runner boundary
tests passed, and the strengthened real upgrade/cleanup run passed again with
the same three-request byte count. Its retained local evidence is
`/private/tmp/bakabase-real-upgrade-51601e31/run5/report.json` and adjacent logs and
`evidence/{old,new}.sqlite`; temporary evidence paths are machine-local artifacts.

## Native installer acceptance on disposable runners

Dispatch the existing CI workflow with `suite=packages` and the development
branch to run `_package_acceptance.yml` at that exact commit:

```bash
gh workflow run ci.yml --ref codex/multi-device-library -f suite=packages
```

The default CI selection remains `full`. Package acceptance builds the actual
production frontend once and self-contained unified/legacy-client packages for
Windows x64, macOS Intel and macOS ARM. It uses pinned Velopack 1.2.0 and synthetic
test versions. Package files and evidence are Actions artifacts; this workflow
does not call deployment, upload a release, or modify an update feed.

`run-package-acceptance.py` checks both portable and full-package identities and
binary hashes, then starts the portable application and invokes the original
installer. macOS runs the original `.pkg` system installation and requires its
postinstall LaunchServices startup to reach the expected default AppData.
Windows runs Setup in silent mode with an owned installation directory, starts
the installed binary, then invokes its real uninstaller. Installed binaries must
match the audited package, serve the correct application UI, and use the expected
data path. The unified application creates a real resource through its API;
after shutdown, the database must contain that resource and pass integrity checks.

Execution is restricted to disposable **GitHub-hosted** native runners and
rejects pre-existing installations, data and caches. It never installs into a
developer's machine. The script removes only its newly created installation,
receipts, processes, data and caches; cleanup failures fail the run. Local
`--audit-only` usage reads the three package artifacts without launching anything.
The pure guard tests run without native applications:

```bash
python3 src/tests/upgrade-tests/test_package_acceptance.py
```

These unsigned fixture packages do not validate production signing, notarization,
Gatekeeper/SmartScreen trust, historical signed upgrades, or production feeds.
First-install LaunchServices startup is distinct from updater automatic restart.
Results must state which of these behaviors actually ran and passed.

## Installed coexistence and native automatic updates

The `installed` selection reuses artifacts from a successful `packages` run:

```bash
gh workflow run ci.yml --ref codex/multi-device-library \
  -f suite=installed -f package_run_id=35618999687
```

`prepare-installed-inputs.py` checks the source run, repository, commit ancestry,
artifact ZIP digest, and every native package's size and SHA256 against its verified
acceptance report. Product or build-source changes since the package run reject
reuse; rebuild with `suite=packages` first. The test checkout may contain later
test, evidence-documentation and explicitly allowed acceptance-workflow changes.

On three disposable native runners, `run-installed-lifecycle.py` installs the
original legacy client before the unified application. Both use their real
default installation and AppData paths. It checks simultaneous API/UI service,
separate process identities, a unified resource created through its actual API,
client settings read back through its API, independent restarts, and both
directions of removal and survivor restart. Windows invokes the original
uninstaller; macOS removes only the owned bundle and package receipt.

`prepare-installed-updates.py` uses Velopack 1.2.0 to repackage the same verified
product payload with a higher synthetic manifest version and a data-only marker.
Every product file is hashed, including web assets and native dependencies;
vendor-generated updater metadata is recorded separately. No product code is
rebuilt. This checks updater mechanics and data retention, and complements the
separate historical-source migration test above.

The two products receive separate, allowlisted loopback feeds. Only one feed
advertises its update at a time. The installed application itself checks and
downloads the package, reaches `PendingRestart` through its existing API or
SignalR event, and requests apply/restart through its actual updater endpoint.
The runner requires the default cache's exact package hash, original updater
process and logs, old process exit, automatic new process startup, new manifest
and marker, unchanged product hashes, and retained data. It continuously samples
the other product's process identity and API. A restart into the old version,
manual relaunch, startup hook, cache override or forced updater termination
cannot count as a successful update.

Only small provenance, package manifests, hashes, reports and bounded logs are
uploaded. The test feeds never publish to production. These unsigned same-code
fixtures do not prove historical signed-package migrations, operating-system
trust, production CDN behavior or physical multi-device networking. Results and
remaining release gates are recorded in
[release readiness](../../../docs/multi-device-library-release-readiness.md).

## Older filesystem replacement fixtures

`run-macos.sh`, `run-linux.sh`, `run-windows.ps1` and `run-docker.sh` remain manual
utilities. They publish the current source twice with synthetic version numbers,
seed files in an isolated directory, replace the simulated `current` directory,
and compare hashes. B is a default-layout fixture, C a custom-path fixture, and D
an environment-variable fixture. Example:

```bash
./src/tests/upgrade-tests/run-macos.sh --scenario B
./src/tests/upgrade-tests/run-linux.sh --scenario D
# PowerShell
./src/tests/upgrade-tests/run-windows.ps1 -Scenario C
```

They **do not start the application**, resolve its effective AppData at runtime,
invoke Velopack, exercise a hosted update feed, or prove a real old-installation
upgrade. Their SQLite-named fixture is random bytes, not an integrity-tested
SQLite database. They leave their own work directory for inspection. Native
production resolver/relocation behavior is covered by the automatic real-code
tests above; actual signed installer upgrades and GUI coexistence require the
release matrix. The helper `vpk_pack` is available but these scripts do not call
it. No `vpk` installation is needed to run their current scenarios.

For legacy versions whose data lives inside an updater-managed `current` folder,
back up and move the data to a supported external AppData location before applying
an update. Directory-copy fixtures are not proof that upgrading such installations
is safe. Use the application's supported relocation/recovery procedure and verify
the backup before proceeding.
