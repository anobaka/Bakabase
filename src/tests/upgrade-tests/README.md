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
