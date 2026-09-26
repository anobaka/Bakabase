# Data Sync (definitions kept in step between devices)

Data sync (「数据同步」, part of 「多设备互联」) keeps **definitions** — custom properties with
their options, and extension groups — the same on a person's devices. It is pull-only: a
device reads what a peer publishes over federation with a `datasync.read` grant and writes
its **own** database, through its **own** services. There is no remote write path. Library
data (resources, values, files, path marks, play history…) never travels.

Every definition carries a sync key and a version vector. Safe changes apply by themselves;
conflicts, deletions of things in use, type changes and name-only matches wait in the inbox
(“Needs you” / 「待你决定」), and deciding on one device closes the same item everywhere.
Every apply can be undone.

Most rules below are **not** compile-checked. Use the checklists when you add a kind, a
field, a DbSet or an inbox item type.

## Where things live

| Concern | Location |
|---|---|
| Pure engine: kind contracts, identity, canonical JSON, wire, planner, merger, revision rules | `src/modules/Bakabase.Modules.DataSync/` (no EF, ASP.NET, Service, legacy, Property or Federation) |
| Extension group codec | `src/modules/Bakabase.Modules.DataSync/Kinds/ExtensionGroups/` |
| Custom property codec | `src/modules/Bakabase.Modules.DataSync/Kinds/CustomProperties/` |
| Custom property adapter (writes through `ICustomPropertyService`) | `src/modules/Bakabase.Modules.Property/Components/DataSync/` |
| Persistence, apply runner, feed, extension group adapter, DbSet classification | `src/legacy/Bakabase.InsideWorld.Business/Components/DataSync/{Persistence,Apply,Feed,Kinds}/`, `DataSyncDbSetClassification.cs` |
| Scheduler, BTasks, links, inbox, notifications, the `IDataSyncService` facade | `src/legacy/Bakabase.InsideWorld.Business/Components/DataSync/Runtime/`, `DataSyncService.cs` |
| Local UI API `~/data-sync`, identity bridge, host kind, CLI | `src/apps/Bakabase.Service/Controllers/DataSyncController.cs`, `Components/DataSync/` |
| Node routes `~/federation/v1/{pair,export}/datasync/*`, grants, peer client | `src/apps/Bakabase.Service/Controllers/{DataSyncNodeController,FederationDataSyncPairingController}.cs`, `src/apps/Bakabase.Service/Components/Federation/{FederationDataSync*,DataSyncNodeInfoContributor}.cs`; scopes in `src/modules/Bakabase.Modules.Federation/Peers/FederationScopes.cs` |
| Page, drawings, review, inbox, history, map adapter | `src/web/src/features/data-sync/` |
| Hub pushes `DataSyncStatus`, `DataSyncApplied` (`DataSyncHubPublisher`) | `components/SignalR/UIHubConnection.ts` hands them to `applyDataSyncHubData` (`features/data-sync/stores/dataSync.ts`) first |
| Help section | `src/web/src/components/HelpCenter/topics/multiDevice/dataSync/`, `locales/{en,cn}/components/helpDataSync.json` |

The Federation module never references the DataSync module: the Service bridges them
(`FederationDataSyncPeerClient`, `FederationDataSyncGrants`, `FederationPairingFlow` →
`IDataSyncGrantEvents`). Grant, scope and gate rules are in `federation.md` ("Grants have
scopes").

## Checklist: adding a kind

- [ ] **Id** — add it to `DataSyncKindIds` (`^[a-z][A-Za-z0-9]{1,63}$`, never renamed or
  reused) and to `All`, in apply order (topological by `DependsOn`, ties ordinal).
  `src/modules/Bakabase.Modules.DataSync/Abstractions/DataSyncKindIds.cs`
- [ ] **Codec** — a `DataSyncKindCodec<TContent>` implementing every member:
  `Descriptor` (schema version, `DependsOn`, `ContentType`, `AutoLinkIdentical`, `HasOrder`,
  `HasChildren`, `SupportsChildrenLocal`, `ChildNoun`), `Upgrade`, `Read` (peer input:
  validates, drops invalid children, holds invalid entities, never throws), `ReadLocal`
  (`Write(ReadLocal(x))` is byte-identical to `x`), `Write`, `NameOf`/`SubtypeOf`/
  `ChildCountOf`, `MatchNatural`, `Diff`/`Merge`/`PrepareCreate` (the first-link review),
  `ComparisonFormVersion`, `Publish`, `ComparisonForm`, `ChildDeletionCandidates`,
  `Merge3` (all four `DataSyncMerge3Mode`s) and `ChildrenOf`.
- [ ] **Peer regexes** — today's kinds carry no regular expressions. A kind whose content
  carries a pattern from a peer compiles it with a match timeout
  (`new Regex(pattern, options, TimeSpan)`) wherever it is used, and treats an invalid
  pattern or a timed-out match as invalid input, never as a crash.
- [ ] **Adapter** — an `IDataSyncKind` next to the service that owns the table. It writes
  only through that service: no `ExecuteUpdate`/`ExecuteDelete`, raw SQL or hand-built
  `UpdateRange`. `ResetCaches` drops the service's cache after a rollback; after a rollback to
  a savepoint the apply session calls it again once the transaction commits, so it must be safe
  to repeat (derived state it invalidates is invalidated again on every call). Usage counts,
  order, subtype changes and raw-row hashes as the interface asks.
- [ ] **DbSet classification** — the kind's tables are `Synced` with the kind id in
  `DataSyncDbSetClassification` (`DbSetClassificationTests`).
- [ ] **Golden tests** — `RoundTripGoldenTests`, `SchemaGoldenTests` and `WireGoldenTests`
  cover the kind; its codec tests include the closure and symmetry properties below.
- [ ] **Convergence simulator** — `SimNode` emulates the kind, including the owning service's
  own normalization, so `DataSyncConvergenceTests` runs it.
- [ ] **i18n** — `dataSync.kind.{kind}` in `locales/{en,cn}/pages/dataSync.json`.
- [ ] **Help** — `helpCenter.dataSync.types.{kind}.{title,desc}` in `helpDataSync.json` (en and
  cn) and the kind in `syncedKinds` (`WhatSyncsDiagram.tsx`).
  `multiDeviceDataSync.test.tsx` fails until the help lists every `DataSyncKinds` entry.
- [ ] **Constants** — `yarn gen-sdk`; `DataSyncKinds` in `constants.ts` comes from
  `DataSyncKindIds.All`.
- [ ] **No route** — a kind adds no federation route and no gate entry: the feed serves every
  registered codec under `export/datasync/*`, and `NodeInfo.DataSyncKinds` advertises it. An
  older build never requests a kind it does not know (the manifest names kinds), so a new
  kind needs no `DataSyncContract.Version` bump.

## Checklist: adding a field to a synced DTO

- [ ] **Classify it.** *Portable* (content, published and merged), *local-only* (never
  content: local ids, `CreatedAt`, the local integer `Order`, `ValueCount`, overlays, entity
  state, `CreatedBySync`), or *overlay* (a local-only rule on an entity, never published).
- [ ] **Portable** — add it to the content DTO, `Write`/`ReadLocal`/`Read`, the comparison form
  (every field `Merge3` merges must be in it, see the rule below; bump
  `ComparisonFormVersion`), and `Merge3` as a path (scalar, set member, child class or
  appearance). Content never carries a local id: references go through `OptionRef`.
- [ ] **Schema version.** A new **top-level** optional member at the same schema needs no bump:
  older builds preserve unknown top-level members verbatim and merge them per member. A
  **nested** field, or a change of meaning, bumps the kind's `SchemaVersion` and adds an
  `Upgrade` step; older builds then hold such records (`Held(NewerSchema)`) instead of
  half-applying them. Update the goldens — they force the decision.
- [ ] **Comparison form** — if the form changes, bump `ComparisonFormVersion` (see below).
- [ ] **Secrets** — never. `SecretCanaryTests` and `ReferenceInventoryTests` fail on leaks.

## Checklist: adding a DbSet

- [ ] Classify it in `DataSyncDbSetClassification`: `Synced` (with its kind), `NeverSync` or
  `LibraryData`. `DbSetClassificationTests` fails on an unclassified DbSet.
- [ ] A new **whole-row writer** of a synced table (anything that writes a row back from a
  domain object it read earlier) must be covered by Refresh and the lost-update guard, and,
  when it runs as a BTask, conflict with `DataSyncApply`.

## Checklist: adding an inbox item type

- [ ] Decide its **origin**. *Merger-derived* items are re-derived whenever their entity is
  evaluated and at resolve time, belong to one link, and close when the merger no longer
  produces them (or by dominance). *State-derived* items are tied to a piece of local state
  (a hold, a frozen entity, `PublishHeld`, waiting large-change records): they need a
  **closure condition** in `CloseStaleStateItemsAsync` and a **validation** at resolve time,
  and are never closed for not being produced.
- [ ] Its allowed actions, each with a row in the resolve action table and a revision taken
  through `DataSyncRevisionRules`.
- [ ] Its token covers only what the person decides on (type, subject path, the fields'
  base/local/remote), never record hashes or vectors.
- [ ] i18n `dataSync.inbox.type.*` and a card layout in `features/data-sync/components/InboxCard.tsx`.
- [ ] `InboxOriginTests`: resolved with every allowed action after an unrelated pull of the
  same entity.

## Hashes and the comparison form

- **`LocalHash`** is the hash of this device's local canonical content (local ids, local
  order). It detects local changes only and is never compared across devices.
- **`SharedHash`** is the hash of the comparison form of the published content. Every device
  computes it with **its own** codec, for its own entities and for every peer record it
  compares against. It is never read from the wire, so hashes are never compared across
  builds.
- **The comparison-form rule.** The form is invariant under exactly what `Merge3` does not
  transfer (child ids, local child order, duplicate members of a label class, case when the
  property ignores case, a tag group `null` versus `""`), and `Merge3` transfers every
  difference the form keeps. Any codec change that alters the form bumps
  `ComparisonFormVersion`; Refresh then recomputes hashes **without** issuing revisions.
  `FastForwardClosureTests` (`ComparisonForm(Merge3(FastForward)) == ComparisonForm(R)`) and
  `SymmetricMergeTests` must stay green.
- **Codec folding equals the service's.** Label classes and fold keys agree with the
  property's own comparer and normalizer (`LabelKeyCrossCheckTests`,
  `OptionEquivalenceCrossCheckTests`); local content is read through `ReadLocal`, never
  re-normalized by data sync.

## Engine rules

- **Pending records, not copies.** A peer record this device did not agree to is stored once,
  on its base row (per link and entity), with a reason. Items carry its hash, never a copy.
  Nothing unapplied is lost, and nothing blocks the cursor.
- **Revisions only through `DataSyncRevisionRules`.** Never write `VvJson` by hand. `Seq`
  comes from `NextSeq` and never goes backwards.
- **BTasks.** The `DataSync` task only fetches (conflict key `DataSync`), so it never waits
  behind the enhancer. Every task that writes definitions conflicts with `DataSyncApply`,
  `Enhancement`, `SyncResources` and `SyncPathMarks`. Fixed-id one-shot tasks are enqueued
  with `EnqueueOnce`. Every task decision carries its token. Cancel goes through the
  attempt-id pattern (flag set before the status is read; the body checks flag and attempt id
  after `YieldAsync` and after entering the gate). `OperationCanceledException` is rethrown
  unchanged, never wrapped.
- **Actor checks** run after entering the gate and before any transaction; a rotation never
  runs inside an open apply transaction.
- **A caller that joins Refresh to its transaction** commits only while the actor is still
  verified, writes `actor.json` after the commit, and runs a retry in a new scope. Outside the
  runner that is `IDataSyncLocalChangeRunner` (the refresh coordinator): an entity setting goes
  through it, never through a hand-written transaction.
- **Only a committed apply is recorded as a sync.** `DataSyncAutoSyncOutcome.End` says how an
  auto-sync apply ended; the apply task records a link as synced, consumes its once flags and
  completes its first contact only for `Committed`. `Failed` keeps the runner's `ApplyFailed`
  and backoff; `NotApplied` (the attempt ended, the actor unverified or changing) puts the pull
  and a requested re-merge back for the next run.
- **A first sync is always in the history** (§8.3). The pull that completes a link's first
  contact — the approver's first pull, or its "Start anyway" — writes a `FirstLink` entry (the
  page's "First sync" with the device), even when it only raised link suggestions or conflicts;
  the initiator's review writes its own. Every other pull writes an `AutoSync` entry only when it
  applied something.
- **Retired actors outlive retention while a restore waits** (§4.6, B1(a)). An actor lost with
  the restore is in no stored vector, yet "This device's definitions win" must cover what it
  issued, so retention forgets retired actors no vector names only while no restore is pending.
- **Reads take no write lock.** A read-only read of the local state (the review page) runs in a
  deferred transaction, never EF's `BEGIN IMMEDIATE`, so a page load neither waits for nor
  holds up a writer.
- **The gate and link rows.** The `/data-sync` actions §10.1 gates (links, pause all, reset,
  applying a review, resolving, entity settings, undo, the restore choice) wait for the
  `DataSyncGate` at most 30 s, then answer `Busy` having changed nothing; reads never wait, and
  a call that asks a peer for access releases the gate around that network call
  (`OutsideGateAsync`). Two departures from §10.1's gate column, both on purpose:
  approving a request takes **no** gate for its link row (so an approval never waits behind an
  apply), and `PUT /data-sync/sharing` takes it for `newDefinitionsStayLocal` only, after the
  switch — while the gate is busy the switch still applies and the answer is `Busy` with the
  detail `newDefinitionsStayLocal`. Its `enabled` is optional: without it the switch is left as
  it is, which is how "share new definitions automatically" is sent, so it never sends back a
  sharing value read before sharing changed elsewhere, and only `enabled: true` widens access. Withdrawing a request is not gated either. Link rows are
  ordered by `DataSyncLinkService`'s lock plus one `BEGIN IMMEDIATE` transaction per write, not
  by the gate: the apply runner reads a link row inside the write transaction that writes it
  back, never writes back a row it read before that transaction began, and re-checks "Pause
  all" and the link once it holds the gate.
- **One fetch per peer.** `IDataSyncPeerClient.AcquireFetchAsync` holds the peer's fetch lock
  from the head to the last page; a second fetch waits up to 30 s, then gets `Busy`. The
  fetcher takes it for every fetch — the cycle, review staging, copy once and "Fetch again" —
  in the method that makes the head, manifest and page calls (a hold returned out of an async
  helper is not seen by its caller's calls), so no fetch discards at the source the snapshot
  another is reading (one snapshot per grant).
- **A staged review waits for its person** (§8.3). One per link awaiting a review, in memory,
  never evicted to make room for another — only applied ones, kept for their result screen,
  are — so links awaiting reviews never make each other fetch full snapshots every minute. The
  fetch cycle and the status views only peek (`PeekForLink`), so a review nobody reads idles out
  after an hour and the next cycle fetches it again. "Ready to review" is announced once per link
  and set of kinds while the link waits, not per staged review. "Fetch again" replaces a review
  only once the new one is staged; a failed fetch leaves it where it was.
- **Wire pages are raw canonical bytes**, written and parsed with data sync's own options —
  never `FederationJson`, whose depth limit rejects a deep multilevel property.
- **A pull is budgeted in count and time, not only bytes.** The `DataSync` task fetches its due
  links one after another, so a source must not be able to hold it. The page reader discards
  the pull on a page that is not the last and carries nothing (the writer never makes one), on
  more records than the manifest counted, and past `RecordCount × (1 + MaxChunksPerEntity) + 1`
  pages; the fetcher gives a pull up as `Unreachable` (`timeout`) once it outlasts
  `DataSyncSchedule.SnapshotDeadline` (10 min, a restart included), on top of each call's own
  deadline and `MaxStagedPullBytes`.
- **A state-derived item commits with its state.** A merge drafts one only when it makes the
  state (a hold), and a later pull meets that state already agreed (row K4) and drafts nothing.
  So a chunked apply writes the state-derived items of a chunk's entities in that chunk's own
  transaction (`DataSyncMergeWriter`), never only at the end.
- **Undo is faithful, or the step is taken back** (§8.11). Each step runs under a savepoint. A
  deletion is re-created from its captured content verbatim (`CreateEntityOperation.FromPreImage`);
  a type change goes back through `IDataSyncKind.RestoreAsync` with the captured raw row, never a
  subtype change alone (which rebuilds children with fresh ids, F73); a change list removes a
  child only together with everything now under it that the same list removes (a child added or
  moved under it since is a conflict). The entity re-read after each step must have the canonical
  content the step restored, or the step is refused as `ChangedSinceImport`.
- **Nothing a decision writes back removes a child in use.** "Put the synced change back"
  (`Reapply`) checks usage like a merge (§8.5.4 step 3) and undo (`AddedOptionsInUse`): while
  resources use a child it would remove, it writes nothing and the item names them
  (`Detail = reapplyInUse`).

## Links, requests and access

- **`AskAccessAgain` is four actions, by the link's state**, and always counts as creating
  access (§7.1.5):
  - on `Paused(PeerReset)` (not a restore): B1's "Ask X for access again" — a new request with
    the link's mode, `Initiator = ThisDevice`. The link waits in `AwaitingAccess` with its
    bases, pending records and items, keeping `PeerReset` as the mark that a reset is due;
    only **once it is granted** is it reset (the row is deleted and a new one made for the same
    peer, mode and kinds — the link id changes — its items close `LinkRemoved`, and a new first
    contact runs against the new epoch). A request that ends without access — rejected,
    withdrawn, expired or no longer listed — takes it back to `Paused(PeerReset)` with
    `LastErrorCode` saying why;
  - on `AccessRevoked` (the peer revoked this reader, removed the device or replaced its grant;
    also `AccessMissing`): "Ask X for access again" — a new request (two-way when the link is and
    the peer does not read this device), the link waiting in `AwaitingAccess` with everything it
    had, back to where it was once granted. Never offered for `PeerSharingOff`;
  - on `AwaitingAccess`: "Try again" — a fresh request (a Follow request for an approver whose
    read-back failed, which the peer approves; the link's mode otherwise);
  - on a working two-way link with `ReadBackDeclined`: "Ask X to keep in step" — an ordinary
    two-way request with a reciprocal offer; bases and items stay. When the peer already reads
    this device, it only clears the note.
- **Dead credentials are never access.** The two "Ask X for access again" actions forget the
  datasync credentials this device holds for the peer (`ForgetOutboundAsync`) before the request
  goes out, so that only the answer can read as access. A link waiting for a reset grant goes by
  its own request alone (`granted`, or the claim loop's event), never by `HasOutboundGrantAsync`.
- **`ReadBackDeclined`** ("X does not read this device") is set when the peer answers a two-way
  request or code without reading back: a code made without two-way consent, or a two-way
  request approved without read-back — the approval stores `readBack = declined` on the
  exchange and the claim carries it to the requester's `OutboundGranted`. It is cleared only
  once the peer does read this device (any grant this device issues it, or a claim that says
  `started`), never when a request is merely filed.
- **A request that ends.** Rejected or expired, the link stops (`Mode` Off, its last mode,
  bases and pending records kept) and stays on the map with Dismiss. The listing never shows an
  expired request — it is simply gone, as is one deleted with the peer's datasync state — so a
  request the link waits for that is no longer listed has ended as expired, once no grant has
  arrived. Withdrawn
  (`DELETE /data-sync/requests/{id}`), the `AwaitingAccess` link made for it goes with it when
  it has nothing yet (no first contact, cursor or base); one with sync state stops as above
  (`AccessCancelled`). A link that asked a reset peer again goes back to `Paused(PeerReset)`
  either way. "Dismiss" is `DELETE /data-sync/links/{id}` (reset), which never withdraws a
  request, and withdrawing never resets a link.
- **Withdrawn consent takes the reciprocal codes with it.** Revoking a reader, "Done — stop
  reading X", removing the peer and an identity reset drop the datasync codes this device
  minted for that device to read it back with; cancelling an outgoing request drops them
  unless another unexpired two-way request to that device is waiting or granted. A stale copy
  of the request approved later then reads nothing back.
- **Offered addresses** a two-way request came with are kept on the peer
  (`DataSyncOfferedAddresses`) for "Try again", used only when neither `DataSyncAddress` nor
  `Address` is known, always expecting the peer's NodeId, never shown as its address, and
  cleared once a datasync address is verified.

## Invariants — do not weaken

- **No destructive action without a decision.** Never delete a definition that has values, or
  an option used by resources here, without a person. The only automatic entity deletion is
  one that sync created here, that nothing changed here since, that has no values and no
  open item or pending record. Destructive decisions back the database up first by default.
- **Writes only through services.** Adapters call the owning service; nothing writes around it.
- **No local ids in content**, and **no secrets**, ever.
- **The key invariant.** Within a kind, a key is exactly one of: a live primary, a tombstoned
  primary or an alias. `DataSyncIdentityStore` is its only writer and enforces it. Undo never
  removes a side row or an alias.
- **Nothing is merged by name alone.** Name matches are suggestions (link, keep both, skip);
  the only exception is an extension group identical in name and extensions.
- **Type changes always ask**, even lossless ones.
- **Federation.** Every new federation route needs an exact `FederationRoutePolicy` entry, a
  `RequiredSharing` value and gate-matrix rows; every Export action declares its scope.
  `library.read` is never reachable from `/data-sync`.
- **Who may create or widen access.** Only this device's own window, a paired device or the
  CLI (and `BAKABASE_DATASYNC_SHARING` at start) may turn definitions sharing on, approve a
  definitions request, create a code, or send a request / mint a reciprocal code
  (`AskAccessAgain` included). The controller refuses what always widens access; for links and
  copy once it passes who is asking, and the link service refuses exactly the calls that would
  send a request or mint a code. An unpaired browser admitted only by Unrestricted mode may
  reduce access, never widen it (`NotAllowedOnThisDevice`). **What that holds against:** it
  refuses callers that have not paired, so it is only as strong as the way to a device key.
  On an Enabled server — pairing required or not — an unpaired caller has none: remote access's
  management routes (approve a pairing request, issue a code, manage devices) are for the host
  and paired devices only, never `[RemoteAccessible]` (G31b). On an Unrestricted server any LAN
  caller can pair itself — its browser is the operator there, and it is where a headless
  server's pairing requests are answered and codes issued — so there the rule only makes a
  caller pair first (a device listed on the server, revocable), never keeps it out.
- **A GET never writes** (`LoopbackCrossSiteGuard` lets cross-site GETs through). Reviews
  re-plan read-only.
- **The inbox order is a contract.** `GET /data-sync/inbox` lists open items first, then closed
  ones, newest first in each group: the page reads the closed ones from `skip = openTotal`. With
  `kind` and `localKey` it answers one definition's items whole, so conflicts that must be
  resolved together are never split across pages. Views count bases with
  `IDataSyncStore.CountBasesAsync`, never by reading every base.
- **`/data-sync` times are UTC**, read on the web through `parseServerTime`.
- **Offline is said by the error alone.** `Unreachable` and `Busy` are offline, grey
  (`DataSyncViews.OfflineCodes`, the web's `offlineErrors`); `ApplyFailed`, `FetchFailed`,
  `InvalidResponse` (with Retry) and `TooLarge` (a line of its own) are failures — on the page,
  the map and the indicator alike. A link's `peerOnline` is false after every restart until its first head and
  after every failure, so it never makes a device offline. The indicator's reason is the error
  of a link that set its level; a failed read-back's reason is its `LastErrorDetail`.
- **The indicator is `Off` only when there is nothing** (§11.3): no live link, no reader, no
  request waiting here, nothing to decide and no restore (`DataSyncViews.GetStatus`). The status
  carries `PendingRequests`, `Readers`, `LinksToReview` and `LinksWaiting`, so a quiet device's
  line says what it is: a first sync ready to review here, waiting for another device's approval
  or review (never "Syncing…"), only read by others, or requests waiting for an answer.
- **What a reader declares** (§7.5.6) is `ok|awaitingReview|waitingForPeerReview|paused:{reason}|
  needsYou:{n}` — `waitingForPeerReview` being the reader waiting for this device's review. The
  readers list maps each word explicitly and shows nothing for a word it does not know; it never
  builds a key from a wire value (a missing key renders as the key itself).
- **This device's counts** (`DataSyncOverview.Kinds`) come from the kinds' own rows, less those
  kept local or detached (and, while new definitions stay local, those no Refresh has met):
  never from what Refresh last published, which a device no peer has read has none of.
- **A device found only nearby is asked at its address.** A link or copy once to a device the
  server does not know sends its `peerNodeId` and the `address` it answered at; the wizard and
  the map do alike. Sharing the wizard turned on for a request that then failed is turned off
  again (remote access, a setting of its own, stays).
- **Copy.** The feature is 数据同步 / Data sync; never 配置同步, 配置包 or 分享给他人. “Needs
  you” is 待你决定 (待处理 is taken). Device names are never quoted — no «», “ ” or 「」
  around `{{name}}`. No copy (help, menu, page, notice, legend, rule text) describes a kind or
  capability before it works. An English string with a `{{count}}` noun has a `_one` form
  beside it (i18next resolves it); Chinese needs none, and the locale test holds both to that.
  A switch's label is quoted with “ ” in both languages, as the help does.

## Headless (NAS/Docker)

The desktop app and a headless server run the same code; they differ only in notifications
(`IDataSyncHostKind.IsHeadless`, the kind the install reports about itself through
`IServerSelfDescription`). A headless one creates none — neither the notifier's nor a new
request's — and says so in its heads (`Attention.Headless`).

- `BAKABASE_DATASYNC_SHARING=true` turns definitions sharing on at **every** start
  (`DataSyncSharingAnnouncer`), together with remote access (pairing required) only when that
  is `Disabled` — a Docker install's `Unrestricted` is never touched. Turning sharing off (in a
  window or with `share off`) therefore lasts only until the next start while it is set, and
  the CLI says so. A failure to turn it on is logged and never stops the server.
- `docker exec <c> dotnet Bakabase.Service.dll federation datasync <command> [--port <port>]`
  calls only its loopback `/data-sync/*` API (`DataSyncCli`; the port defaults to
  `API_LISTENING_PORTS`, `ASPNETCORE_HTTP_PORTS`, then 8080). Commands: `status` (sharing,
  remote access, links with what waits on each peer, readers, requests), `share on|off`,
  `invite [--two-way]` (a code and this device's addresses), `requests` (pending ones, with
  the claim warning), `approve <requestId> [--no-receive-back]` (a two-way request is read
  back unless told not to), `reject <requestId>`, `revoke <nodeId>`, and
  `pause [<nodeId>]` / `resume [<nodeId>]` (every link, or the link with that device). There
  is no CLI inbox and the CLI cannot start a link: decisions waiting on a hub are made through
  server switching (the hub's own `/data-sync` page), or close by themselves when a desktop
  decides the same thing.
- A hub tells its readers how many decisions wait on it (`Attention` in every head), so the
  desktops show "NAS has N changes waiting for a decision".

## Known limits (accepted, documented)

- **Two undetected restore cases.** (1) A whole-AppData restore that no direct reader has read
  since, whose lost revisions reached other devices only through a hub, with every peer
  offline for the first two minutes and an edit before any evidence arrives. (2) Wider: a
  device with readers but **no `Active` link of its own** (a NAS that only publishes) is
  verified at once, so after a whole-AppData restore local edits made before its first reader
  reads can lift `LastSeq` past that reader's cursor. In both, readers meet equal vectors with
  different content from that device's actor and pause (`PeerIdentityDuplicated`); nothing
  merges wrongly.
- **The cache window.** The property service's cache is shared across scopes: a request-scoped
  whole-row writer can read content an apply wrote but rolled back, and write it back. It is
  what the merge intended, so it converges with no item.
- **No "receive only" loops longer than two.** Two devices receiving from each other work as
  two-way; A → B → C → A can rotate values and is not supported.
- **Case-only renames** of options of a property that ignores case stay on that device.
- **Saved filters** that used an option sync removed lose that condition; usage counts
  resource values only.

## Tests

- `src/tests/Bakabase.Modules.DataSync.Tests` — engine: identity, wire, merger rows, revision
  rules, closure/symmetry, anomalies, convergence simulator (the test kind, extension groups
  and custom properties, whose simulator kind emulates the Property service's own folding).
  Every random run is seeded; CI keeps the project near a minute. For longer local runs:
  `DATASYNC_FUZZ_SEED`/`DATASYNC_FUZZ_RUNS` (simulator, merger fuzz) and
  `DATASYNC_MERGE3_RUNS` (custom property closure and symmetry). A seed a longer run finds
  failing is pinned as a `DataRow`; `SymmetricMerge_KnownGaps_StillDiffer` lists the few
  custom property inputs that still merge apart.
- `src/tests/Bakabase.Tests/DataSync/**` — persistence, apply, feed, guardrails
  (`DbSetClassificationTests`, `SecretCanaryTests`), `Api/**` endpoints, `TwoHost/**`.
- `src/tests/Bakabase.Tests/Federation/**` — gate matrix, scopes, pairing.
- `src/tests/federation-smoke/datasync.py` — three real processes (two desktops, a headless
  server with `BAKABASE_DATASYNC_SHARING`) driven through their loopback APIs and the headless
  CLI: pairing for definitions with a two-way read-back, first reviews and pulls, a conflict
  decided once, an in-use option held, a reset peer, a newer-schema record held, and headless
  silence. `run.py` runs it after the library checks (`--skip-datasync` leaves it out), so
  CI's federation job does; every TestHost offers other devices the loopback address it listens
  on, and `BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA` exists for it (see its README).
- Version-skew fixtures (§13.8), rewritten with `DATASYNC_WRITE_FIXTURES=<dir>`: pages and heads
  in `Bakabase.Modules.DataSync.Tests/Fixtures/VersionSkew`, and the `state.json` an older build
  must read in `Bakabase.Modules.Federation.Tests/Fixtures/VersionSkew`.
- Frontend: `yarn vitest run src/features/data-sync src/components/HelpCenter`.
- Browser: `src/tests/federation-browser-smoke/data-sync.cjs`, the last stage of that smoke's `run.py`
  — started on the device map, approved in the window switched to the server, a conflict decided on
  the page, the rule editor from the keyboard at 1440 and 1280 px, 375 px, and a LAN browser (the
  test host's `BAKABASE_FEDERATION_TEST_LAN_PORT`) in and outside Unrestricted mode.
