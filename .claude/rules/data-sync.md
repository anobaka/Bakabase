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
  `UpdateRange`. `ResetCaches` drops the service's cache after a rollback. Usage counts,
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
- **Wire pages are raw canonical bytes**, written and parsed with data sync's own options —
  never `FederationJson`, whose depth limit rejects a deep multilevel property.

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
  CLI may turn definitions sharing on, approve a definitions request, create a code, or send
  a request / mint a reciprocal code. An unpaired browser admitted only by Unrestricted mode
  may reduce access, never widen it (`NotAllowedOnThisDevice`).
- **A GET never writes** (`LoopbackCrossSiteGuard` lets cross-site GETs through). Reviews
  re-plan read-only.
- **`/data-sync` times are UTC**, read on the web through `parseServerTime`.
- **Copy.** The feature is 数据同步 / Data sync; never 配置同步, 配置包 or 分享给他人. “Needs
  you” is 待你决定 (待处理 is taken). Device names are never quoted — no «», “ ” or 「」
  around `{{name}}`. No copy (help, menu, page, notice, legend, rule text) describes a kind or
  capability before it works.

## Headless (NAS/Docker)

The desktop app and a headless server run the same code; they differ only in whether this
install creates notifications (`IDataSyncHostKind.IsHeadless`: a headless one never does).

- `BAKABASE_DATASYNC_SHARING=true` turns definitions sharing on at **every** start, together
  with remote access (pairing required) only when that is `Disabled` — a Docker install's
  `Unrestricted` is never touched.
- `docker exec <c> dotnet Bakabase.Service.dll federation datasync <status|share on|off|invite|requests|approve|reject|revoke|pause|resume>`
  calls only its loopback `/data-sync/*` API. There is no CLI inbox and the CLI cannot start a
  link: decisions waiting on a hub are made through server switching (the hub's own
  `/data-sync` page), or close by themselves when a desktop decides the same thing.
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
- Frontend: `yarn vitest run src/features/data-sync src/components/HelpCenter`.
