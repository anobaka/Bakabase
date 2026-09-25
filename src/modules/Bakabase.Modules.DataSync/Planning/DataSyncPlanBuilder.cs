using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// One run of <see cref="DataSyncPlanner.Plan"/>: v3.1 §7.2 over a staged pull, with §8.3's refinements.
/// </summary>
/// <remarks>
/// Per kind, in apply order:
/// <list type="number">
/// <item>An unsupported or held kind holds every entity. Tombstones take no part: a review never removes.</item>
/// <item>A held entity is Held with its reason. A record bound by key only to rows kept out of sync (LocalOnly,
/// Detached) is ignored, as the merger ignores it (§5.2, row X).</item>
/// <item>Pass 1, by key: one Synced match bound by one record is key-bound (an unreadable one holds the item as
/// LocalUnreadable; an incoming vector at or below the local one is Unchanged with no changes, LocalIsNewer when the
/// local side is strictly newer; another subtype is TypeMismatch; else Update or Unchanged by the diff). Two matches,
/// or one match two records bind, are IdentityConflict.</item>
/// <item>Pass 2, by name, over the locals no record claimed by key: Create, Link, AmbiguousNameMatch,
/// NameClashDifferentType or DuplicateInPackage (v3.1 §5.2 steps 2–4).</item>
/// </list>
/// </remarks>
internal sealed class DataSyncPlanBuilder
{
    private readonly DataSyncPlanInput _in;

    public DataSyncPlanBuilder(DataSyncPlanInput input) => _in = input;

    public DataSyncPlan Build()
    {
        var staged = _in.Incoming.Kinds.GroupBy(k => k.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var sections = DataSyncKindOrder.Of(staged.Keys, _in.Codecs).Select(kind => PlanKind(staged[kind])).ToList();

        var warnings = new List<DataSyncPlanWarning>();
        if (_in.OwnNodeId is { } own && own == _in.Incoming.Manifest.NodeId)
            warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.FromThisDevice, null, null));

        var items = sections.SelectMany(s => s.Items).ToList();
        var summary = new DataSyncPlanSummary(
            sections.SelectMany(s => s.Items.GroupBy(i => i.Type).OrderBy(g => g.Key)
                .Select(g => new DataSyncKindTypeCount(s.Kind, g.Key, g.Count()))).ToList(),
            items.Count(i => i.RequiresConfirmation), items.Count(i => i.BulkLinkEligible),
            items.Count(i => i.Type == DataSyncPlanItemType.Held));

        var plan = new DataSyncPlan("", SnapshotContentHash(_in.Incoming.Manifest), sections, summary, warnings);
        return plan with { PlanId = DataSyncPlanFormat.PlanId(plan) };
    }

    /// <summary>§2.6: the hash of the manifest's kind hashes, as one canonical object keyed by kind.</summary>
    internal static string SnapshotContentHash(DataSyncFeedManifest manifest)
    {
        var hashes = new JsonObject();
        foreach (var kind in manifest.Kinds)
        {
            if (!hashes.ContainsKey(kind.Kind)) hashes[kind.Kind] = kind.ContentHash;
        }

        return ContentHash.Of(hashes);
    }

    private DataSyncPlanKindSection PlanKind(DataSyncStagedKind staged)
    {
        var codec = _in.Codecs.GetValueOrDefault(staged.Kind);
        var supported = staged.Supported && codec is not null;
        var local = new DataSyncReviewLocal(_in.Local.GetValueOrDefault(staged.Kind),
            _in.LocalState.GetValueOrDefault(staged.Kind), codec);
        var incoming = DataSyncReviewIncoming.Of(staged, codec);

        var kind = new KindPlan(this, staged.Kind, codec, local);
        if (!supported || staged.KindHeld is not null)
        {
            var reason = supported ? staged.KindHeld!.Value : DataSyncHeldReason.UnknownKind;
            foreach (var r in incoming) kind.Items.Add(kind.Held(r, reason, null));
        }
        else
        {
            kind.Run(incoming);
        }

        var items = kind.Items.OrderBy(i => i.Incoming.Position).ThenBy(i => i.ItemId, StringComparer.Ordinal).ToList();
        var localOnly = local.Entities.Count(l => !kind.Claimed.Contains(l.LocalKey) && !kind.UniqueBest.Contains(l.LocalKey));
        return new DataSyncPlanKindSection(staged.Kind, staged.SchemaVersion, supported, items, localOnly);
    }

    /// <summary>The planning state of one kind.</summary>
    private sealed class KindPlan(DataSyncPlanBuilder owner, string kind, IDataSyncKindCodec? codec, DataSyncReviewLocal local)
    {
        public List<DataSyncPlanItem> Items { get; } = [];

        /// <summary>Local keys some record is bound to by key: never a natural candidate.</summary>
        public HashSet<string> Claimed { get; } = new(StringComparer.Ordinal);

        /// <summary>Local keys that are some pending item's unique best candidate.</summary>
        public HashSet<string> UniqueBest { get; } = new(StringComparer.Ordinal);

        private IDataSyncKindCodec Codec => codec!;

        public void Run(IReadOnlyList<DataSyncReviewIncoming> incoming)
        {
            var bound = new List<(DataSyncReviewIncoming R, IReadOnlyList<LocalIdentifiedEntity> Matches)>();
            var pending = new List<DataSyncReviewIncoming>();
            foreach (var r in incoming)
            {
                if (r.Keys is null)
                {
                    Items.Add(Held(r, r.Entity.Held ?? DataSyncHeldReason.Invalid, null));
                    continue;
                }

                var matches = local.Matches(r.Keys);
                if (r.Entity.Held is not null || r.Entity.Content is null)
                {
                    // A held record still owns its local match: nothing else is proposed for it.
                    Claimed.UnionWith(matches.Select(m => m.LocalKey));
                    Items.Add(Held(r, r.Entity.Held ?? DataSyncHeldReason.Invalid, matches.Count == 1 ? matches[0] : null));
                    continue;
                }

                if (matches.Count == 0 && local.IsNotSyncedHere(r.Keys)) continue;
                if (matches.Count == 0) pending.Add(r);
                else bound.Add((r, matches));
            }

            // ---- pass 1: by key --------------------------------------------------------------------
            var bindings = bound.SelectMany(b => b.Matches).GroupBy(m => m.LocalKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var (r, matches) in bound)
            {
                Claimed.UnionWith(matches.Select(m => m.LocalKey));
                Items.Add(matches.Count == 1 && bindings[matches[0].LocalKey] == 1
                    ? KeyBound(r, matches[0])
                    : IdentityConflict(r, matches));
            }

            // ---- pass 2: by name -------------------------------------------------------------------
            var proposals = pending.Select(Propose).ToList();
            var bests = proposals.Where(p => p.Best is not null).GroupBy(p => p.Best!.LocalKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var p in proposals)
            {
                if (p.Best is { } best) UniqueBest.Add(best.LocalKey);
                Items.Add(Pending(p, p.Best is { } b && bests[b.LocalKey] > 1));
            }
        }

        // ---- pass 1 ------------------------------------------------------------------------------

        private DataSyncPlanItem KeyBound(DataSyncReviewIncoming r, LocalIdentifiedEntity m)
        {
            if (m.Unreadable) return Held(r, DataSyncHeldReason.LocalUnreadable, m);

            // §8.3: newer local content is never regressed, and the same revision is no change. Only vectors that
            // both carry a revision say anything.
            var incomingVv = r.Entity.Record.Vv;
            if (local.VvOf(m.LocalKey) is { Counters.Count: > 0 } localVv && incomingVv.Counters.Count > 0 &&
                incomingVv.CompareTo(localVv) is DataSyncVvRelation.Equal or DataSyncVvRelation.DominatedBy)
            {
                var newer = incomingVv.CompareTo(localVv) == DataSyncVvRelation.DominatedBy;
                return Item(r, DataSyncPlanItemType.Unchanged, newer ? DataSyncPlanItemReason.LocalIsNewer : null,
                    local: m, recordsNewKeys: RecordsNewKeys(r, m));
            }

            if (Codec.SubtypeOf(m.Content) != Codec.SubtypeOf(r.Entity.Content!))
            {
                return Item(r, DataSyncPlanItemType.NeedsDecision, DataSyncPlanItemReason.TypeMismatch, local: m,
                    recordsNewKeys: RecordsNewKeys(r, m));
            }

            var diff = Codec.Diff(m.Content, r.Entity.Content!);
            return Item(r, diff.Changes.Count == 0 ? DataSyncPlanItemType.Unchanged : DataSyncPlanItemType.Update, null,
                local: m, diff: diff, recordsNewKeys: RecordsNewKeys(r, m));
        }

        private DataSyncPlanItem IdentityConflict(DataSyncReviewIncoming r, IReadOnlyList<LocalIdentifiedEntity> matches)
        {
            if (matches.Any(m => m.Unreadable)) return Held(r, DataSyncHeldReason.LocalUnreadable, null);

            // A candidate of another subtype cannot be the one that follows: a review never changes a type (§5.2).
            var subtype = Codec.SubtypeOf(r.Entity.Content!);
            var candidates = Sorted(matches.Where(m => Codec.SubtypeOf(m.Content) == subtype)
                .Select(m => (Local: m, Level: Codec.MatchNatural(r.Entity.Content!, m.Content))));
            return Item(r, DataSyncPlanItemType.NeedsDecision, DataSyncPlanItemReason.IdentityConflict,
                candidates: candidates);
        }

        // ---- pass 2 ------------------------------------------------------------------------------

        private sealed record Proposal(DataSyncReviewIncoming R,
            IReadOnlyList<(LocalIdentifiedEntity Local, DataSyncNaturalMatch Level)> Same, bool Clash,
            bool TouchesUnreadable, LocalIdentifiedEntity? Best);

        private Proposal Propose(DataSyncReviewIncoming r)
        {
            var content = r.Entity.Content!;
            var same = new List<(LocalIdentifiedEntity Local, DataSyncNaturalMatch Level)>();
            var clash = false;
            var unreadable = false;
            foreach (var l in local.NamedLike(Codec.NameOf(content)))
            {
                if (Claimed.Contains(l.LocalKey)) continue;
                var level = Codec.MatchNatural(content, l.Content);
                if (level < DataSyncNaturalMatch.Clash) continue;
                if (l.Unreadable) unreadable = true;
                else if (level == DataSyncNaturalMatch.Clash) clash = true;
                else same.Add((l, level));
            }

            same = Sorted(same);
            LocalIdentifiedEntity? best = null;
            if (!unreadable && same.Count > 0 && (same.Count == 1 || same[1].Level < same[0].Level)) best = same[0].Local;
            return new Proposal(r, same, clash, unreadable, best);
        }

        private DataSyncPlanItem Pending(Proposal p, bool duplicate)
        {
            var r = p.R;
            if (p.TouchesUnreadable) return Held(r, DataSyncHeldReason.LocalUnreadable, null);

            List<DataSyncPlanWarning> deletedHere = r.Keys!.Any(local.TombstonedKeys.Contains)
                ? [new DataSyncPlanWarning(DataSyncWarningCode.PreviouslyDeletedHere, null, null)]
                : [];
            if (p.Same.Count == 0)
            {
                if (p.Clash)
                {
                    return Item(r, DataSyncPlanItemType.NeedsDecision, DataSyncPlanItemReason.NameClashDifferentType,
                        extra: deletedHere);
                }

                var create = Codec.PrepareCreate(r.Entity.Content!, null);
                return Item(r, DataSyncPlanItemType.Create, null, extra: deletedHere.Concat(create.Warnings));
            }

            if (p.Best is not { } best)
            {
                return Item(r, DataSyncPlanItemType.NeedsDecision, DataSyncPlanItemReason.AmbiguousNameMatch,
                    candidates: p.Same, extra: deletedHere);
            }

            if (duplicate)
            {
                return Item(r, DataSyncPlanItemType.NeedsDecision, DataSyncPlanItemReason.DuplicateInPackage,
                    candidates: p.Same, extra: deletedHere);
            }

            // v3.1 §5.2 step 3: a link always asks, except an extension group with an Identical unique candidate
            // (the D09 exception); an exact name (or better) can be confirmed in bulk.
            var level = p.Same[0].Level;
            var confirm = !(Codec.Descriptor.AutoLinkIdentical && level == DataSyncNaturalMatch.Identical);
            return Item(r, DataSyncPlanItemType.Link, null, candidates: p.Same, extra: deletedHere,
                defaultTarget: best.LocalKey, requiresConfirmation: confirm,
                bulkLinkEligible: confirm && level >= DataSyncNaturalMatch.Exact);
        }

        // ---- items -------------------------------------------------------------------------------

        public DataSyncPlanItem Held(DataSyncReviewIncoming r, DataSyncHeldReason reason, LocalIdentifiedEntity? m) =>
            new(r.ItemId, kind, DataSyncPlanItemType.Held, null, reason, IncomingEntity(r),
                m is null || codec is null ? null : LocalEntity(m), [], [], DataSyncPlanFormat.CountChanges([]), false, 0, 0,
                [], null, null, false, false, false, false,
                DataSyncPlanFormat.ReviewToken(DataSyncPlanItemType.Held, null, m?.LocalKey, r.Entity.ValidatedHash, []),
                DataSyncPlanFormat.SortWarnings(r.Entity.Warnings), DataSyncPlanFormat.CountWarnings(r.Entity.Warnings),
                false);

        private DataSyncPlanItem Item(DataSyncReviewIncoming r, DataSyncPlanItemType type, DataSyncPlanItemReason? reason,
            LocalIdentifiedEntity? local = null, EntityDiff? diff = null,
            IReadOnlyList<(LocalIdentifiedEntity Local, DataSyncNaturalMatch Level)>? candidates = null,
            IEnumerable<DataSyncPlanWarning>? extra = null, string? defaultTarget = null, bool requiresConfirmation = true,
            bool bulkLinkEligible = false, bool recordsNewKeys = false)
        {
            var allowed = Allowed(type, reason, candidates?.Count ?? 0);
            DataSyncPlanResolution? resolution = type switch
            {
                DataSyncPlanItemType.Create => DataSyncPlanResolution.Create,
                DataSyncPlanItemType.Update or DataSyncPlanItemType.Unchanged => DataSyncPlanResolution.Update,
                DataSyncPlanItemType.Link => DataSyncPlanResolution.Link,
                _ => null,
            };
            if (type is DataSyncPlanItemType.Create or DataSyncPlanItemType.Update or DataSyncPlanItemType.Unchanged)
            {
                requiresConfirmation = false;
                defaultTarget = local?.LocalKey;
            }

            var changes = diff is null ? [] : DataSyncPlanFormat.SortChanges(diff.Changes);
            var warnings = r.Entity.Warnings.Concat(diff?.Warnings ?? []).Concat(extra ?? []).ToList();
            if (owner._in.LinkMode == DataSyncLinkMode.TwoWay && allowed.Contains(DataSyncPlanResolution.CreateSeparate))
                warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.NameUsedEverywhere, null, null));
            var sortedWarnings = DataSyncPlanFormat.SortWarnings(warnings);

            var hash = r.Entity.ValidatedHash;
            var planCandidates = (candidates ?? []).Select(c => Candidate(r, c.Local, c.Level, type, reason)).ToList();
            return new DataSyncPlanItem(r.ItemId, kind, type, reason, null, IncomingEntity(r),
                local is null ? null : LocalEntity(local), planCandidates, changes, DataSyncPlanFormat.CountChanges(changes),
                false, diff?.UnchangedChildren ?? 0, diff?.LocalOnlyChildren ?? 0, allowed, resolution, defaultTarget,
                requiresConfirmation, bulkLinkEligible, allowed.Contains(DataSyncPlanResolution.CreateSeparate),
                recordsNewKeys, DataSyncPlanFormat.ReviewToken(type, reason, local?.LocalKey, hash, changes),
                sortedWarnings, DataSyncPlanFormat.CountWarnings(sortedWarnings), false);
        }

        private DataSyncPlanCandidate Candidate(DataSyncReviewIncoming r, LocalIdentifiedEntity l, DataSyncNaturalMatch level,
            DataSyncPlanItemType type, DataSyncPlanItemReason? reason)
        {
            var diff = Codec.Diff(l.Content, r.Entity.Content!);
            var changes = DataSyncPlanFormat.SortChanges(diff.Changes);
            var warnings = DataSyncPlanFormat.SortWarnings(diff.Warnings);
            return new DataSyncPlanCandidate(l.LocalKey, Codec.NameOf(l.Content), Codec.SubtypeOf(l.Content), level,
                changes, DataSyncPlanFormat.CountChanges(changes), false, warnings,
                DataSyncPlanFormat.CountWarnings(warnings), false, diff.UnchangedChildren, diff.LocalOnlyChildren,
                RecordsNewKeys(r, l),
                DataSyncPlanFormat.ReviewToken(type, reason, l.LocalKey, r.Entity.ValidatedHash, changes));
        }

        /// <summary>v3.1 §7.3.</summary>
        private static IReadOnlyList<DataSyncPlanResolution> Allowed(DataSyncPlanItemType type,
            DataSyncPlanItemReason? reason, int candidates) => type switch
        {
            DataSyncPlanItemType.Create => [DataSyncPlanResolution.Create, DataSyncPlanResolution.Skip],
            DataSyncPlanItemType.Update or DataSyncPlanItemType.Unchanged =>
                [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip],
            DataSyncPlanItemType.Link =>
                [DataSyncPlanResolution.Link, DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip],
            DataSyncPlanItemType.NeedsDecision => reason switch
            {
                DataSyncPlanItemReason.TypeMismatch => [DataSyncPlanResolution.Skip, DataSyncPlanResolution.CreateSeparate],
                DataSyncPlanItemReason.NameClashDifferentType =>
                    [DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip],
                DataSyncPlanItemReason.IdentityConflict => candidates > 0
                    ? [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip]
                    : [DataSyncPlanResolution.Skip],
                _ => [DataSyncPlanResolution.Link, DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip],
            },
            _ => [],
        };

        private bool RecordsNewKeys(DataSyncReviewIncoming r, LocalIdentifiedEntity target) =>
            local.AliasKeysFor(r.Keys!, target).Count > 0;

        private DataSyncPlanEntity IncomingEntity(DataSyncReviewIncoming r) => r.Entity.Content is { } content && codec is not null
            ? new DataSyncPlanEntity(null, r.Entity.DisplayName, codec.SubtypeOf(content), r.Position, codec.ChildCountOf(content))
            : new DataSyncPlanEntity(null, r.Entity.DisplayName, null, r.Position, 0);

        private DataSyncPlanEntity LocalEntity(LocalIdentifiedEntity l) => new(l.LocalKey, Codec.NameOf(l.Content),
            Codec.SubtypeOf(l.Content), l.Position, Codec.ChildCountOf(l.Content));

        /// <summary>v3.1 §7.2: by level descending, then local key numerically ascending.</summary>
        private static List<(LocalIdentifiedEntity Local, DataSyncNaturalMatch Level)> Sorted(
            IEnumerable<(LocalIdentifiedEntity Local, DataSyncNaturalMatch Level)> candidates) =>
            candidates.OrderByDescending(c => c.Level).ThenBy(c => c.Local.LocalKey, LocalKeyComparer.Instance).ToList();
    }
}
