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
