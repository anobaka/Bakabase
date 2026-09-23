# Native GUI source fixture

This test entry uses the production `Bakabase.Shell` and `Bakabase.Service`.
It has its own executable name, AppData profile and per-run single-instance ID.
It is not a shipped product or a second installed unified application. The
installed unified application remains the reader in the native acceptance run.
No production mutex, federation same-origin check, or updater setting is changed.

Build on each disposable GitHub-hosted native runner, after `setup-dotnet`:

```sh
dotnet publish src/tests/Bakabase.NativeGui.SourceHost/Bakabase.NativeGui.SourceHost.csproj \
  --configuration Release --runtime "$RID" --self-contained true \
  -p:RuntimeMode="$RUNTIME_MODE" --output "$RUNNER_TEMP/native-gui-source-publish"
```

`RUNTIME_MODE` is `WINFORMS` for `win-x64`, `MACOS` for both macOS architectures.
Use `native-gui-acceptance/source_fixture.py` to create the private fixture. Its
`create` method takes the publish directory, audited installed reader web root,
verified package provenance and expected web inventory. The latter maps every
relative POSIX filename to `{sizeBytes, sha256}` and must come from the reader's
package-payload verification. The source runtime is built at the execution SHA;
the web assets come from the audited candidate package. Evidence records both.

`start()` returns `app`, `pid` and the exact native process identity. `seed()`
creates three resources through production resource APIs and reads them back.
It never enables sharing or browsing, creates an invitation, pairs or approves.
Those actions belong to native controls in the flow. `read_only_baseline()`
verifies unchanged resource meaning, manual introduction, media and `playedAt`.

`stop()` records the original node/epoch/grant digest. `restart()` uses the same
data directory and port, requires a new PID and preserves those values. Neither
operation repairs or reseeds data. `close()` stops verified owned processes and
removes its private root and the explicitly owned test bundle's cache domains.
On macOS it waits for the exact, previously bound WebKit process to exit without
sending that process a signal. A restart clears the binding and requires a new
complete native probe before final cleanup. Unexpected ownership or cleanup failures retain
the root and report failure; raw logs, grants and credentials are not evidence
artifacts. Always invoke `close()` in a `finally` block before installed products
are removed, because the source reads the installed reader's audited web root.

Only pure tests may run on a developer machine:

```sh
PYTHONDONTWRITEBYTECODE=1 python src/tests/native-gui-acceptance/test_source_fixture.py
```
