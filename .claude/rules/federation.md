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
| Design history | `docs/multi-device-library-execution-plan.md` |

The whole multi-server mode — sharing, management, and later data sync — is named
「多设备互联」 / "Multi-device" in the UI (`federation.mode`, the menu group, the help topic
`multiDevice`). Route and id names stay `federation`.

## The device map

`/federation/map` draws this device in the middle and every relationship it knows of as a
spoke: library sharing (arrow towards the device that may browse), management (arrow from the
manager), pending requests dashed, devices found nearby as outlines. It reads only the three
listings the devices page reads — `/federation/local/peers`, `/federation/local/servers`,
`/remote-access/settings` — plus both discoveries on request, and acts through the same
endpoints and confirmations.

- **Records are merged on evidence only** (`map/graph.ts`). The install id first and always:
  a peer's NodeId is the install's remote-access ServerId unless the node was reset
  (`FederationNodeIdSource`), so a peer, a managed server and a beacon with one id are one
  device — even while the server's address answers as another. An address (never a
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
- **`sync` is a reserved edge kind.** The renderer and legend know it; nothing produces it,
  and the legend hides it until something does. Never describe data sync as available.

## Invariants — do not weaken

- **The federated view is read-only.** Through `/federation/*` a device browses, searches, views
  and plays other devices' resources; it never edits, deletes, moves or runs tasks on them.
  Managing another server is a different feature with a different credential: the desktop app
  switches its window to that server's own UI through a signed loopback relay using the legacy
  `Bakabase-Device` pairing (see `server-switching.md`). Never route management through the node
  protocol, and never let an admin device key travel on `/federation/v1`.

- **Two interfaces, never mixed.** `/federation/local/*` is for this device's own UI: real
  loopback socket + loopback `Host` + matching `Origin` (`FederationAccessMiddleware.IsLocalCaller`).
  `/federation/v1/*` is node-to-node: `export/*` always needs a `Bakabase-Node` signature, even
  from loopback or in `Unrestricted` mode.
- **A node credential is never a legacy principal.** It must not reach options, resource
  writes, `/hub/ui`, file APIs or legacy pairing. Never map it to `IsPaired`.
- **Default deny.** Every new federation action needs an exact entry in
  `FederationRoutePolicy.Allows`; `FederationGateTests.EveryRealFederationActionHasAnExactAllowedProtocolRoute`
  fails otherwise.
- **Directional grants.** A→B never implies B→A or A→C, and a node never queries on behalf of
  another. Two-way pairing is two grants orchestrated by one flow (a single-use reciprocal code
  bound to the requester's NodeId), not one symmetric grant.
- **Local actions stay local.** Playing and opening folders happen on the viewing device with its
  own player configuration. Never launch a program named by a peer; on macOS, packages are only
  revealed (`open -R`), never opened.
- **Peer input is untrusted.** Wire DTOs are validated and budgeted (`QueryProtocol`,
  `FederationMediaSessions.Remember`, `MediaPathBoundary`). Removed enum values (e.g. old
  `ResourceSource` members) must be filtered before they reach the wire, or peers reject whole blocks.
- **No silent widening or truncation.** Unsupported filters are rejected, partial coverage is
  reported per node, budget overruns fail explicitly.
- **Proxies are bypassed** (`UseProxy = false`), matching the desktop app's relays.

## Changing the protocol

Wire DTOs in `Contracts/` and `Peers/FederationPeerModels.cs` are a protocol between versions.
Add fields as optional trailing members; never change the handshake proof input
(`NodeRequestSignature.HandshakeProof` signs a fixed field list on purpose). Run `yarn gen-sdk`
after any DTO/endpoint change.

## Headless (NAS/Docker)

`BAKABASE_FEDERATION_SHARING=true` turns sharing on at startup; `BAKABASE_NODE_NAME` names the
node; `--federation-invite-on-start` prints a one-time code. The running instance is managed with
`docker exec <c> dotnet Bakabase.Service.dll federation <status|share on|invite|approve|reject|revoke>`,
which only calls its loopback API.

## Tests

- `src/tests/Bakabase.Modules.Federation.Tests` — protocol, pairing, security, queries (fast).
- `src/tests/Bakabase.Tests/Federation` — real middleware/controllers, media, gate matrix.
- `src/tests/federation-smoke/run.py` + `Bakabase.Federation.TestHost` — three real processes.
- Frontend: `yarn vitest run src/features/federation`.
