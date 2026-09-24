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
| Design history | `docs/multi-device-library-execution-plan.md` |

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
