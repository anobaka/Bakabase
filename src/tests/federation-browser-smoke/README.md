# Federation browser, legacy migration and server-switching smoke test

This test starts three isolated HTTP processes: two production Service pipelines with 57 fixture resources each, and a production `ClientHost` / `ClientStartup` pipeline with an inert GUI adapter. The "unified" Service is composed as the desktop app's `UnifiedHost` composes it — the server plus the relay manager (`AddRemoteConsole`) and the recorded window origin — so it can manage the "source" Service through a per-server loopback relay; the relays it starts live in its process. A fourth, "first-launch", is a second desktop-composed Service with no resources that the browser stage starts once the thin client has paired, checks and stops again. It uses the actual built frontend and Chromium. It does **not** test an installed Avalonia package, the tray's switcher menu, platform file dialogs, OS folder/player launch, auto-updating, or installer coexistence; those remain native release checks.

The old client selects the real `AppDataPathProfile.Client`. Its `BAKABASE_CLIENT_DATA_DIR` points to a new temporary directory, while a conflicting `BAKABASE_DATA_DIR` points at the unified fixture. The unified and first-launch fixtures are given the same `BAKABASE_CLIENT_DATA_DIR`, which is how the desktop app finds the thin client's pairings to import. No user installation, saved connection, existing signing key or browser profile is accessed. Discovery is disabled in the client fixture and never started in the desktop ones. All test processes — relays included — are stopped and temporary data is removed on success or failure.

**Nothing leaves the machine.** The shipped `appsettings.json` carries live Clarity, GA4, PostHog and Sentry ids and anonymous tracking is on by default, and the fixtures serve the production frontend, which starts those SDKs with whatever `/app/analytics-info` hands it. So:

- Both test hosts blank every `Analytics:*` key — the known ones and whatever the shipped settings files put under `Analytics` — and set `App:EnableAnonymousDataTracking` to false, as environment variables that win over those files (`Bakabase.Federation.TestHost/FixtureAnalytics.cs`, linked into the client host). A host whose effective configuration still has any of them set never reports ready. `run.py` passes the same settings, and the browser stage and `federation-smoke/run.py` check `/app/analytics-info` of every Service before any page loads.
- The browser is confined as a second line (`network.cjs`): each context refuses every request outside the fixtures' origins and the relay port range (`127.0.0.1:34650`–`34905`, the relay manager's default), and Chromium is launched with `--host-resolver-rules` under which no name but `localhost` and `127.0.0.1` resolves — which also covers WebSockets and preconnects, which routes cannot refuse. Every refused attempt is recorded once (origin and path, never the query) in the report as `blockedRequests`, and a stage fails when any page tried to reach an address off this machine. A WebSocket has two witnesses, because Playwright alone announces one only once its handshake was sent or it failed, and forgets it when its page navigates — so a socket whose page moved on or closed before the refused lookup came back used to be refused and never recorded. Every frame of every page now reports each socket as it is made (an init script subclasses `WebSocket`, reports through a binding, then opens it exactly as the browser's own class does), and Playwright's report still covers a worker's. A stage reads the record only after every open page's reports are in (`assertStayedLocal` makes one last binding call per page, answered after all earlier ones). Each run first proves this on a canary page: a fetch, a beacon, an image and WebSockets to `.invalid` addresses must all be refused and recorded — including a socket whose page navigates on at once, one whose page is closed at once and one in a frame, which must be in the record without any waiting, and one in a worker.

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

The runner builds both test host projects, starts three free loopback ports, waits for their ready files, and invokes `browser.cjs`, which runs the legacy-migration and federation stage and then `switching.cjs` (it reuses the thin client's pairing made by the first stage). Options:

- `--dotnet /absolute/path/to/dotnet` and `--node /absolute/path/to/node` select runtimes.
- `--no-build` reuses already-built Debug/net9.0 test hosts.
- `--web-root /absolute/path/to/dist` selects a production frontend build.
- `--timeout 300` bounds fixture startup plus browser work in seconds.
- `--keep-fixtures` retains temporary databases and **disposable test credentials** for debugging after stopping the processes. Do not publish that directory; normal runs delete it.

`result.json` records assertions or a failed status, including build failures; `browser.log` records the browser outcome. Each `*-host.log` (the first-launch fixture's included, once it has started) retains the process exit code, readiness, recognized failure categories, exception types and stack method names before fixture deletion. These summaries omit raw messages, ready payloads, paths and arguments because those may contain test credentials. When a run fails, each Service's `*-requests.jsonl` is kept too — the evidence of what reached which server and how it answered — rewritten to the recorder's own fields. Screenshots (`switching-a-manages-b.png`, `switching-b-console.png` and `switching-back-on-a.png` among them) and the sanitized exported hint file are retained in the results directory. No legacy or node grant key, and no managed-server device key, is included in the report, the exported hints or a kept request log.

The test host takes these optional, test-only environment variables, all set by `run.py`:

- `BAKABASE_FEDERATION_TEST_DESKTOP_WINDOW` (unified and first-launch): compose the relay manager as `UnifiedHost` does and record this address — the harness browser's origin for the host — as the window's origin, the one "back to this device" returns to. Readiness waits for the manager's one-time startup import.
- `BAKABASE_FEDERATION_TEST_REQUEST_LOG` (unified and source): an outermost observer writes each request's method, path, `Sec-Fetch-Site`/`Dest`, its `Origin`, whether it is a WebSocket handshake, whether it carried a switch ticket or a cookie, what its device signature verifies as against that host's own paired devices (its own nonce cache, so the server's is never consumed), and the status and refusal header the host answered with. Never any other header, queries, bodies or keys, and a media ticket in a path is replaced by `{ticket}`; the file is deleted with the fixture unless the run failed.
- `BAKABASE_FEDERATION_TEST_SERVER_NAME` (every Service): the name the server gives itself in `server-info` and discovery — and so the name every pairing records and every switcher shows — in place of `Environment.MachineName`, which all fixtures on one machine share. `run.py` names them `fixture-<role>`, so a check by name can tell A from B.
- `BAKABASE_FEDERATION_TEST_WEB_ROOT`: the production frontend, served after the Service's own pipeline — as a release build's `UseSpa` is — so the request gates, `frame-ancestors` included, apply to the UI's own document.

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
9. First launch (`first-launch.cjs`): a desktop install started for the first time after the thin client paired — its own data directory, the thin client's to read — has brought the thin client's pairing with the source over by itself when it reports ready, before any page is opened: the listing shows it imported, with its path mappings and no key; the store holds the thin client's device ID and key, mode `0600`, and records that the import ran; the thin client's file is unchanged.
10. Server switching (`switching.cjs`), with the unified host as this computer (A) and the source as the server it manages (B):
   - **Import.** "Import from Bakabase Client" on A's devices page brings over the thin client's pairing with B — its device ID, key and path mappings — without pairing again. The listing carries no key; A's store holds exactly the thin client's key, mode `0600`; the thin client's file is unchanged; "Check status" reports B online.
   - **Switch.** The switcher at the top of the menu sends the window to `127.0.0.1:<relay port>`. The switch is followed document by document: the ticketed navigation must be served the landing page, and the landing page's own navigation, without the ticket, must be served too — a missing or refused ticket fails there with the relay's answer (e.g. `HTTP 400 (ForeignCaller)`), not as a timeout on UI text. Exactly one ticketed navigation happens, the ticket is gone from the URL and from the window's history (read over CDP), and the switcher says "Managing" with B's name, which is not A's. The page's `/client/status` says `console` with B's server and the imported device ID and no key; `/client/switcher` lists this device and B as current. Every request B receives from the window or the relay — B's UI document included — verifies on B as the imported device's signature, with no ticket and no cookie (a cookie set on the relay origin is not forwarded).
   - **Switch to a route.** Back on A, A's own page asks `/federation/local/servers/{id}/open` for B at `/#/configuration` and sends the window there. The route is the fragment, which the browser never sends, so only the landing page's `+ location.hash` can carry it on: the window must end on the relay at `#/configuration`, after exactly one ticketed navigation and with no ticket left.
   - **Full control.** Turning on "Allow live transcoding for other devices" in B's own settings page (where the routed switch landed), through the relay, changes B's setting as read from B's own API — a write no read-only sharing grant can make — and B saw it as a signed `PUT`. B's own UI keeps its hub through the relay as a WebSocket: the change is pushed to it live (an `OptionsChanged` frame with `allowLiveTranscode: true` on the relay page's `/hub/ui` socket), and B recorded every hub handshake of that page as signed by the managed device, naming B's own origin — the relay forwards a request it admitted from its own page that way — and answered 101. A's own UI likewise negotiated and upgraded its hub from its window origin.
   - **This computer's actions.** A path mapping added in A's UI (B's library folder → a folder that does not exist on A) is what A's relay applies: B's "open folder" request (`/resource/directory?id=1`, sent from B's page) is answered by the relay with the locally mapped path, after the relay itself asked B for the resource's path; an unmapped `/tool/open-file` is answered `PathNotMapped`. Neither reaches B. No folder or program is opened: the relay's container has no seam for a recording shell opener without a product change, so the handler is driven to the point where the missing local folder stops it.
   - **Back.** The switcher returns the window to A's recorded origin (`localhost:<port>`), where a `localStorage` value set before switching is still there — neither visible to B's page nor replaced by what B's page stored under the same key.
   - **Stop and re-pair.** "Stop managing" asks B to revoke the device (a signed `DELETE`, after which B no longer lists it) and deletes the key. Adding B's address without a code files a request shown as waiting; approving it in B's own devices page is collected by A in the background and noticed by A's page without a reload; B reappears and switching works again, now signed by the new device.
   - **Containment.** From the relay page, no-cors `POST /remote-access/pairing/code`, a no-cors `GET /file/icon` (runs on this machine) and an `<iframe>` of A's UI — against both `127.0.0.1:<A>` (same site) and `localhost:<A>` (cross site) — are each answered 403 `HostOnly` by A according to A's own request log (the frame with `frame-ancestors 'self'`), and A's pairing code is unchanged. `/client/log`, `/client/log/open`, `/client/app/info` and `/client/app/open` on the relay answer 404. A navigation into the relay started by a page on A's origin is refused (400 `ForeignCaller`) without a ticket and with every ticket already spent.

   - **Hubs.** Chromium — and so WebView2 — sends no fetch metadata on a WebSocket handshake, only `Host`, `Upgrade` and `Origin`, so both guards judge a handshake by its `Origin`. The relay page opens `ws://127.0.0.1:<A>/hub/ui` and `ws://localhost:<A>/hub/ui` and, should either open, speaks SignalR's handshake and asks for the initial data: neither may open or bring back a single message, Playwright must report each answered 403, and A's own log must show exactly one handshake each, naming the relay's origin, answered 403 `HostOnly` (the report keeps what fetch metadata it carried: none). And a page on A's origin — in both of its address forms — and an opaque `about:blank` page each open `ws://127.0.0.1:<relay>/hub/ui`: the relay must answer 400 and B must receive nothing at all.

   Both Services run on one machine, so B treats the relay as a loopback caller and does not itself enforce the device signature; B's request log verifies each signature with B's own authenticator instead — what B enforces from another machine. That enforcement path stays covered by the RemoteAccess unit tests. A fixture has no screen, so A announces a first-device pairing code at start as a headless server does; the containment check compares A's code before and after rather than expecting none.

### Not covered here

- **The tray's switcher** (`IMainViewSwitcher` in the shell): no Avalonia shell runs, so switching is only ever driven from the SPA's own switcher and `/open` URLs. A native release check.
- **A switch between two relays** — from one managed server's page straight to another's, the same-site navigation between two relay ports. Only one server is managed here.
- **Later launches.** The first-launch fixture shows the import on the first start; that a start after `LegacyClientImportedAt` is recorded leaves the thin client alone is covered by `LegacyClientImportTests.The_automatic_import_runs_once_and_a_manual_one_runs_again`, not here.

This complements `ClientPipelineTests`, `run-compatibility.py`, `federation-smoke/run.py` and the `RemoteAccess/Console` unit tests; it adds the real browser download/import/approval transition and the switch between two real servers' own UIs that unit and HTTP-only tests cannot cover.
