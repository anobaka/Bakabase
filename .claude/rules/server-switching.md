# Server Switching (managing other servers from the desktop app)

Every PC install is the all-in-one desktop app. It always runs its own server, and its main
window can switch to another server it manages — a NAS, or another PC's all-in-one — and
control it fully, as the removed thin client did. Headless servers (Docker/NAS) never
manage anything; they are only ever managed.

## How it works

- **The other server's own UI, not ours.** The window shows the target's own SPA bundle, so
  UI and API always match whatever version the target runs. This device's bundle could not
  drive another server's API anyway: the SPA is same-origin by construction, and request
  signing cannot happen in a page.
- **One relay per managed server.** `Bakabase.Remoting` composes, for each server, a slim
  `WebApplication` with its own container, bound to `127.0.0.1` on a port that stays the same
  for that server across launches (browser storage is keyed by origin). It signs every
  forwarded request with that server's device key and intercepts the actions that must run on
  this machine (play, open folder, cookie capture, batch play) with this machine's player
  configuration and per-server path mappings.
- **Never in the Service.** The relays live next to the in-process server, not inside its
  container or pipeline — the user-machine dispatcher would otherwise intercept the local
  server's own play and open routes. The Service reaches them only through
  `IManagedServerService` (resolved optionally), and headless builds register none.
- **Switching is a navigation.** The SPA asks for a URL (`/federation/local/servers/{id}/open`
  locally, `/client/switcher/{id}/open` inside a relay) and assigns `location`. The tray menu
  (`IMainViewSwitcher`) is the way back when the target's UI is too old to have a switcher.
- **A server is its install identity, not its address.** A relay forwards, and signs, nothing
  until its server's address has answered the pairing handshake (`/remote-access/server-info`)
  with that server's `ServerId` (`UpstreamIdentity`, with `RemoteConsoleManager` as the
  verifier). Addresses change hands for real: a desktop app picks its ports at every launch, so
  after a restart the port a managed server had can belong to another install on the same
  machine — or to this device itself — and a DHCP lease can move a NAS's address to another NAS.
  The server that answers then may never check the device key (a server takes any loopback
  caller as local; an unrestricted or older one may let anyone in), so the key being accepted
  proves nothing about who accepted it. When it is asked, kept off the hot path:
  - a request reads the last answer from memory and waits only when it is missing, older than
    a minute, about another address, or under suspicion — and past half a minute it starts the
    next question in the background, so a page in use is re-checked without waiting;
  - every **new TCP connection** to the server (the relay's `ConnectCallback`, for YARP and the
    relay's own `HttpClient`s alike) needs an answer no older than two seconds — a server that
    restarts drops every connection to it, so whoever holds its port next is asked before a
    single request reaches it, at one question per burst of new connections;
  - a forwarding failure the server or network caused raises suspicion (the next request asks
    first), and opening a server (`OpenAsync`, the tray, the switcher) and every probe ask too;
    a probe's answer is handed to the running relay at once.
  All waiting callers share one question; a failed or mismatched answer stands three seconds.
  An address answering as someone else is `WrongServer` (another install, or `IsThisDevice`);
  one where nobody can be identified (nothing answers, not Bakabase, remote access off with no
  identity) is refused as unreachable. Refused requests get 503 with `X-Bakabase-Client:
  WrongServer` (or `ServerUnreachable`) and a message naming the address and who answers; a
  navigation gets the console's unavailable page with the same reason, at the URL asked for.
  Remote access off (the gate's 403 `Disabled` before any identity) is carried on the check
  (`UpstreamIdentityCheck.RemoteAccessDisabled`): the page and the JSON message both say to
  turn it on at that device under "Let other devices manage this device", never to check that
  it is running. The listing reports `ManagedServerState.WrongServer` with `answeredBy` (never the other
  server's name, mode or version as the server's own), and nothing is renamed, re-keyed or
  paired from such an answer. **The verdict never waits on the store**: an answer from the
  right server also keeps its stored name and `LastConnectedAt` current, but that write runs in
  the background (`RemoteConsoleManager.NoteAnswered`, one writer per server holding the latest
  answer); a failed write (full disk, a locked or replaced `connection.json`) is logged and
  retried every `StoreRetryInterval` until it lands, and forwarding and probing carry on
  meanwhile. Relocation is left to the user: pairing again at the new address
  keeps path mappings and the relay port. Limit: the check compares a claimed identity —
  remote access has no server-side proof, unlike federation's handshake — so it catches an
  address that moved, not a server lying about who it is.
- **The switcher inside a relay says how each server was last seen.** `GET /client/switcher`
  answers `{currentId, targets: [{id, name, isLocal, isCurrent, state}]}`. `state` is
  `ManagedServerState` as a number — `0` Unknown, `1` Online, `2` Offline, `3` Revoked,
  `4` WrongServer — and is **absent on the local target** (this device is the app itself, not a
  server it reaches). The relay's own (current) server is WrongServer while its address answers
  as someone else, and otherwise reports what the latest request forwarded through this relay
  said (`UpstreamStanding`): any answer the server's gate did not refuse is Online, even a 404
  or 500, since the signature was accepted; `DeviceRevoked`/`Unauthenticated` is Revoked;
  `Disabled`/`SignatureExpired`, a forwarding failure the server or the network caused, or
  nobody identifiable at the address, is Offline; the relay's own answers and the browser
  hanging up change nothing. Until anything has been forwarded it falls back to the console's
  last probe (`LastKnownState`, which a pairing and the relays' own questions also set), which
  is also what every other managed server reports. Read from memory only: the listing never
  waits on the network or the disk.
- **Management is legacy paired-device access.** Pairing uses `/remote-access/pair/*` with a
  code or an approved request, exactly as the removed thin client did, and grants full control
  of the target. It is unrelated to federation grants, which stay read-only.
- **A filed request is collected in the background.** The manager claims it every few seconds
  until it is approved, rejected, expires or is cancelled. In the listing, `outcome` is what the
  last attempt said and `active` is whether the wait is still on: a claim that did not get
  through (`Unreachable`, `TooManyAttempts`) does not end it, so the page polls and offers
  "cancel" on `active`, never on `outcome`.
- **Finding servers to manage uses the remote-access beacons**, not library sharing:
  `GET /federation/local/servers/discover` (UDP probe + mDNS, ~3 s, on request only) lists
  every server answering them, minus this install and marked when already managed. Library
  sharing's discovery only lists servers that opted into sharing, which says nothing about
  whether a server can be managed.

## Invariants — do not weaken

- **Keys stay in the store.** Device keys live in `AppData/remote-access/managed/connection.json`
  (mode 0600 on Unix), never in `[Options]`, never in a DTO, log line or `/client` response.
- **A relay only serves this device's own window.** The loopback guard checks `Host`; a browser
  request from another site (`Sec-Fetch-Site: same-site|cross-site`) is refused unless it is a
  top-level navigation carrying a single-use `RelayNavigationTokens` ticket for that relay's
  port. The ticket is consumed and the relay answers with a small page that does
  `location.replace("<same URL without it>" + location.hash)` — **not a 302**: browsers
  compute `Sec-Fetch-Site` over the whole redirect chain, so the request after a 302 would
  still read as cross-site and be refused. The `+ location.hash` is what keeps the SPA's `#`
  route: the fragment is never sent, and unlike a redirect a script navigation does not
  inherit it. Requests without fetch metadata keep the old behaviour, WebSocket handshakes apart (local
  players do not send it). `Cookie` and `Authorization` never reach the target.
- **A WebSocket handshake is judged by its `Origin`, on both sides.** Chromium — and so
  WebView2 — sends **no fetch metadata on a handshake**, only `Host`, `Upgrade` and `Origin`,
  and a handshake is a GET, so neither `Sec-Fetch-Site` nor the relay's non-safe-method rule
  ever sees it; what the socket then carries is past every CORS check. The relay refuses a
  handshake (`Upgrade: websocket`, `Sec-Fetch-Mode: websocket`, or HTTP/2 extended `CONNECT`)
  whose `Origin` is present and is not its own page — `127.0.0.1`, `localhost` or `[::1]` on
  its port — as `ForeignOrigin` (400 `ForeignCaller`), before fetch metadata and before any
  ticket: a ticket is never spent on a handshake. An absent `Origin` is a native client and is
  judged as before. Without this, any page in any browser on the machine — this device's own
  window, another relay's page, a website — could open the managed server's hub through the
  relay, signed with the device key, and read every options object it pushes.
- **The relay presents its page to the server as the server's own UI.** A request the guard
  admitted from the relay's own origin is forwarded with the server's own origin in `Origin`.
  A server on another machine reads no `Origin`; one on this machine (another install, a
  host-network container, an SSH tunnel) takes the relay for a loopback caller and judges a
  handshake by its `Origin` like this device's server does, and would otherwise refuse its own
  UI's hub through the relay. For such a server its `/federation/local` interface answers that
  page as it answers its own window. A foreign `Origin` is never rewritten.
- **A relay never exposes this device's diagnostics to the target's page.** Nothing in
  `UseRelayPipeline` publishes this machine's log or directories. The removed thin client
  served its own at `/client/log*` and `/client/app/*`, and a managed server's older UI may
  still ask; in the desktop app those would be this device's whole log — its own server's
  pairing codes, every managed server's address — and the data directory holding every key,
  so a console relay answers them 404. Beyond the console `/client` contract (status,
  switcher, path mappings, tray, connect page) and the mapped user-machine actions, a managed
  server's page learns nothing about this device.
- **The local server does not trust other loopback origins.** A relay page runs the target's
  JavaScript on a loopback origin; this device's own Service (`LoopbackCrossSiteGuard`, all
  loopback requests, 403 `HostOnly`) refuses, unless the page is one it trusts — an origin in
  its CORS allow-list (its `ApiEndpoints`, the userscript's sites, and `yarn dev`'s
  `http://localhost:3000` only in `RuntimeMode.Dev` builds) or a browser extension (how the
  userscript manager sends its requests):
  - a **WebSocket handshake** whose `Origin` is present and is not the request's own origin —
    exactly its scheme and the `Host` it was sent to, under any name (so the window works on
    `localhost`, on `127.0.0.1`, and behind a hosts-file alias or a local reverse proxy/tunnel
    over plain HTTP, which sends no fetch metadata). This tells apart a page on a *different*
    origin — every relay page and every other site; it does not defend against DNS rebinding,
    which needs a `Host` allow-list covering reads too and is tracked separately. `null` is
    foreign; an absent `Origin` (a native client) is judged as before. By `Origin` because
    Chromium sends no fetch metadata on a handshake: judged by `Sec-Fetch-Site` alone, a relay
    page could open `ws://127.0.0.1:<port>/hub/ui` and read every options object — third-party
    cookies and API keys included — that `GetInitialData` pushes;
  - a request that is not GET/HEAD/OPTIONS, or a frame load, that the browser labels
    `Sec-Fetch-Site: same-site|cross-site`;
  - a request that is not GET/HEAD/OPTIONS, from an engine that sends no `Sec-Fetch-Site`, that
    names a foreign `Origin` — and, after routing, a `[RunsOnUserMachine]` action reached by
    either kind of untrusted page (`LoopbackCrossSiteUserMachineFilter`).

  The Service's only WebSocket endpoints are its two SignalR hubs, `/hub/ui` and
  `/hub/progressor`. Their other transports need a connection id from `negotiate`, a POST that
  a foreign page is refused (and could not read); every send is a POST too, and a long-poll or
  SSE receive carries no CORS grant for a foreign page. The Service listens on plain HTTP,
  where browsers never speak HTTP/2, so an extended `CONNECT` handshake is covered by the unit
  matrix only.
- **The local server cannot be framed by another page.** It refuses cross-site frame loads
  and sends `frame-ancestors 'self'`, so a relay page cannot load this device's own UI in a
  frame and drive it.
- **Trust is explicit and pairwise.** Managing B is a decision taken on B (approve or show a
  code). Nothing joins a device to others automatically.
- **Warn, never reconfigure.** A target in `RemoteAccessMode.Unrestricted` is flagged in the UI;
  the app never changes another server's mode on its own.
- **Never pair with yourself, never talk to yourself.** Refuse an address whose handshake
  returns this install's `ServerId`, and loopback addresses at this app's own server or relay
  ports — when pairing, and when a managed server's stored address comes to point here.
- **Nothing signed goes to an address that does not answer as the server.** Forwarding, the
  relay's own calls (context, play/open lookups, played-at history), probing's signed context
  read and "stop managing"'s revoke all ask first; a mismatch gets the handshake question and
  nothing else. "Stop managing" forgets the server here first and asks it to revoke this device
  afterwards, so an open racing it never hands out a ticket to a relay being stopped.
- **Legacy import is read-only.** The thin client's `connection.json` is read from its own AppData
  (`AppDataPathProfile.Client`, following its redirect) once at startup and on request; its file
  is never written, and servers already managed here are never overwritten.
- **Layering is enforced** by `src/scripts/check-release-contract.py`: the Service image ships no
  `Bakabase.Remoting` or YARP; the desktop app ships YARP only through `Bakabase.Remoting` and no
  `Bakabase.Client*` assembly.

## The removed thin client

`Bakabase.Client.App` and `Bakabase.Client.Remoting` were removed while the product was in
beta, together with their build, update feed, download manifest and frontend (connect page,
updater banner, migration export); there is no deprecation path. Do not reintroduce them.
What stays is deliberate:

- the one-time import of an old install's pairings (`LegacyClientConnectionSource`, reading
  `AppDataPathProfile.Client` from the Infrastructures submodule) and its manual re-run;
- the console's `/client` API keeps the thin client's shape where the two mean the same thing,
  and answers its connect and pairing routes 409 `ManagedByHost` and everything else it had
  404 — a managed server's older UI, written for the thin client, may still call them;
- the release contract refuses anything shipped under the thin client's name, pack ID or feed:
  old installs still have them.

## Tests

- `src/tests/Bakabase.Tests/RemoteAccess/Console` — relays, console endpoints, store view,
  pairing edge cases and request liveness, discovery, legacy import, key secrecy,
  `ConsoleDiagnosticsExposureTests` (a real `AppService` and log behind a relay, answered 404),
  and `RelayIdentityTests`: two real servers swapping one port under a real relay — after a
  restart, while a page is open, found by a probe, answering as this device, at one of this
  app's own ports, the server coming back — with a loopback-trusting and an unrestricted server
  taking the address, asserting the newcomer is only ever asked who it is; plus the right
  server behind a store that cannot be written (still forwarded to and probed Online, the
  write retried on its own) and one with remote access off (the page and the fetch say so).
- `src/tests/Bakabase.Tests/RemoteAccess/UpstreamIdentityTests` — when the relay asks, and what
  an answer stands for (lifetime, refresh ahead, the new-connection window, suspicion, shared
  questions, a moved address, failures), with the clock under the test's control.
- `src/tests/Bakabase.Tests/Federation` — `/federation/local/servers` end to end and its route
  policy (`FederationServerControllerTests`, `Security/FederationGateTests`).
- `src/tests/Bakabase.Tests/RemoteAccess` — loopback guard, navigation tokens, the relay's
  components (forwarding, signing, path mapping, user-machine handlers, batch play), and
  `Console/RelayPipelineTests` (the assembled pipeline of a console relay: guard order, tickets,
  fetch metadata, cookies, forwarding, the no-server path, route interception). WebSocket
  handshakes: `LoopbackOriginGuardTests` (`The_websocket_matrix`),
  `Console/RelayWebSocketOriginTests` (real handshakes through a relay; the forwarded
  `Origin`), `Console/RelayPipelineTests`;
  `Service/LoopbackCrossSiteGuardMatrixTests` (every page × kind × fetch metadata, Dev and
  packaged) and `Service/LoopbackHubAccessTests` (real Kestrel handshakes as Chromium sends
  them, on both hubs, plus negotiate, long polling and SSE).
- Frontend: `yarn vitest run src/features/federation src/layouts`.
- End to end: `src/tests/federation-browser-smoke/switching.cjs` (run by `run.py`, in CI's
  federation job) — Chromium against real hosts, the unified fixture composed as `UnifiedHost`
  is: import of an old thin client's pairing (a real device key from the managed server's own
  pairing API, in the thin client's file format — `legacy-client.cjs`), switch and back, a write on the managed server (pushed live
  to its UI over the relay's hub WebSocket), path mapping and interception, stop managing and
  re-pair by request, and the relay page's containment — the relay page's WebSockets to this
  device's hub refused 403, and other pages' to the relay refused 400 before the managed server
  sees them.
  Each Service's request log is how it judges what reached the managed server and how this
  device answered; see its README for what a one-machine run cannot show.
