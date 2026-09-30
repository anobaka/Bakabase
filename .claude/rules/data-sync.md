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
| Pure engine: kind contracts, identity, canonical JSON, wire, merger, revision rules | `src/modules/Bakabase.Modules.DataSync/` (no EF, ASP.NET, Service, legacy, Property or Federation) |
| Extension group codec | `src/modules/Bakabase.Modules.DataSync/Kinds/ExtensionGroups/` |
| Custom property codec | `src/modules/Bakabase.Modules.DataSync/Kinds/CustomProperties/` |
| Custom property adapter (writes through `ICustomPropertyService`) | `src/modules/Bakabase.Modules.Property/Components/DataSync/` |
| Persistence, apply runner, feed, extension group adapter, DbSet classification | `src/legacy/Bakabase.InsideWorld.Business/Components/DataSync/{Persistence,Apply,Feed,Kinds}/`, `DataSyncDbSetClassification.cs` |
| Scheduler, BTasks, links, inbox, notifications, the `IDataSyncService` facade | `src/legacy/Bakabase.InsideWorld.Business/Components/DataSync/Runtime/`, `DataSyncService.cs` |
| Local UI API `~/data-sync`, identity bridge, host kind, CLI | `src/apps/Bakabase.Service/Controllers/DataSyncController.cs`, `Components/DataSync/` |
| Node routes `~/federation/v1/{pair,export}/datasync/*`, grants, peer client | `src/apps/Bakabase.Service/Controllers/{DataSyncNodeController,FederationDataSyncPairingController}.cs`, `src/apps/Bakabase.Service/Components/Federation/{FederationDataSync*,DataSyncNodeInfoContributor}.cs`; scopes in `src/modules/Bakabase.Modules.Federation/Peers/FederationScopes.cs` |
| Page, drawings, first-sync preview, inbox, history, map adapter | `src/web/src/features/data-sync/` |
| The Devices page's Data sync tab (`?section=sync`: a summary and the way to `/data-sync`) | `src/web/src/features/federation/devices/SyncTab.tsx`, registered in `devices/sections.ts` |
| Hub pushes `DataSyncStatus`, `DataSyncApplied` (`DataSyncHubPublisher`) | `components/SignalR/UIHubConnection.ts` hands them to `applyDataSyncHubData` (`features/data-sync/stores/dataSync.ts`) first |
| Help section (its steps — a link's five, a rename conflict's three — are numbered text; only the ways to sync and what syncs are drawn, a deviation from spec §11.7's step drawings) | `src/web/src/components/HelpCenter/topics/multiDevice/dataSync/`, `locales/{en,cn}/components/helpDataSync.json` |

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
  `HasChildren`, `SupportsChildrenLocal`, `ChildNoun`), `Read` (peer input:
  validates, drops invalid children, holds invalid entities, never throws), `ReadLocal`
  (`Write(ReadLocal(x))` is byte-identical to `x`), `Write`, `NameOf`/`SubtypeOf`/
  `ChildCountOf`, `MatchNatural`, `PrepareCreate` (a create), `ComparisonFormVersion`, `Publish`, `ComparisonForm`,
  `Merge3` (all four `DataSyncMerge3Mode`s) and `ChildrenOf`.
- [ ] **Peer regexes** — today's kinds carry no regular expressions. A kind whose content
  carries a pattern from a peer compiles it with a match timeout
  (`new Regex(pattern, options, TimeSpan)`) wherever it is used, and treats an invalid
  pattern or a timed-out match as invalid input, never as a crash.
- [ ] **Adapter** — an `IDataSyncKind` next to the service that owns the table. It writes
  only through that service: no `ExecuteUpdate`/`ExecuteDelete`, raw SQL or hand-built
  `UpdateRange`, and everything it writes joins the apply's transaction, since a refused undo
  step or Convert is taken back by rolling the whole transaction back. `ResetCaches` drops the
  service's cache after a rollback. Usage counts, order, subtype changes and raw-row hashes as
  the interface asks.
- [ ] **DbSet classification** — the kind's tables are `Synced` with the kind id in
  `DataSyncDbSetClassification` (`DbSetClassificationTests`).
- [ ] **Golden tests** — `RoundTripGoldenTests`, `SchemaGoldenTests` and `WireGoldenTests`
  cover the kind; its codec tests include the closure and symmetry properties below.
- [ ] **Convergence test** — the real-stack `DataSyncConvergenceTests` creates and edits the
  kind through its own service in its steps, and its covered paths name them.
- [ ] **i18n** — `dataSync.kind.{kind}` in `locales/{en,cn}/pages/dataSync.json`.
- [ ] **Help** — `helpCenter.dataSync.types.{kind}.{title,desc}` in `helpDataSync.json` (en and
  cn) and the kind in `syncedKinds` (`WhatSyncsDiagram.tsx`).
  `multiDeviceDataSync.test.tsx` fails until the help lists every `DataSyncKinds` entry.
- [ ] **Constants** — `yarn gen-sdk`; `DataSyncKinds` in `constants.ts` comes from
  `DataSyncKindIds.All`.
- [ ] **No route** — a kind adds no federation route and no gate entry: the feed serves every
  registered codec under `export/datasync/*`, and its heads and manifests name it. An older
  build never requests a kind it does not know (the manifest names kinds), so a new kind needs
  no `DataSyncContract.Version` bump.

## Checklist: adding a field to a synced DTO

- [ ] **Classify it.** *Portable* (content, published and merged), *local-only* (never
  content: local ids, `CreatedAt`, the local integer `Order`, `ValueCount`, overlays, entity
  state, `CreatedBySync`), or *overlay* (a local-only rule on an entity, never published).
- [ ] **Portable** — add it to the content DTO, `Write`/`ReadLocal`/`Read`, the comparison form
  (every field `Merge3` merges must be in it, see the rule below; bump
  `ComparisonFormVersion`), and `Merge3` as a path (scalar, set member, child class or
  appearance). Content never carries a local id: references go through `OptionRef`.
- [ ] **One contract.** Any change to a kind's content bumps its `SchemaVersion` and
  `DataSyncContract.Version` (with it `MinimumPeerVersion`, which is always the same): devices on
  different contracts do not sync at all ("update X"), and a record of another schema version —
  received or stored before an update — is held, never upgraded or half-applied. There is no
  unknown-member preservation: a member a codec does not know is ignored with a warning. Update
  the goldens — they force the decision.
- [ ] **Comparison form** — if the form changes, bump `ComparisonFormVersion` and the contract
  (see below).
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
  produces them (or by dominance, except `LinkSuggestion` and `IdentityConflict`, which no
  vector settles). At resolve time a closed item's records are merged again at once, so a
  record never waits without its item, and a card whose target the merger no longer offers
  is updated, never applied. *State-derived* items are tied to a piece of local state
  (a hold, a frozen entity, `PublishHeld`): they need a
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
  `ComparisonFormVersion` and `DataSyncContract.Version`; Refresh then recomputes this device's
  hashes **without** issuing revisions. Heads carry no form version: peers on one contract
  compute one form, so row A2 reads unequal forms under equal vectors as a duplicate actor when
  the revision is this device's or the source's own, and as drift only for a third device's
  revision or one naming no editor.
  `FastForwardClosureTests` (`ComparisonForm(Merge3(FastForward)) == ComparisonForm(R)`) and
  `SymmetricMergeTests` must stay green; the symmetric forms are asserted for every input
  without a multilevel class of several members (see Known limits).
- **Codec folding equals the service's.** Label classes and fold keys agree with the
  property's own comparer and normalizer (`LabelKeyCrossCheckTests`,
  `OptionEquivalenceCrossCheckTests`); local content is read through `ReadLocal`, never
  re-normalized by data sync.

## Engine rules

- **Pending records, not copies.** A peer record this device did not agree to is stored once,
  on its base row (per link and entity), with a reason. Items carry its hash, never a copy.
  Nothing unapplied is lost, and nothing blocks the cursor. Every apply of the link merges all
  of them again; one runs without a pull only for a `Retry` record, a local change since the
  link's last apply (LastSeq past an in-memory watermark; unknown after a start) or a resume —
  never merely because records wait, or it would apply every minute and hold the enhancer.
- **A conflict freezes its definition.** While any field of a peer record conflicts, nothing of
  that record applies (only its new keys and the child classes it matched are recorded); it
  waits as a `Conflict` pending record with its items. A decision writes its value here as a
  local edit, marks the paths decided on the pending record (`DataSyncMergeFlags.DecidedPaths`)
  and merges it again, so the rest applies and the base takes the record — never an edit of the
  stored base, which row A2 needs as the peer's real record.
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
  runs inside an open apply transaction. Only a check changes the actor: evidence reported
  outside the gate (a head's `SeenCounter`, the feed's reader-ahead check) is only queued, with
  the counter recorded when it came as its baseline, and the next check handles it — the next
  gate holder's, or the scheduler's tick while it waits (`TryEnter(0)`). Counters an apply that
  was already running issues meanwhile never hide it: it is judged against its baseline. A head
  serves a reader found ahead from 0 at once (`Recorded || Ahead`).
- **A caller that joins Refresh to its transaction** writes `actor.json` after the commit.
  Outside the runner that is the refresh coordinator's `RunLocalChangeAsync`: an entity setting
  goes through it, never through a hand-written transaction. A Refresh that finds the actor
  changed (an identity reset racing the call) rolls back, and a request answers `Busy`.
- **Only a committed apply is recorded as a sync, by the runner alone.** The runner writes
  the link's bookkeeping in its final transaction (`DataSyncLinkColumns.RecordApplied`:
  cursors, errors cleared, synced time, first contact — complete once any kind still waiting
  for one was pulled — and the full reconciliation, only for a pull the fetch half tags
  `ReconcilesLink`), and its outcome says whether that was the first sync, which the task only
  announces. A failure rolls back and propagates; the task records `ApplyFailed` and one
  backoff. `NotApplied` (the attempt ended, the actor unverified or changing) puts the pull and
  a requested re-merge back for the next run.
- **Deletions and size never pause a link.** A pull applies whatever it changes or creates,
  however many definitions, as one undoable history entry. When a peer deletes more
  definitions of a kind than B2 allows (new deletions only: one already asked about is not
  new), every deletion of that kind in that pull becomes a `DeletedThere` question and the rest
  applies; a kind the peer stops offering is recorded as `MissingAtPeer` by the next full
  reconciliation. Links carry no once flags.
- **A first sync is always in the history** (§8.3). The pull that completes a link's first
  contact — the initiator's Start, the approver's first pull, or its "Start anyway" — writes a
  `FirstLink` entry (the page's "First sync" with the device), even when it only raised link
  suggestions or conflicts; a copy once's Start writes a `CopyOnce` entry. Every other pull
  writes an `AutoSync` entry only when it applied something.
- **Retired actors are never forgotten** (B1(a)). An actor lost with a restore is in no stored
  vector, yet "This device's definitions win" must cover what it issued. Retention leaves them;
  rotation keeps at most `MaxActorsPerVector`, dropping the lowest counters, never one it retired.
- **Reads take no write lock.** A read-only read of the local state (a first sync's preview)
  runs in a deferred transaction, never EF's `BEGIN IMMEDIATE`, so a page load neither waits
  for nor holds up a writer.
- **The gate and link rows.** The gated `/data-sync` actions (Off, reset, pause all,
  resolving, entity settings, undo, the restore choice) wait for the `DataSyncGate` at most
  30 s, then answer `Busy` having changed nothing; reads never wait. Departures from §10.1's
  gate column, on purpose: creating, changing (other than Off), pausing and resuming a link and
  a first sync's Start (its task takes the gate) take **no** gate, and neither does approving a
  request for its link row (so none waits
  behind an apply, and a call that asks a peer for access never holds the gate over the
  network); and `PUT /data-sync/sharing` takes it for `newDefinitionsStayLocal` only, after the
  switch — while the gate is busy the switch still applies and the answer is `Busy` with the
  detail `newDefinitionsStayLocal`. Its `enabled` is optional: without it the switch is left as
  it is, which is how "share new definitions automatically" is sent, so it never sends back a
  sharing value read before sharing changed elsewhere, and only `enabled: true` widens access. Withdrawing a request is not gated either, unless it
  stops a link (below). Link rows are
  ordered by `DataSyncLinkService`'s lock plus one `BEGIN IMMEDIATE` transaction per write, not
  by the gate: the apply runner reads a link row inside the write transaction that writes it
  back, never writes back a row it read before that transaction began, and re-checks "Pause
  all" and the link once it holds the gate.
- **Stops and resets hold the gate.** Every stop or reset of a link — Off, Reset/Dismiss, a
  request that ends or is withdrawn, a link that starts over from a fresh row — releases the
  link's holds (must-fix 28), which rewrites entity rows, and only a gate holder writes those: an
  apply holds the gate while it runs. A gated action passes its `DataSyncGateHold` down; the
  fetch half enters the gate itself, without a limit, before the link service's lock and
  transaction (`WriteUnderGateAsync`). A link that starts over (below) and withdrawing a request
  that stops a link wait for the gate at most 30 s and answer `Busy`; withdrawing one that drops
  a link made for the request (nothing merged, so nothing held) stays ungated.
- **One fetch per peer.** `DataSyncFetcher.FetchLinkAsync` holds a per-peer lock from the head
  to the last page; a second fetch waits up to 30 s, then gets `Busy`. Every fetch goes through
  it — the cycle's pulls and a first sync's snapshot alike — and nothing else calls a peer's
  feed, so no fetch discards at the source the snapshot another is reading (one snapshot per
  grant). The peer clients take no lock.
- **A first sync is the ordinary merge, previewed** (§8.3). The device that starts a link (or
  a copy once) fetches the peer's whole snapshot once and keeps it in memory for the link
  (`DataSyncRuntimeState.StagePreview`) until a Start applies it or the link stops or goes; a
  restart loses it and the next cycle fetches it again. The fetch refreshes the link's kinds
  under the gate before it stages the snapshot (a device nobody read has no local state yet).
  `GET /data-sync/links/{id}/first-sync` runs the merger over it read-only, against the last
  committed local state (no Refresh, no write), and lists what the merge would do with each record: create, update, delete, a name
  match, another question, held, not synced or unchanged. Start (`POST …/first-sync`) runs that
  merge as the `DataSyncReview:{linkId}` task through the auto-sync apply, only while the link
  still awaits it; a Skip excludes the record on the link (`Skipped`) first. There is no per-field
  choice and no plan: a name match waits under "Needs you" as a `LinkSuggestion` after the Start,
  and the card says what linking takes on a Follow link. A Start that did not commit drops the
  snapshot: the next cycle fetches and previews it again, against the definitions as they are then. "Ready to review" is announced once while the link waits.
- **The reader waits for the first sync to settle.** A device's head tells its reader that the
  first contact is complete (`DataSyncFeedCounterpart.FirstContactCompleted`) only once the
  first sync committed and no item is open on the link, so the reader's first pull meets the
  name matches already answered instead of asking them again.
- **A copy once is the link's mode, read when it applies.** A Start is a copy once only while
  its link is `Off`, as read in the apply's transaction: a Follow merge without a base
  (`DataSyncCopyOnce`) that takes the peer's values whoever edited them here last and no
  deletion from either side — neither the peer's nor a revival of what this device deleted — and
  so removes no child. Its name matches are answered in the preview (link to one of the
  candidates, or keep both under a new name); a name match the preview did not show (a definition
  made here since) makes the Start apply nothing. The link stops in the same transaction, with a `CopyOnce` entry. A copy once turned into
  Follow or two-way before its Start — the rule editor, or approving the peer's two-way request —
  drops its snapshot, and the next cycle stages the link's own first sync. A copy once onto a
  stopped link starts over from a fresh row: once its request (if any) succeeded, the stopped row
  goes with its bases and pending records and a new one is made, under the gate.
- **Wire pages are raw canonical bytes**, written and parsed with data sync's own options —
  never `FederationJson`, whose depth limit rejects a deep multilevel property.
- **A record travels whole or not at all.** A page is at most `MaxPageBytes` (3.5 MiB, pinned
  below federation's 4 MiB response cap). A definition whose published content is larger than a
  page leaves room for (`MaxContentBytes`), or that has more options than a reader accepts, is
  held at the source as `TooLarge`: its readers hold it with that reason, and this device's own
  pages mark it and offer "sync the definition only". There are no chunks.
- **A pull is budgeted in count and time, not only bytes.** The `DataSync` task fetches its due
  links one after another, so a source must not be able to hold it. The page reader discards
  the pull on a page that is not the last and carries no record (the writer never makes one), and
  on more records than the manifest counted, so a kind takes at most `RecordCount + 1` pages;
  the fetcher gives a pull up as `Unreachable` (`timeout`) once it outlasts
  `DataSyncSchedule.SnapshotDeadline` (10 min, a restart included), on top of each call's own
  deadline and `MaxStagedPullBytes`.
- **An auto-sync apply is one transaction**, a first sync's Start included. Refresh, the merge, every write, the
  inbox, the history entry and the cursor commit together, or nothing does: every committed apply
  has its history entry, so it can be undone and the lost-update guard sees it. There is no
  per-transaction time bound; `ApplyTransactionTests` holds a first sync of 500 properties, one
  with 10,000 options, to 5 s. After Refresh the session forgets the rows it tracked
  (`DataSyncApplySession.ForgetTrackedAsync`) and reads the link row again. A resolution batch
  runs in parts of about 2 s, each a complete attempt — the gate, the actor check, Refresh, its
  own `Resolution` entry — with the gate and the write lock free between them, where a pause
  waits; a large bulk decision can so leave several entries. As defence in depth an
  automatic deletion carries `DeleteEntityOperation.RequireNoValues`, which an adapter with values
  checks before it deletes: a content hash never covers values.
- **The lost-update window is measured to the change** (§6.5). Refresh cannot tell when a change
  was made, only that it came after the kind's last committed Refresh began
  (`DataSyncLocalStates.RefreshedAtJson`), so it judges a change against every guarded apply of the
  window before that moment, however late it runs. The scheduler refreshes once each window has
  closed (`DataSyncRefreshCoordinator.CloseLostUpdateWindowsAsync`), so a deliberate revert after it
  is an ordinary revision again; `RefreshedAtJson` is written only while a guarded apply lies in
  the window, so heads do not write the row for nothing.
- **Local child ids follow a rebuild.** A subtype change rebuilds the children with fresh ids
  (F73). Convert (and undo converting back) maps holds, local-only children and every link's child
  map to the rebuilt children by label class (`IDataSyncKindCodec.MapChildrenByClass`,
  `DataSyncChildIdRemap`) before anything is merged or published; a question about a held child
  that no longer exists closes `Superseded` and releases the hold, never `ResolvedHere`.
- **Nothing half converted is published.** While a Convert's second step waits for a decision
  (a `TypeChange` record still pending over an entity phase one gave its type), Refresh publishes
  the entity held (`PendingDecision`) and issues no revision; it publishes again, under a revision,
  once the decision merges the record or the link forgets it.
- **Undo is faithful, or the step is taken back** (§8.11). A step refused only after it wrote,
  or key moves that cannot all return, roll the whole transaction back, and the undo runs again
  without them (`DataSyncRunWithoutException`; a Convert whose phase two cannot run defers its
  item the same way). A
  deletion is re-created from its captured content verbatim (`CreateEntityOperation.FromPreImage`);
  a type change goes back through `IDataSyncKind.RestoreAsync` with the captured raw row, never a
  subtype change alone (which rebuilds children with fresh ids, F73); a change list removes a
  child only together with everything now under it that the same list removes (a child added or
  moved under it since is a conflict). The entity re-read after each step must have the canonical
  content the step restored, or the step is refused as `ChangedSinceImport`. An undo whose every
  step is refused undoes nothing and its task fails `UndoNotAvailable`, naming the refused steps —
  never completes with its entry still undoable.
- **Nothing a decision writes back removes a child in use.** "Put the synced change back"
  (`Reapply`) checks usage like a merge (§8.5.4 step 3) and undo (`AddedOptionsInUse`): while
  resources use a child it would remove, it writes nothing and the item names them
  (`Detail = reapplyInUse`). One with an undone change that no longer fits the content (a
  conflict) writes nothing either, keeps the hold and offers Publish only (`reapplyUnavailable`).
  The hold and Reapply judge what was undone by one rule (`DataSyncChangeLists.IsUndone`).

## Links, requests and access

- **A peer that refuses a link is its error code, not a state.** `AccessRevoked` (also what
  `AccessMissing` is stored as), `PeerSharingOff`, `PeerRemoteAccessOff`, `PeerTooOld` and
  `ThisTooOld` stay on the link as `LastErrorCode` (`HasPeerError`) while it keeps its state;
  retried hourly (every 6 h for a version), cleared when the peer answers again, and a new code is
  a transition that pushes the status. Meanwhile the link neither applies nor re-merges
  (`IsRunning`), offers no "Start anyway", and counts neither as waiting nor as to review; an
  apply never clears such a code. The page reads the code before the state, unless paused or
  stopped.
- **`AskAccessAgain` is four actions, by the link's state and code**, and always counts as
  creating access (§7.1.5):
  - on `Paused(PeerReset)` (not a restore): B1's "Ask X for access again" — a new request with
    the link's mode. Once it is out the link starts over from a fresh row, under the gate: the
    old row is deleted (its items close `LinkRemoved`) and a new one is made for the same peer,
    mode and kinds, `Initiator = ThisDevice` — the link id changes — waiting in `AwaitingAccess`
    (or `AwaitingReview` when granted at once) for a new first contact against the new epoch.
    It then ends like any request: rejected or expired it stops with Dismiss, withdrawn it goes;
  - on `AccessRevoked` (the peer revoked this reader, removed the device or replaced its grant;
    also `AccessMissing`): "Ask X for access again" — a new request (two-way when the link is and
    the peer does not read this device), the link waiting in `AwaitingAccess` with everything it
    had, back to where it was once granted. Never offered for `PeerSharingOff`;
  - on `AwaitingAccess`: "Try again" — a fresh request (a Follow request for an approver whose
    read-back failed, which the peer approves; the link's mode otherwise);
  - on a working two-way link with `ReadBackDeclined`: "Ask X to keep in step" — an ordinary
    two-way request with a reciprocal offer; bases and items stay. When the peer already reads
    this device, it only clears the note.
- **Dead credentials are never access.** "Ask X for access again" — on `AccessRevoked` and on
  `Paused(PeerReset)` alike — forgets the datasync credentials this device holds for the peer
  (`ForgetOutboundAsync`) before the request goes out, so that only the answer can read as access
  and a fresh link never reads the revoked ones as its grant. Where the pause stood for another
  install answering at the peer's address (`IdentityConflict`), that costs one more approval.
- **Codes grant one way.** A definitions code lets whoever redeems it read this device and is
  never read back: two-way consent is given only when approving a request. The redeemer sends it
  as a Follow request, with no offer to be read back (a two-way link made with it says "X does not
  read this device"), so no code of its own is minted for it. Two-way with a device reached by
  code (a typical Docker NAS) is: redeem, then "Ask X to keep in step", then approve on X — an
  approval that always says it replaces existing access.
- **The approver writes its own link.** Approving a request writes the approver's link (and
  clears "X does not read this device") directly, with `CancellationToken.None` once the grant
  is issued, so a page closed mid-approval never loses it; a failed read-back is the answer's
  `ReadBackFailed` on that link. Only a redeemed code reaches the runtime as an event
  (`InboundGranted`).
- **`ReadBackDeclined`** ("X does not read this device") is set when the peer answers a two-way
  request or code without reading back: a two-way link made with a code, or a two-way request
  approved without read-back — the approval stores `readBack = declined` on the exchange and the claim
  carries it to the requester's `OutboundGranted`. It is cleared only once the peer does read
  this device (any grant this device issues it, or a claim that says `started`), never when a
  request is merely filed.
- **A request that ends.** Rejected or expired, the link stops as Off stops it
  (`StopLinkAsync`: `Mode` Off, its last mode, bases and pending records kept, its items closed
  `LinkStopped`, its holds local-only) and stays on the map with Dismiss. The listing never shows an
  expired request — it is simply gone, as is one deleted with the peer's datasync state — so a
  request the link waits for that is no longer listed has ended as expired, once no grant has
  arrived. Withdrawn
  (`DELETE /data-sync/requests/{id}`), the `AwaitingAccess` link made for it goes with it when
  it has nothing yet (no first contact, cursor or base); one with sync state stops as above
  (`AccessCancelled`). "Dismiss" is `DELETE /data-sync/links/{id}` (reset), which never withdraws a
  request, and withdrawing never resets a link.
- **One record per device.** The /data-sync page and the device map read the same
  `GET /data-sync/map`: one `DataSyncMapPeer` per device with a link, a `datasync.read` grant or a
  waiting request of this device's own, carrying its link view (as `/data-sync/links` has it), its
  reader view and this device's own request (`DataSyncOwnRequest`: waiting, with the id [Cancel]
  withdraws, or ended until dismissed — also a request no link carries: "Ask X to keep in step",
  "Try again", one left after a Reset), plus the requests other devices filed. The page shows
  this device's own requests on their device and lists incoming ones only; a failed read keeps
  the whole view as it was, with the error beside it.
- **Removing a device ends its link too.** "Remove device" (`DELETE /federation/local/peers/{id}`)
  removes both scopes' grants with the peer, then resets this device's links to that node as
  Dismiss does (`ResetLinkAsync`: every definition kept, the link, its bases and pending records
  forgotten, its items closed). A link left behind would find its access gone on the next pull,
  read as `AccessRevoked` — "X stopped sharing", blaming the device just removed — and put it
  back on the map. The Service reaches data sync there through `IDataSyncService`, resolved per
  request; a host without data sync has nothing to reset.
- **Withdrawn consent takes the reciprocal codes with it.** Revoking a reader, "Done — stop
  reading X", removing the peer and an identity reset drop the datasync codes this device
  minted for that device to read it back with; cancelling an outgoing request drops them
  unless another unexpired two-way request to that device is waiting or granted. A stale copy
  of the request approved later then reads nothing back.
- **Offered addresses** a two-way request came with are kept on the peer
  (`DataSyncOfferedAddresses`) for "Try again", used only when neither `DataSyncAddress` nor
  `Address` is known, always expecting the peer's NodeId, never shown as its address, and
  cleared once a datasync address is verified.
- **A request under a reader's NodeId says what approving takes away.** Approving revokes the
  live `datasync.read` grant held under the NodeId the request claims, and a two-way request
  approved to receive back also replaces how this device reads that NodeId. The claim warning
  (`ClaimsKnownDevice`) fires only where both addresses are IP literals, so
  `ReplacesExistingAccess` is carried on its own — on the requests listing, the map's
  `DataSyncMapRequest` and the CLI's `requests` — and every approval surface (the card, its
  confirmation, the CLI) says it, whatever the address says.

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
- **Nothing is merged by name alone.** Name matches are suggestions (link, keep both, skip),
  answered under "Needs you" or, for a copy once, in its preview; the only exception is an
  extension group identical in name and extensions. A definition already agreed with another
  record of the same link is never offered or taken for a link (the question offers Keep both
  and Skip): two of the peer's records would bind to it and merge without a base from then on.
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
- **A GET never writes** (`LoopbackCrossSiteGuard` lets cross-site GETs through). A first
  sync's preview merges read-only.
- **The inbox order is a contract.** `GET /data-sync/inbox` lists open items first, by definition
  (kind, local key, newest first), then closed ones newest first: the page reads up to 5,000 open
  items in one read, then the closed ones from `skip = openTotal`. A page never ends inside a
  definition — a full one brings the rest of its last — so conflicts that must be resolved
  together are never split; past 5,000 the page says how many it shows. Views count bases with
  `IDataSyncStore.CountBasesAsync`, never by reading every base.
- **`/data-sync` times are UTC**, read on the web through `parseServerTime`.
- **Offline is said by the error alone.** Only `Unreachable` is offline, grey
  (`DataSyncViews.OfflineCodes`, the web's `offlineErrors`). `Busy` — the peer answered busy (its
  snapshot limit, its gate, a signature outside its clock window) or this device's own fetch of
  it was still running — is a peer that is there and is tried again within minutes: "Syncing…",
  online, never offline or failed. `ApplyFailed`, `FetchFailed`, `InvalidResponse` (with Retry)
  and `TooLarge` (a line of its own) are failures — on the page, the map and the indicator alike.
  The indicator's reason is the error of a link that set its level; a failed read-back's reason
  is its `LastErrorDetail`. A failed read-back (`ReadBackFailed`) is a failure there too, never
  counted in `LinksWaiting`: it waits for this device's own "Try again", and the status carries
  the detail (`LastErrorDetail`).
- **A task's end is said.** What a task pushes while it runs counts it as syncing, so every data
  sync task body — the fetch cycle and each write task — tells the observer when it is over
  (`IDataSyncRuntimeObserver.TaskEndedAsync`), and the status pushed then leaves that task out
  (`DataSyncViews.IsSyncing(endingTaskId)`); a waiting task a cancel removes is pushed from the
  cancel. The page's header reads the status the hub pushes, as the indicator does.
- **Following a task the page started.** Its id may be listed already — a restore chosen again,
  an undo retried — so the page takes `listedTasks()` before sending the request and never takes
  that earlier run for the new one. "Needs you" and the history follow their decisions and undos
  with one `useTaskFollower`: a task that fails says so on its card or row; one that completes,
  was listed and then is not, or was never listed for 30 s, is over once a read after it is in.
  The restore panel and the first sync follow theirs with `useDataSyncTask(id, earlier)`; the
  restore panel disables its choices until its task is over and reads the restore again then,
  and also whenever the overview or the hub says something new about it. An undo that completes
  with its entry still undoable says nothing was undone.
- **A problem's detail is not copy.** `DataSyncProblem.Detail` is a token, an id or an English
  sentence. The UI words the details that change what a problem means
  (`wordedProblemDetails`, `dataSync.problem.detail.{code}.{detail}`) and shows any other only
  under a collapsed "Technical details". Confirmations go through `DataSyncConfirmDialog`, so a
  problem is never read as a network failure; a failed task is worded from its brief error when
  that names a problem code (`taskFailureText`), never from its full error.
- **A reset peer reads as reset, not revoked.** A reset revokes the grant, so a request on a
  datasync session verified before it (the factory reuses one for a minute) is refused
  `GrantRevoked`; the peer client verifies the session again once (`PeerSessionFactory.Invalidate`),
  whose info shows the new epoch — `PeerReset`. The pause clears the link's earlier error.
- **The indicator is `Off` only when there is nothing** (§11.3): no live link, no reader, no
  request waiting here, nothing to decide and no restore (`DataSyncViews.GetStatus`). The status
  carries `PendingRequests`, `Readers`, `LinksToReview` and `LinksWaiting`, so a quiet device's
  line says what it is: a first sync ready to start here, waiting for another device's approval
  or first sync (never "Syncing…"), only read by others, or requests waiting for an answer.
- **What a reader declares** (§7.5.6) is `ok|awaitingReview|waitingForPeerReview|paused:{reason}|
  needsYou:{n}` — `waitingForPeerReview` being the reader waiting for this device's first sync. The
  readers list maps each word explicitly and shows nothing for a word it does not know; it never
  builds a key from a wire value (a missing key renders as the key itself).
- **This device's counts** (`DataSyncOverview.Kinds`) come from the kinds' own rows, less those
  kept local or detached (and, while new definitions stay local, those no Refresh has met):
  never from what Refresh last published, which a device no peer has read has none of.
- **A device found only nearby is asked at its address.** A link or copy once to a device the
  server does not know sends its `peerNodeId` and the `address` it answered at; the wizard and
  the map do alike, through the one rule editor (`SyncRuleDrawing`): the wizard only chooses the
  device, or an address and a code, and asks its questions inside its dialog. Sharing the rule
  editor turned on for a request that then failed is turned off again (remote access, a setting
  of its own, stays).
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
  `API_LISTENING_PORTS`, `ASPNETCORE_HTTP_PORTS`, then 8080). Like the library's CLI it prints
  the API's answer as JSON (enums as numbers, times in UTC); a refusal prints `Refused: {code}`
  and exits 1. Commands: `status` (overview, links, readers and requests), `share on|off`,
  `invite` (a one-way code and this device's addresses), `requests` (pending ones, in prose, with
  the claim warning and the warning that approving replaces a reader's access),
  `approve <requestId> [--no-receive-back]` (a two-way request is read back unless told not
  to), `reject <requestId>`, `revoke <nodeId>`, and `pause [<nodeId>]` / `resume [<nodeId>]`
  (every link, or the link with that device). There
  is no CLI inbox and the CLI cannot start a link: decisions waiting on a hub are made through
  server switching (the hub's own `/data-sync` page), or close by themselves when a desktop
  decides the same thing.
- A hub tells its readers how many decisions wait on it (`Attention` in every head), so the
  desktops show "NAS has N changes waiting for a decision".

## Known limits (accepted, documented)

- **Two undetected restore cases.** After a start the actor is verified once one fetch cycle
  has asked every `Active` link's peer (answered or unreachable), else after two minutes.
  (1) A whole-AppData restore that no direct reader has read since, whose lost revisions
  reached other devices only through a hub, with every peer unreachable in that first cycle
  and an edit before any evidence arrives. (2) Wider: a
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
- **Options without an id** (legacy or corrupt rows) are never published nor merged into: a
  peer's option of the same label is added beside one, and both show. Under IgnoreCase the
  next edit in the property editor folds them.
- **Same-named sibling nodes.** A multilevel class of several members (two sibling nodes with
  one label, or labels that differ in case under IgnoreCase), one with children, is not merged
  part by part: a peer node without a counterpart here goes under its own parent only when the
  peer added or changed it, else with the class's first node. The two directions of a
  merge may then keep, colour or claim what sits below such nodes differently for one more
  sync; the next pull settles it as any concurrent change (a differing key or parent asks, an
  unused option one side dropped goes, one in use is held with a question).
- **Saved filters** that used an option sync removed lose that condition; usage counts
  resource values only.

## Tests

- `src/tests/Bakabase.Modules.DataSync.Tests` — engine: identity, wire, merger rows, revision
  rules, closure/symmetry, anomalies. Every random run is seeded; CI keeps the project near a
  minute. For longer local runs: `DATASYNC_FUZZ_SEED`/`DATASYNC_FUZZ_RUNS` (merger fuzz) and
  `DATASYNC_MERGE3_RUNS` (custom property closure and symmetry). A seed a longer run finds
  failing is pinned as a `DataRow`. `SimulatorFindingsTests` keeps, as single
  merges, the defects the retired in-memory simulator found.
- `src/tests/Bakabase.Tests/DataSync/Convergence/**` — the convergence property test (§13.3) on
  the real stack: seeded random scenarios over 2–4 `TwoHostNode` hosts (pair, chain, a star
  around a headless hub, mesh; two-way, Follow, mutual Follow) with every step people and the
  hosts' loops take, I4 and I8 checked after each step, then quiescence (a deterministic
  chooser, rounds until nothing changes, daily full reconciliations) and I1, I2, I5, I10. CI
  runs 12 seeds with a covered-paths gate; `DATASYNC_CONVERGENCE_SEED`/`_RUNS` run others. A
  seed replays its steps, but keys and option ids differ from run to run, so a failure prints
  its steps and every host's items, entities and bases. Beside it: §9.1's design examples over the
  same DSL (`SyncWorld`) and the realistic week (§9.4's noise budget,
  `DATASYNC_WEEK_SEED`).
- `src/tests/Bakabase.Tests/DataSync/**` — persistence, apply, feed, guardrails
  (`DbSetClassificationTests`, `SecretCanaryTests`), `Api/**` endpoints, `TwoHost/**`. The
  `Runtime/**` and `Api/**` tests run the real composition over SQLite (`DataSyncHarness`) and
  fake only what a test plays or breaks: the peers, the grant service, the actor guard, the apply
  runner, the page reader, the gate and, for two ordering tests, the row transactions.
- `src/tests/Bakabase.Tests/Federation/**` — gate matrix, scopes, pairing.
- `src/tests/federation-smoke/datasync.py` — three real processes (two desktops, a headless
  server with `BAKABASE_DATASYNC_SHARING`) driven through their loopback APIs and the headless
  CLI: pairing for definitions with a two-way read-back, first syncs (name matches linked under
  "Needs you") and pulls, a conflict
  decided once, an in-use option held, a reset peer, a record of another schema held, and headless
  silence. `run.py` runs it after the library checks (`--skip-datasync` leaves it out), so
  CI's federation job does; every TestHost offers other devices the loopback address it listens
  on, and `BAKABASE_DATASYNC_TEST_FUTURE_SCHEMA` exists for it (see its README).
- The `state.json` an older build must read (§13.8), rewritten with `DATASYNC_WRITE_FIXTURES=<dir>`:
  `Bakabase.Modules.Federation.Tests/Fixtures/VersionSkew`. Data sync itself has no cross-build
  fixtures: one contract number (§8.12).
- Frontend: `yarn vitest run src/features/data-sync src/components/HelpCenter`.
- Browser: `src/tests/federation-browser-smoke/data-sync.cjs`, the last stage of that smoke's `run.py`
  — started on the device map, approved in the window switched to the server, a conflict decided on
  the page, the rule editor from the keyboard at 1440 and 1280 px (its mode buttons changing
  nothing under the arrow keys), 375 px, and a LAN browser (the
  test host's `BAKABASE_FEDERATION_TEST_LAN_PORT`) in and outside Unrestricted mode.
