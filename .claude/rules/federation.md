# Multi-device Identity, Definitions Transport and Devices UI

Desktop and headless Service hosts can manage explicitly paired devices through the ordinary
resource list. Management owns resource access, local metadata replicas, path mappings and
cross-device processing; see `server-switching.md`. Definitions sync is a separate, directional
`datasync.read` protocol; see `data-sync.md`. Neither permission implies the other.

The former read-only shared-library feature was removed during beta, with no compatibility
endpoints, sharing switch, grants, library discovery, query coordinator or media leases.
Do not reintroduce those surfaces. Existing management pairings, mappings and metadata replicas
are untouched. `federation/state.json` still owns the install's NodeId, LibraryEpoch, DisplayName,
definitions grants, requests and peer addresses: removing the old feature must not regenerate
that identity or erase peers that definitions sync still uses. Unknown retired JSON fields are
ignored and disappear on a normal atomic save, not migrated into administrator rights.

## Where things live

| Concern | Location |
|---|---|
| Identity, grants, pairing, persisted state (`AppData/federation/state.json`) | `src/modules/Bakabase.Modules.Federation/{Identity,Peers,Security}` |
| Outbound HTTP, per-peer verified sessions, address relocation | `.../Federation/Transport` |
| Shared media MIME/path boundaries (used by management) | `.../Federation/Media` |
| Host adapters, access middleware, management streaming, definitions pairing flow, NAS CLI | `src/apps/Bakabase.Service/Components/Federation/` |
| Endpoints | `src/apps/Bakabase.Service/Controllers/Federation*.cs` |
| UI | `src/web/src/features/federation/` |
| Device map (`/federation/map`) | `src/web/src/features/federation/map/`, `DeviceMapPage.tsx` |
| Data sync (definitions kept in step over `datasync.read` grants) | see `data-sync.md`; its node side is `src/apps/Bakabase.Service/Controllers/{DataSyncNodeController,FederationDataSyncPairingController}.cs`, `src/apps/Bakabase.Service/Components/Federation/{FederationDataSync*,DataSyncNodeInfoContributor}.cs` and `src/modules/Bakabase.Modules.Federation/Peers/FederationScopes.cs` |
| Devices page (`/federation/devices`) | `DevicesPage.tsx`, `src/web/src/features/federation/devices/` |
| Both pages' data (one hook) | `src/web/src/features/federation/hooks/useDevicesData.ts` |
| Current management design | `docs/multi-device-management.md` |

The whole multi-server mode — management and data sync — is named
「多设备互联」 / "Multi-device" in the UI (`federation.mode`, the menu group, the help topic
`multiDevice`). Route and id names stay `federation`.

## The devices page

`/federation/devices` has four tabs: **本机 / This device** (name and reachable addresses),
**管理 / Management** (devices this one manages and who may manage this one), **数据同步 /
Data sync** (definitions status and the way to `/data-sync`) and **高级 / Advanced** (device
ID and copied/restored-data recovery). The registry is `devices/sections.ts`; navigation uses
links so Back and copied URLs work.

- **`?section=` is a contract.** Tabs are `device`, `manage`, `sync`, `advanced`; supported
  places are `addresses`, `servers`, `add-server`, `management`, `identity`. Producers include
  notifications, configuration, the switcher, help and the device map. A place implies its
  tab (`resolveSection`), then scrolls, focuses and highlights for 2.5 s after loading, once
  per navigation. A missing place and a bare tab focus the heading. Unknown values use the
  device tab without moving focus. Retired sharing anchors are not aliases.
- **One read for the page.** `useDevicesData` reads identity, managed servers, remote access
  and `/data-sync/map` once for both device surfaces. Only the shown tab mounts. Independent
  source failures do not erase other data; counts stay live. The Data sync count is what waits
  on this device (decisions, first sync review, requests), from hub-updated status. Management
  sections still load their own data when embedded outside these pages.
- **Focus** follows the map's rules (`devices/useSectionFocusKeeper.ts`): when an action takes
  away the control that had the keyboard, focus goes to the heading of the part it was in;
  a pointer press or focus moved elsewhere is never pulled back — nor the focus a click gives
  the very button it presses (only a key pressed afterwards hands focus back to the keeper),
  so removing a row clicked with the mouse neither moves focus nor scrolls. Every heading
  focus can go to shows a ring from the keyboard (`focusHeadingClass`), and every place a
  link lands on is named by its heading (`aria-labelledby`). A form behind a button that
  it replaces ("+ 添加要管理的设备") takes the keyboard into its first
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
- **Words.** 配对/配对码 describes management; definitions access uses data sync's own
  wording. Use 允许 (never 批准) for letting a device in and 添加 (never 连接) for adding one
  to a list. Never show 节点, 代际, 旧接口 or 联合浏览. Server messages name the current places:
  设备与分享 → 管理 → 谁可以管理本机, or → 高级 → 复制或恢复数据后 → 设为新设备.
  A message aimed at another device names the page and tab. NAS/Docker operators use the
  same management UI; 配置 → 远程访问 remains available. A requester's name is its own claim
  wherever shown (一台自称 {{name}} 的设备…), not an authenticated device identity.

## The device map

`/federation/map` draws this device in the middle and every relationship it knows of as a
spoke: management (arrow from the manager), data sync (arrow toward the receiving device),
pending requests dashed, devices found nearby as outlines. It reads the listings the
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
  as one. Not where what they said of themselves rules it out — another OS is another
  machine — and never
  for a request's claim, which says whom it claims to be instead. The note names each other
  record by what it is to this device — the device that manages this one, the one supplying
  definitions, one found nearby… — and its address, else its platform (`namesakeDescription`), in
  its text and so in its button's accessible name; two pairings that still read alike add when
  each was paired. Merging decides where a line is drawn, never which identity an action goes
  to.
- **A request someone else filed is a claim.** Incoming data sync and management requests get
  their own node (`sync-request:` / `manager-request:`), marked unverified on the card, in
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
  as that server just now: each candidate address found for that install is probed (`managedServerApi.probe`) and must answer with the
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
  managing pairing's platform (desktop app or phone) — never from a request. Management availability never implies a desktop app: headless servers manage devices too.
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
  node at step 3, so a link to a device that is not a peer yet still has
  a node, and one that ended stays with its outcome and Dismiss (which resets the link). An incoming one is a claim: its own unverified
  `sync-request:{id}` node, deduplicated like management claims and never merged into a
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

- **Management and definitions have separate credentials.** Paired managers use
  `Bakabase-Device` signatures and owner-scoped management APIs. The only retained node scope
  is `datasync.read`; its `Bakabase-Node` credentials must never acquire management rights.
  Management resource and media APIs retain their own identity, epoch, path and capability checks.

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
  (see `data-sync.md`, "Who may create or widen access").
- **A node credential is never a legacy principal.** It must not reach options, resource
  writes, `/hub/ui`, file APIs or legacy pairing. Never map it to `IsPaired`.
- **Default deny.** Every new federation action needs an exact entry in
  `FederationRoutePolicy.Allows`; `FederationGateTests.EveryRealFederationActionHasAnExactAllowedProtocolRoute`
  fails otherwise.
- **Directional grants.** A→B never implies B→A or A→C, and a node never queries on behalf of
  another. Two-way pairing is two grants orchestrated by one flow (a single-use reciprocal code
  bound to the requester's NodeId), not one symmetric grant. A reciprocal datasync code is the
  requester's consent to be read back, and goes when that consent does: revoking the device,
  forgetting it ("Done — stop reading X") or an identity reset drop it, and
  so does withdrawing the request unless another two-way request to that device stands — a
  stale copy of the request approved later reads nothing back.
- **Definitions grants are explicit.** Only `datasync.read` grants are valid; `*` is an
  endpoint declaration for the handshake, never a grant. The `DataSyncSharingEnabled` switch
  gates `info`, definitions pairing and every export before authentication. Definitions pair
  only on `pair/datasync/{request|code|claim}`. Export permits only the handshake and
  `export/datasync/{head|manifest|changes}`; everything else is denied before MVC routing.
  Every Export action declares its scope on `FederationEndpointAttribute`, and
  `FederationLocalAccessFilter` fails closed for absent/mismatched scope metadata.
  `EveryExportActionDeclaresAScope` and the per-action gate matrix pin this boundary.
  Verified sessions use `DataSyncAddress`, with the saved `Address` as a fallback. Preserve
  these addresses and peer identity during retirement; they do not confer resource access.
- **Identity reset is deliberate.** Restoring this device rotates its library epoch, disables
  definitions sharing and revokes inbound definitions access, without dropping outgoing
  management pairings or mappings. Making a copied install a new device first forgets its
  outbound managed credentials locally, then replaces the install/node identity and removes
  inbound management and definitions access. It sends no revoke to servers using the original
  copy's keys. Corrupt-state recovery remains explicit; never silently replace an unreadable
  state at ordinary startup.
- **Local actions stay local.** Playing and opening folders happen on the viewing device with its
  own player configuration. Never launch a program named by a peer; on macOS, packages are only
  revealed (`open -R`), never opened.
- **Peer input is untrusted.** Wire DTOs, metadata pages, mapping trees and media paths are validated and budgeted.
  `MediaPathBoundary` remains shared by management media access. Removed enum values (e.g. old
  `ResourceSource` members) must be filtered before they reach the wire, or peers reject whole blocks.
- **A typed address is read one way, for both features** (`RemoteAddressInput`, behind
  `FederationHttpClient.NormalizeAddress` and `ServerConnector`): full-width `：．。` and digits
  and a leading `\\` are forgiven; typed without a scheme it must name its port — none is
  guessed, since the desktop app's port can change at launch and a Docker server's is its
  own — while `http(s)://` keeps its scheme's port, for a reverse proxy. Anything that is not
  an http(s) host and port is `InvalidAddress`; both are answered before anything is sent.
- **No silent widening or truncation.** Unsupported filters are rejected, unavailable metadata sources are
  reported explicitly, and budget overruns fail rather than returning a truncated success.
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
  address (`DataSyncPeerClientTests`, `FederationNodeDiscoveryTests`, `ConsoleNetworkTests`).
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
  budget (the probe's 2 s). Definitions transport says `ProxyFakeAddress` (503; also the peer's
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

`BAKABASE_NODE_NAME` overrides the device name while set; otherwise the UI can change the
saved display name. An environment-controlled name requires changing startup configuration. The loopback CLI retains
`docker exec <c> dotnet Bakabase.Service.dll federation <status|new-identity|datasync ...>`.
`new-identity` matches Advanced → After copying or restoring data → Make this a new device.
There is no library share/invite CLI or startup switch.

The browser UI offers the ordinary Resources page, Devices and sharing, Device map and Data
sync. Both desktop and headless hosts can manage paired devices; only desktop hosts can open
another device's whole view in a local relay or launch a native player. A browser uses streamed
media. `BAKABASE_DATASYNC_SHARING=true` and `federation datasync <command>` control definitions
only; see `data-sync.md`.

## Tests

- `src/tests/Bakabase.Modules.Federation.Tests` — identity, definitions pairing, direct transport and security (fast).
- `src/tests/Bakabase.Tests/Federation` — management and data sync controllers, media and retired-route gate matrix.
- `src/tests/federation-smoke/run.py` + `Bakabase.Federation.TestHost` — three real processes;
  then `datasync.py`, data sync across three more (two desktops and a headless server).
- Frontend: `yarn vitest run src/features/federation`.
