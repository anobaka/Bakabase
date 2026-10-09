# Multi-device Library (Federation)

Every desktop install (and a headless NAS/Docker Service) can share its own library
**read-only** with devices it authorizes, and browse other devices' libraries. There is no
central server and no database replication: each node answers for its own resources and the
current device merges results.

## Where things live

| Concern | Location |
|---|---|
| Identity, grants, pairing, persisted state (`AppData/federation/state.json`) | `src/modules/Bakabase.Modules.Federation/{Identity,Peers,Security}` |
| Outbound HTTP, per-peer verified sessions, address relocation | `.../Federation/Transport` |
| Frozen snapshots, k-way merge, cursors | `.../Federation/Queries` |
| Asset leases, path boundary | `.../Federation/Media` |
| Host adapters, access middleware, media proxy, pairing flow, NAS CLI | `src/apps/Bakabase.Service/Components/Federation/` |
| Endpoints | `src/apps/Bakabase.Service/Controllers/Federation*.cs` |
| UI | `src/web/src/features/federation/` |
| Device map (`/federation/map`) | `src/web/src/features/federation/map/`, `DeviceMapPage.tsx` |
| Data sync (definitions kept in step over `datasync.read` grants) | see `data-sync.md`; its node side is `src/apps/Bakabase.Service/Controllers/{DataSyncNodeController,FederationDataSyncPairingController}.cs`, `src/apps/Bakabase.Service/Components/Federation/{FederationDataSync*,DataSyncNodeInfoContributor}.cs` and `src/modules/Bakabase.Modules.Federation/Peers/FederationScopes.cs` |
| Devices page (`/federation/devices`) | `DevicesPage.tsx`, `src/web/src/features/federation/devices/` |
| Both pages' data (one hook) | `src/web/src/features/federation/hooks/useDevicesData.ts` |
| Design history | `docs/multi-device-library-execution-plan.md` |

The whole multi-server mode — sharing, management, and data sync — is named
「多设备互联」 / "Multi-device" in the UI (`federation.mode`, the menu group, the help topic
`multiDevice`). Route and id names stay `federation`.

## The devices page

`/federation/devices` is split by capability into tabs, the same kinds of trust the map
draws: **本机 / This device** (name, the address to type, what waits here, "add another
device"), **管理 / Management** (full control: devices this one manages, who may manage this
one), **资源库分享 / Library sharing** (read-only: libraries this device browses, sharing its
own), **数据同步 / Data sync** (a summary — its status, definitions sharing, how many devices
this one receives from and how many read it — and the way to `/data-sync`, where all of it is
decided) and **高级 / Advanced** (device ID, "after copying or restoring data"). The registry
is `devices/sections.ts`; the nav is links, not a tablist (Back and copied links work). A
device known only through data sync (a definitions grant, no library access either way) is in
neither library sharing list.

- **`?section=` is a contract.** A value is a tab id or a place inside a tab, and a place
  implies its tab (`resolveSection`). Producers: the Service's notifications (`management`,
  `sharing-requests`), the window's switcher (`servers`), the configuration page
  (`identity`), the map (`device`, `management`, `share`, `sharing`), the library (`sharing`,
  `connect`, `browsing`), the help (`management`, `add-server`, `sharing`), the device tab's
  "waiting for you" (`sync`). Never rename one;
  `devicesSections.test.ts` pins them. A place is revealed (scrolled to, focused, marked
  2.5 s) once what it waits for has loaded, again for every navigation; a place that is not
  there (a request decided meanwhile) and a bare tab focus the tab's heading instead.
- **One read for the page.** The page reads sharing, managed servers, remote access and data
  sync's `/data-sync/map` once (`useDevicesData`, shared with the map) and mounts only the tab
  shown, so switching tabs never waits and the nav's counts stay live. The data sync tab's
  count is what waits on this device (decisions, first syncs to review, requests to answer),
  from the status the hub keeps current. The management sections still read their own data
  where the rest of the page is not available (a managed window, a LAN browser).
- **Focus** follows the map's rules (`devices/useSectionFocusKeeper.ts`): when an action takes
  away the control that had the keyboard, focus goes to the heading of the part it was in;
  a pointer press or focus moved elsewhere is never pulled back — nor the focus a click gives
  the very button it presses (only a key pressed afterwards hands focus back to the keeper),
  so removing a row clicked with the mouse neither moves focus nor scrolls. Every heading
  focus can go to shows a ring from the keyboard (`focusHeadingClass`), and every place a
  link lands on is named by its heading (`aria-labelledby`). A form behind a button that
  it replaces ("+ 添加要管理的设备", "+ 添加要浏览的设备") takes the keyboard into its first
  field when opened from that button (`hooks/useFocusOnOpen.ts`). The Management add form
  stays open while a managed server is WrongServer or Revoked: their tips name its search.
- **Layout** is measured on the page's container (`@container`, `@3xl:`), never the window:
  beside the app's sidebar a wide window can leave a narrow column. The action feedback is
  sticky inside the content's column, so it never covers the sticky nav, and never what a
  link, the nav or the focus keeper brings into view: the page keeps its height (plus a gap)
  in `--devices-scroll-offset`, which every such place and heading uses as its scroll margin
  (`scrollOffsetClass`). A notice (not an error) is left behind by the next navigation.
- **Addresses.** Remote access supplies one candidate list for the devices page, invitations,
  reciprocal pairing and data sync: the optional saved external address, browser-observed API
  endpoints, Compose host endpoints, then native network interfaces. The browser reports its
  effective API endpoint only with management access; a desktop relay uses its upstream server
  address, never the relay's loopback origin. Observations are bounded, expire in memory and
  never overwrite the saved external address. Container interfaces are not host endpoints.
  Compose derives the published port from the same resolved configuration used to start the
  service; remote Docker contexts do not borrow the CLI machine's LAN address.
  Keep distinct schemes and ports on the same host in both UI and reciprocal offers: only
  the connecting device can tell which is reachable. Each peer validates the expected identity
  before exchanging a code and keeps its own successful address. A candidate is not a global
  reachability verdict. The API reports `source`, `kind` and `recommended`; older servers retain
  client-side classification, and virtual/link-local adapters stay folded away. Native interface
  ordering still prefers a LAN gateway (`RemoteAccessAddressClassifier`); reciprocal offers
  keep explicit/browser/deployment sources first and prefer matching subnets within each source.
  Until settings arrive the list says loading or why it failed; "no address found" is only
  for an empty successful response.
- **Words.** 配对/配对码 only for management, 分享码 only for library sharing, 浏览 for what
  sharing allows, 允许 (never 批准) for letting a device in, 添加 (never 连接) for putting a
  device in a list, 多设备资源库 for the merged library. Never shown: 节点, 代际, 设备身份, 旧接口,
  新分享协议, 联合浏览, 授权 as a noun. Server texts that send the reader to the page (the relay's
  unavailable page and refusals, notifications, the CLI) name its current places:
  设备与分享 → 管理 → 谁可以管理本机, → 资源库分享, → 高级 → 复制或恢复数据后 → 设为新设备.
  A text that sends the reader to **another** device, or that another device shows, names
  the Devices page and its tab. A NAS or Docker administrator can now use the same page;
  配置 → 远程访问 and the `federation` CLI remain alternative management surfaces. A management
  request's notification names only 配置 → 远程访问 besides its link. A requester's name is
  its own claim wherever it is shown (一台自称 {{name}} 的设备…), and a decided incoming
  request says what this device did (`federation.requests.incoming*`), never the
  requester's `federation.pair.*` outcome.

## The device map

`/federation/map` draws this device in the middle and every relationship it knows of as a
spoke: library sharing (arrow towards the device that may browse), management (arrow from the
manager), pending requests dashed, devices found nearby as outlines. It reads the listings the
devices page reads — `/federation/local/peers`, `/federation/local/servers`,
`/remote-access/settings` and data sync's `/data-sync/map` (below) — and both discoveries on
request, and acts through the same endpoints and confirmations.

- **Records are merged on evidence only** (`map/graph.ts`). The install id first and always:
  a peer's NodeId is the install's remote-access ServerId (`FederationNodeIdSource`; "Make this
  a new device" replaces both together; only a node reset by an older build, or one
  that replaced an unreadable sharing state, differs),
  so a peer, a managed server and a beacon with one id are one device — even while the server's
  address answers as another. An address (never a
  `WrongServer` address) only where one side carries no id at all (a device that manages this
  one, a request from a server that did not say who it is); two known ids that differ are two
  devices. **A name merges only where neither record carries an install id** (ServerId or
  NodeId — a pairing's device id is not one): the same name on the same host, or, for a device
  that manages this one, a name exactly one other device has — and the panel says it was
  recognised by name. Every install pairs under its machine name, so a name is never enough
  against an id. Two devices of one name that stay apart — an id on either side, or a name
  more than one device has — each say they **may be the same device** as the other
  (`MapNode.namesakes`, the panel's note, with a way to the other): said on both, never drawn
  as one. Not where what they said of themselves rules it out — a headless server never
  manages anything, a phone is not a desktop app, another OS is another machine — and never
  for a request's claim, which says whom it claims to be instead. The note names each other
  record by what it is to this device — the device that manages this one, the one sharing its
  library, one found nearby… — and its address, else its platform (`namesakeDescription`), in
  its text and so in its button's accessible name; two pairings that still read alike add when
  each was paired. Merging decides where a line is drawn, never which identity an action goes
  to.
- **A request someone else filed is a claim.** Incoming sharing and management requests get
  their own node (`sharing-request:` / `manager-request:`), marked unverified on the card, in
  its accessible name and in the panel — never merged into a trusted device and never the
  source of a kind or platform. Two claims from one address under one name are one node; when
  a claim names a device this one knows, the panel sets that device's known address against
  the request's.
- **Nothing the user filed vanishes silently.** A management request this device filed that
  ended (rejected, expired) stays as a node with its outcome and Dismiss until dismissed. Each
  request shows on its own — live with Cancel, ended with Dismiss — also beside a server this
  device already manages: asking again to manage a server that moved joins the request to that
  server by its id, and it must be seen there.
- **The way back to a moved server is confirmed, never remembered.** A managed server whose
  address answers as another install is offered pairing again only at an address that answered
  as that server just now: each place it was last seen — its own beacon, where library sharing
  reaches the same install — is probed (`managedServerApi.probe`) and must answer with the
  server's own id, and is asked again right before pairing. A place that is the server's own
  address under another spelling — every loopback name, and each address this device answers on
  (`sameMachine`, `ownHostsOf`) — is never asked, and not shown as the device's address. A
  connection state kept from an earlier conversation (a peer's `Online`) is never evidence. While
  a request to that place waits, it is shown instead of the form.
- **The panel follows a device, not a record.** Nodes carry identity keys (`MapNode.keys`:
  install ids, addresses, request and pairing ids); when an action turns one record into
  another (found nearby → request → managed server, request → peer) the selection moves to
  the node carrying the key, and actions name the keys of what they create. What an action
  said is held by the page. A record that appears only later is waited for: approving a
  request to manage this device answers the id the device will be listed under once it has
  collected its key, and the details wait for it (re-reading every 2 s, a minute at most),
  then move to it.
- **The keyboard stays in the details when the details take away what had it**
  (`map/useDetailsFocus.ts`). Where focus was is taken when an action starts (`usePanelActions`
  → `onActionStart`), never while rendering: the action's button is disabled while it runs,
  and Chromium moves focus off a disabled control to the page's body at once — jsdom does not,
  which is why the page tests blur it themselves. Once the action is over, focus that fell to
  the body goes back to that control if it is still there and enabled, else to the heading of
  what the details show now. After that, whenever what has the keyboard in the details is
  taken away — a record replaced, a form an answer hid (the way back re-asked after an address
  changed hands), a panel that follows its device — it goes to the heading; checked on every
  change to the page's DOM, since a part of the details can re-render alone. A message's own ×
  gives the keyboard to the heading too. **Never taken back from where the reader put it**: a
  pointer pressed anywhere, or focus moved outside the details, lets go — a re-read (the
  minute the details wait for an approved device, every 2 s) never pulls focus back. The
  browser smoke checks this in Chromium.
- **The details never cover the map.** At 1536 px and wider they are docked beside it. Below
  1536 px (`ON_DEMAND_QUERY`) they show on demand: with nothing selected the map has the
  page's whole width; while they are open they take a column beside it (narrower in a narrower
  window) and the map is laid out again for the width that is left — the same limits, the same
  list when a drawing cannot meet them. Nothing of the map is ever under them, so every device
  the keyboard reaches can be seen (WCAG 2.4.11). The selected device and its lines are
  scrolled into view when it is chosen and each time the map is laid out for another width
  (`map/reveal.ts`), never on a refresh that moves nothing. They close with X or Escape and give
  focus back to what opened them, and stay open on what an action said when the record they
  showed went away. The browser smoke checks at 1440 and 1280 px that no device is under them.
- **Focus on the map is kept by what it is on, never by the element** (`map/mapFocus.ts`).
  Opening the details beside the map can turn the drawing into the list, closing them the list
  back into the drawing, and each replaces every control of the other — so what opened the
  details is remembered as its `data-node`/`data-edge` id, and closing them focuses that id in
  the map as it is then (the relationship, else its device, else this device). When the canvas
  switches rendering while one of its controls had the keyboard, it gives the keyboard to the
  same device or relationship in the new one; focus the reader put elsewhere (a pointer
  pressed, focus moved outside the map) is never pulled back. The page tests drive the width
  through a stubbed `ResizeObserver`; the browser smoke pairs thirteen more devices with A so
  that at 1280 px opening the details lists the map, and checks Escape, X and Escape on the
  map.
- **Kinds are reported, never guessed.** Every install says what it is — `kind` (desktop app
  or headless server: `ServiceSelfDescription`, WinForms/MacOS → desktop, Docker → headless, a
  Dev run by whether it composes `IManagedServerService`) and `platform` (its OS) — as optional
  trailing fields: `server-info` and the management listing's servers/candidates (enums),
  the discovery beacon (`kind`/`os` words) and federation's `NodeInfo` (`kind`/`platform`
  words, outside the handshake proof, kept on the peer from its last verified handshake).
  Older installs leave them out and read as unknown, an unknown value reads as nothing, and
  nothing is decided on them. The map takes a device's kind from its peer's verified
  handshake, then the managed server's last probe, then what answered nearby, then a
  managing pairing's platform (desktop app or phone) — never from a request. This device is
  the desktop app when it can manage servers.
- **Server times are UTC even without a zone.** `/remote-access/settings` writes Newtonsoft's
  naked `yyyy-MM-dd HH:mm:ss.fff`; read every server time through `parseServerTime` /
  `millisecondsUntil` (`@/core/serverTime`), never `Date.parse` — east of UTC it drops live
  requests. `deviceMapServerTimes.test.ts` runs in Asia/Shanghai.
- **Readable at any count** (`map/layout.ts`, `map/text.ts`): no line — either lane a
  relationship can split into, so a change of state never moves a card — and no badge within
  6 px of a card other than its own two; spokes lengthen first, every other device moves to an
  outer tier when that is the shorter picture, then the map is laid out wider and drawn scaled —
  but never below `MIN_SCALE` (its smallest text about 9 px), never taller than `MAX_ASPECT`
  times its width or `MAX_SHOWN_HEIGHT`, and never stretched past the width it was laid out
  for. When no drawing meets all that (around a dozen devices at the desktop app's smallest
  window, fewer while the details stand beside the map),
  the devices are listed instead (`DeviceMapGrid`): every device a card, each relationship
  spelt out with the map's own marks and selectable like the drawing, under a note saying why.
  Cards are sized against the canvas actually laid out and the line they draw under the name. Cards grow with names and the kind line; a name that still does not fit is
  shortened in the middle, and two different names are never shortened alike. A direction
  that does not work is a dotted lane with a warning triangle, explained in the legend and
  named in its accessible name.
- **Data sync lines (`kind: "sync"`) are built by data sync, inside the graph.** The map reads a
  fourth source, `GET /data-sync/map` (`useDataSyncMap`: every 5 s while a definitions request
  or a link waiting for access is live, 15 s otherwise; a failure leaves the other sources on
  the map). `buildSyncEdges` (`features/data-sync/map/mapAdapter.ts`, pure and tested there)
  runs inside `buildDeviceGraph` — never through `extraEdges` — and draws one line per peer on
  the node that carries that peer's install key. Its `in` lane is this device's link
  (receiving, waiting for access or a first sync, or none), its `out` lane the peer's
  `datasync.read` grant; the arrow points to the device that **receives** the definitions, and
  the badge says both ways or receive only (two devices receiving from each other show both
  ways). A link that does not work marks its lane (`syncPaused`, `syncFailed`,
  `syncUpdateNeeded`, `syncAccessLost`); open items add `syncNeedsYou` to the node, and a
  headless peer whose heads report open decisions, paused links or a pending restore adds
  `syncNeedsYouThere`.
- **Definitions requests follow the request rules above.** A request this device filed comes on
  its device's own record (`DataSyncMapPeer.Request`), which finds or creates its `peer:{nodeId}`
  node at step 3, like a library request, so a link to a device that is not a peer yet still has
  a node, and one that ended stays with its outcome and Dismiss (which resets the link). An incoming one is a claim: its own unverified
  `sync-request:{id}` node, deduplicated like `sharing-request:` nodes and never merged into a
  trusted device; its panel shows the request card only, never the rule editor.
- **The data sync sections of the panel act through the map.** They live in
  `features/data-sync/map/` and import nothing from the map: every action goes through the
  panel's own `run`/`confirm` (`DataSyncPanelActions`, a structural subset of
  `usePanelActions`), what an action said through `setNotice`, and they never move focus
  themselves, so the focus rules above hold unchanged. Their rule editor (`SyncRuleDrawing`)
  stands vertical at every width; its arrows are drawn, never pressed, and each choice has one
  control — the mode buttons, the kind chips, [Stop X reading]. The per-direction phrases
  (`federation.map.direction.sync.*`) live in `pages/dataSync.json`; the legend's sync entry,
  like the others, shows only once a line has the kind.

## Invariants — do not weaken

- **The federated view is read-only.** Through `/federation/*` a device browses, searches, views
  and plays other devices' resources; it never edits, deletes, moves or runs tasks on them.
  Managing another server is a different feature with a different credential: the desktop app
  switches its window to that server's own UI through a signed loopback relay using the legacy
  `Bakabase-Device` pairing (see `server-switching.md`). Never route management through the node
  protocol, and never let an admin device key travel on `/federation/v1`.

- **Two interfaces, never mixed.** `/federation/local/*` is for this device's own UI: real
  loopback socket + loopback `Host` + matching `Origin` (`FederationAccessMiddleware.IsLocalCaller`)
  on desktop hosts. **Headless administration** additionally admits the server's own browser
  origin after the ordinary remote-access gate accepts a paired administrator or Unrestricted
  mode (`FederationAdministrationMiddleware`). Disabled mode, pairing requirements, foreign
  Origin/fetch metadata and node credentials remain enforced. A headless server never
  composes desktop relays or launches a native player/file manager; its library previews
  stream to the browser. The context API reports `federationAvailable` for menu/page access.
  `/federation/v1/*` is node-to-node: `export/*` always needs a `Bakabase-Node` signature, even
  from loopback or in `Unrestricted` mode.
  **Data sync's separate permission:** its ordinary API, `/data-sync/*`, may create or widen
  `datasync.read` access — turn definitions sharing on, approve a definitions request, create a
  definitions code, send a request or mint a reciprocal code — for a **paired** caller
  (`RemoteAccessContext.Device != null`: the desktop app's switching window or another paired
  device) as well as for this device's own window and the CLI. A paired device already has full
  control of the server, and a read-only definitions grant is less. A browser admitted only
  because the mode is `Unrestricted` may reduce access (reject, revoke, sharing off, pause),
  never create it (`NotAllowedOnThisDevice`) — until it pairs, which on an Unrestricted server
  any LAN caller can do (it may approve pairing requests there), so the rule refuses only
  callers that have not paired; on an Enabled server an unpaired caller cannot pair itself
  (see `data-sync.md`, "Who may create or widen access"). Library grants stay on
  the authorized `/federation/local/*` interface and the CLI; `/data-sync` has no path to them
  (`DataSyncGrantBoundaryTests`).
- **A node credential is never a legacy principal.** It must not reach options, resource
  writes, `/hub/ui`, file APIs or legacy pairing. Never map it to `IsPaired`.
- **Default deny.** Every new federation action needs an exact entry in
  `FederationRoutePolicy.Allows`; `FederationGateTests.EveryRealFederationActionHasAnExactAllowedProtocolRoute`
  fails otherwise.
- **Directional grants.** A→B never implies B→A or A→C, and a node never queries on behalf of
  another. Two-way pairing is two grants orchestrated by one flow (a single-use reciprocal code
  bound to the requester's NodeId), not one symmetric grant. A reciprocal datasync code is the
  requester's consent to be read back, and goes when that consent does: revoking the device,
  forgetting it ("Done — stop reading X"), removing the peer or an identity reset drop it, and
  so does withdrawing the request unless another two-way request to that device stands — a
  stale copy of the request approved later reads nothing back.
- **Grants have scopes.** A grant is `library.read` (the whole library, read-only; every grant
  made before data sync) or `datasync.read` (the definitions data sync publishes); `*` is only
  ever an endpoint's declaration (the handshake), never a grant's. The two are **separate
  grants** in separate `state.json` collections, approved, revoked and lease-cancelled on their
  own, behind their own switches (`SharingEnabled`, `DataSyncSharingEnabled`); neither ever
  implies the other, and approving one never touches the other. Definitions pair on their own
  routes (`pair/datasync/{request|code|claim}`), so an older node refuses them instead of
  taking them for library requests, and a code of one scope is never accepted on the other's
  route. Definitions pairing never rewrites a library peer's `Label`, `Address`,
  `LibraryEpoch`, `Kind`, `Platform` or `Enabled` (it keeps its own `DataSyncAddress`). The
  gate checks per route: `FederationRoutePolicy.RequiredSharing` names the switch the route
  needs, checked before authentication (`info` answers when either is on). After
  authentication the middleware checks the principal's scope against the route's: a library
  grant on `export/datasync/*`, or a definitions grant on any other Export route, gets 403
  `ScopeNotGranted` (gate rows G6–G9). Behind it, every Export action declares its scope on
  `FederationEndpointAttribute.Scope`, and `FederationLocalAccessFilter` is the fail-closed
  backstop (403 `FederationEndpointDenied`) for an Export action that declares no scope, or
  one whose scope the principal's does not match. Library export services also require
  `library.read` in `NodeGrantService.ValidateAsync` (`ScopeNotGranted`).
  `EveryExportActionDeclaresAScope` and the generated per-action matrix in
  `FederationGateTests` pin it.
- **Local actions stay local.** Playing and opening folders happen on the viewing device with its
  own player configuration. Never launch a program named by a peer; on macOS, packages are only
  revealed (`open -R`), never opened.
- **Peer input is untrusted.** Wire DTOs are validated and budgeted (`QueryProtocol`,
  `FederationMediaSessions.Remember`, `MediaPathBoundary`). Removed enum values (e.g. old
  `ResourceSource` members) must be filtered before they reach the wire, or peers reject whole blocks.
- **A typed address is read one way, for both features** (`RemoteAddressInput`, behind
  `FederationHttpClient.NormalizeAddress` and `ServerConnector`): full-width `：．。` and digits
  and a leading `\\` are forgiven; typed without a scheme it must name its port — none is
  guessed, since the desktop app's port can change at launch and a Docker server's is its
  own — while `http(s)://` keeps its scheme's port, for a reverse proxy. Anything that is not
  an http(s) host and port is `InvalidAddress`; both are answered before anything is sent.
- **No silent widening or truncation.** Unsupported filters are rejected, partial coverage is
  reported per node, budget overruns fail explicitly.
- **Proxies are bypassed** (`UseProxy = false`), matching the desktop app's relays.
- **Connections race IPv4 and IPv6.** Every outbound connection to another device — peer
  requests, discovery, the desktop app's relays and probes — opens through
  `DualStackConnector` (`Bakabase.Modules.RemoteAccess`): IPv4 first, the next address beside
  it after 250 ms, first to connect wins. A server listens on IPv4 only, and Windows resolves a
  computer name IPv6-first; tried one after another, a silently dropped IPv6 address spent the
  whole connect budget. Never go back to a plain `Socket.ConnectAsync(DnsEndPoint)`. A peer
  connection gets 2 s once its name is resolved and 5 s in all, lookup included, so a
  switched-off peer stored as an address is still reported after 2 s. Each call site takes its
  connector from its composition — a `DualStackConnector` service for `AddFederationPeers` and
  `FederationNodeDiscovery`, `RemoteConsoleOptions.Connector` for the desktop app's — and the
  wiring is tested through the real handlers over a name that resolves IPv6-first to a dropped
  address (`PeerConnectionTests`, `FederationNodeDiscoveryTests`, `ConsoleNetworkTests`).
- **A proxy's addresses are dialled only for a domain.** 198.18.0.0/15 (`ProxyFakeAddresses`:
  what Clash/Mihomo, Surge, sing-box and Shadowrocket answer names with in fake-IP or TUN mode;
  reserved, never a LAN address) typed, or the resolver's first answer for a name only the LAN
  knows — a `.local` name or a single label, where the proxy has no way to the device — is
  refused by `DualStackConnector` with `ProxyFakeAddressException`, and nothing is sent. Any
  other name (a DDNS or public domain, a reverse proxy's: `ProxyResolvesItself`) is dialled at
  the proxy's address as the resolver gave it, as before — the proxy resolves it and connects
  for us — and only a failure there becomes `ProxyFakeAddressException`; a later connection to
  the address a question reached goes there again (`ConnectAgainAsync`). Behind a real answer a
  proxy's address is left out. The same exception reports a `.local` name whose LAN (IPv6-only)
  addresses all failed while the system resolver gave a proxy's address
  (`LanHostResolution.ProxyAddress`); those attempts get at most `ProxiedLanConnectLimit`
  (750 ms) of the connector's own, so a hang is still said to be the proxy's within a caller's
  budget (the probe's 2 s). Library sharing says `ProxyFakeAddress` (503; also the peer's
  connection state, and it starts relocation like `NodeUnreachable`), server switching
  `ManagedServerOutcome.ProxyFakeAddress`; both tell the user to set `.local` names and LAN
  addresses to DIRECT or use the IP — for a domain, to set that domain to DIRECT
  (`proxy.ts` picks the wording on the web). Discovery offers an advertised proxy address last.
  No other range is special-cased (`ProxyFakeAddressTests`).
- **`.local` names are asked on the LAN.** The connector's default resolver
  (`LanHostResolver`) asks a `.local` name over Bakabase's own mDNS and the system resolver at
  once. A direct system answer (first address not a proxy's, an IPv4 address among them: the
  hosts file, a router's or a domain's DNS) is taken without waiting for mDNS; otherwise mDNS
  decides — past a proxy that answers the system's lookups — and the system's answer is the
  fallback when nothing on the LAN answers. When mDNS gives IPv6 alone, the system's direct IPv4
  addresses join it, or its proxy address rides along as above. `MdnsHostResolver` over
  `MdnsSocketTransport`: one-shot A/AAAA queries with the QU bit, out of every LAN interface by
  name, heard on their own ports and on 5353; the still unanswered ones sent again at 250 and
  500 ms; a question ends 150 ms after an answer with an IPv4 address, else at ~1 s (an IPv6
  answer alone never ends it: the IPv4 one may come later); answers kept up to 10 s, silence —
  and an answer still without the IPv4 address it asked Bakabase's name for — 5 s, one question
  per name at a time; what Bakabase's name answered is kept for that name too, replacing a stale
  entry. Bakabase's own responder (`MdnsResponder`) answers a one-shot query (source port not
  5353) straight back to its sender, id and question repeated, TTL ≤ 10 s, from this machine's
  links only and apart from the once-a-second multicast limit (RFC 6762 §6.7), so the IPv4
  answer depends on neither the 5353 listener nor one multicast packet over Wi-Fi. Answers drop proxy, loopback and
  unscoped link-local addresses (a link-local one takes the interface it came in on as its
  scope) and addresses this machine holds too, unless those are all there is (`ThisMachine`);
  a name with no IPv4 answer is also reached at `{name}-bakabase.local`, Bakabase's own
  advertisement on that machine. It is only where to connect: identity checks are unchanged.
  Tests use a fake transport (`MdnsHostResolverTests`); none sends real multicast.
- **Discovery hides this node, not its copies.** "Find nearby devices" leaves out this node
  answering from this machine's own addresses; another machine answering under this node's id
  is listed — a copy of this data directory — and connecting to it is refused as
  `SameIdentity`, which is how the user learns of it.

## Changing the protocol

Wire DTOs in `Contracts/` and `Peers/FederationPeerModels.cs` are a protocol between versions.
Add fields as optional trailing members; never change the handshake proof input
(`NodeRequestSignature.HandshakeProof` signs a fixed field list on purpose). Run `yarn gen-sdk`
after any DTO/endpoint change.

## Headless (NAS/Docker)

`BAKABASE_FEDERATION_SHARING=true` turns sharing on at startup; `BAKABASE_NODE_NAME` names the
node; `--federation-invite-on-start` prints a one-time code. The running instance is managed with
`docker exec <c> dotnet Bakabase.Service.dll federation <status|share on|off|invite|approve|reject|revoke|new-identity>`,
which only calls its loopback API. `new-identity` is the devices page's "Make this a new device"
(Advanced → After copying or restoring data), for a copied data directory. The headless
server's browser UI also offers Multi-device → Devices and sharing and the read-only merged
library/map to administrators. Data sync is a child of Multi-device in every window, including
a desktop relay; relay windows still direct library/map access back to their local device. Data sync's counterparts —
`BAKABASE_DATASYNC_SHARING=true` (definitions sharing on at every start) and
`federation datasync <command>` — are in `data-sync.md` ("Headless").

## Tests

- `src/tests/Bakabase.Modules.Federation.Tests` — protocol, pairing, security, queries (fast).
- `src/tests/Bakabase.Tests/Federation` — real middleware/controllers, media, gate matrix.
- `src/tests/federation-smoke/run.py` + `Bakabase.Federation.TestHost` — three real processes;
  then `datasync.py`, data sync across three more (two desktops and a headless server).
- Frontend: `yarn vitest run src/features/federation`.
