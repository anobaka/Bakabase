# Federation browser and legacy migration smoke test

This test starts three isolated HTTP processes: two production Service pipelines with 57 fixture resources each, and a production `ClientHost` / `ClientStartup` pipeline with an inert GUI adapter. It uses the actual built frontend and Chromium. It does **not** test an installed Avalonia package, platform file dialogs, OS folder/player launch, auto-updating, or installer coexistence; those remain native release checks.

The old client selects the real `AppDataPathProfile.Client`. Its `BAKABASE_CLIENT_DATA_DIR` points to a new temporary directory, while a conflicting `BAKABASE_DATA_DIR` points at the unified fixture. No user installation, saved connection, existing signing key or browser profile is accessed. Discovery is disabled in the client fixture, and browser requests outside the three loopback origins are blocked. All test processes are stopped and temporary data is removed on success or failure.

## Run

Use .NET 9, Node 20 or newer, Python 3, and the repository's Yarn version. Build the frontend with its normal dependencies:

```sh
cd src/web
corepack yarn install --immutable
corepack yarn build
cd ../..
```

Install the browser dependency in this test directory, separately from production dependencies:

```sh
cd src/tests/federation-browser-smoke
npm ci --ignore-scripts
npx playwright install chromium
# Linux runners may need: npx playwright install --with-deps chromium
cd ../../..
python3 src/tests/federation-browser-smoke/run.py --results-directory /tmp/bakabase-browser-results
```

The runner builds both test host projects, starts three free loopback ports, waits for their ready files, and invokes `browser.cjs`. Options:

- `--dotnet /absolute/path/to/dotnet` and `--node /absolute/path/to/node` select runtimes.
- `--no-build` reuses already-built Debug/net9.0 test hosts.
- `--web-root /absolute/path/to/dist` selects a production frontend build.
- `--timeout 300` bounds fixture startup plus browser work in seconds.
- `--keep-fixtures` retains temporary databases and **disposable test credentials** for debugging after stopping the processes. Do not publish that directory; normal runs delete it.

`result.json` records assertions or a failed status, including build failures; `browser.log` records the browser outcome. Each `*-host.log` retains the process exit code, readiness, recognized failure categories, exception types and stack method names before fixture deletion. These summaries omit raw messages, ready payloads, paths and arguments because those may contain test credentials. Screenshots and the sanitized exported hint file are retained in the results directory. No legacy or node grant key is included in the report or exported hints.

Runner failure handling can be checked without starting .NET or a browser:

```sh
python3 -m unittest discover -s src/tests/federation-browser-smoke -p 'test_*.py'
```

## What it verifies

1. The old client starts beside the unified host, uses its own data profile, pairs through its shipped connect page, and remains functional after migration.
2. Its real migration UI downloads only names, origin addresses and legacy path hints. The downloaded file contains neither legacy administrator credentials nor source-root IDs.
3. Unified import produces a locally persistent preview, survives reload, and deduplicates repeat imports. Importing or selecting an address creates no grant and binds no path.
4. A fresh node read-only request remains pending until approved through the source device UI. The old client authorization is not reused, no reverse grant appears, and the local library identity stays unchanged.
5. Old and unified browser storage remain separate. Old `connection.json` stays byte-for-byte unchanged, the thin client has no library database, and no legacy key enters unified JSON state.
6. Browsing remains off until explicitly enabled. A combined query returns both libraries, remote detail preserves the full owner/epoch/resource identity, and an unmapped directory cannot be opened.
7. Audio metadata loads from a controlled same-origin URL under `localhost`. Disabling browsing from another window clears active results and media while retaining pairings and sharing settings.
8. Recovery navigation opens the explicit identity controls. The restore action keeps the node ID and outbound connections while changing its library generation; the clone action changes both IDs and clears connections. Both leave sharing and browsing disabled. This tests the reset controls, not a full manual AppData restore.

This complements `ClientPipelineTests`, `run-compatibility.py`, and `federation-smoke/run.py`; it adds the real browser download/import/approval transition that unit and HTTP-only tests cannot cover.
