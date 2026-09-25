using System.Globalization;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.CustomProperties;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// Seeded random merges over both kinds: local entities, tombstones, bases in every state with pending records of
/// every reason, pulls with aliases, tombstones, held and retired-actor records, re-merges, open items, once flags,
/// Follow, full reconciliations and emptied kinds. Whatever the input a store can hold, the merger never throws,
/// gives the same result twice, and every revision it decides can be applied (§2.8: the new vector covers the
/// local one). CI runs 1,000 inputs; <c>DATASYNC_FUZZ_SEED</c> and <c>DATASYNC_FUZZ_RUNS</c> run others locally.
/// </summary>
[TestClass]
public class MergerFuzzTests
{
    private const int CiRuns = 1_000;
    private static readonly DataSyncActorId[] Actors = [Self, Peer, Third, Retired];
    private static readonly DataSyncEditorRef[] Editors =
        [SelfEditor, PeerEditor, ThirdEditor, new(SelfNode, "This PC", Retired.Value)];
    private static readonly string[] Labels = ["Action", "Drama", "Comedy", "action"];
    private static readonly string[] Extensions = [".mkv", ".mp4", ".avi", ".jpg"];

    private static (int First, int Runs) Seeds(int ciRuns)
    {
        var first = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_FUZZ_SEED"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var seed) ? seed : 1;
        var runs = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_FUZZ_RUNS"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var count) && count > 0 ? count : ciRuns;
        return (first, runs);
    }

    /// <summary>Custom property inputs in CI (package B's codec, whose merges cost more): fewer than the test kind's.</summary>
    private const int CiCustomPropertyRuns = 500;

    [TestMethod]
    public void RandomInputsNeverThrowMergeDeterministicallyAndGiveApplicableRevisions() => Fuzz(false, CiRuns);

    /// <summary>
    /// The same over custom properties in place of the test kind: contents from <c>Merge3Generator</c> (IgnoreCase,
    /// duplicates, <c>null</c>/<c>""</c> tag groups, multilevel trees, options this device never publishes).
    /// </summary>
    [TestMethod]
    public void RandomCustomPropertyInputsNeverThrowMergeDeterministicallyAndGiveApplicableRevisions() =>
        Fuzz(true, CiCustomPropertyRuns);

    private static void Fuzz(bool customProperties, int ciRuns)
    {
        var (first, runs) = Seeds(ciRuns);
        var failures = new List<string>();
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var seed = first; seed < first + runs && failures.Count < 5; seed++)
        {
            var f = Generate(new Random(seed), customProperties);
            DataSyncMergeResult result;
            try
            {
                result = f.Merge();
            }
            catch (Exception e)
            {
                failures.Add($"seed {seed}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                continue;
            }

            var dump = MergerTests.Dump(result);
            if (dump != MergerTests.Dump(f.Merge())) failures.Add($"seed {seed}: two merges of one input differ");
            Count(outcomes, result);

            var counter = f.ActorCounter;
            foreach (var decision in result.Revisions)
            {
                var local = LocalVectorOf(f, decision);
                try
                {
                    var vv = DataSyncRevisionRules.Next(decision.Revision, local, decision.RemoteVv,
                        decision.ResultEqualsRemote && !decision.SeenBoth, decision.ResultEqualsLocal, Self, () => ++counter,
                        decision.TombstoneVv);
                    if (vv.CompareTo(local) is not (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates))
                        failures.Add($"seed {seed}: {decision.Revision} of {decision.Keys.Primary} gives {vv}, below {local}");
                }
                catch (Exception e) when (e is ArgumentException)
                {
                    failures.Add($"seed {seed}: {decision.Revision} of {decision.Keys.Primary} cannot be applied: {e.Message}");
                }
            }
        }

        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n\n", failures));
        // The generator reaches the interesting paths, not only pauses.
        foreach (var (path, share) in new[] { ("applied", 10), ("inbox", 10), ("revision", 10), ("anomaly", 200), ("pause", 500) })
        {
            Assert.IsTrue(outcomes.GetValueOrDefault(path) >= runs / share,
                $"{path}: {outcomes.GetValueOrDefault(path)} of {runs}");
        }
    }

    private static void Count(SortedDictionary<string, int> outcomes, DataSyncMergeResult r)
    {
        void Add(string what) => outcomes[what] = outcomes.GetValueOrDefault(what) + 1;
        if (r.Pause is not null) Add("pause");
        if (r.Anomaly is not null) Add("anomaly");
        if (r.Batches.Count > 0) Add("applied");
        if (r.Inbox.Count > 0) Add("inbox");
        if (r.Revisions.Count > 0) Add("revision");
    }

    /// <summary>The vector the decision's row holds before the apply: the entity's, the tombstone's, or none (a create).</summary>
    private static DataSyncVersionVector LocalVectorOf(MergeFixture f, DataSyncRevisionDecision decision)
    {
        if (decision.Revision == DataSyncRevisionKind.Create) return DataSyncVersionVector.Empty;
        // A revive rewrites the tombstone's row.
        if (decision.Revision == DataSyncRevisionKind.Revive) return decision.TombstoneVv ?? DataSyncVersionVector.Empty;
        var entity = f.Entities.GetValueOrDefault(decision.Kind)?.FirstOrDefault(e =>
            decision.LocalKey is not null ? e.LocalKey == decision.LocalKey : e.Keys.All.Contains(decision.Keys.Primary!.Value));
        if (entity is not null) return entity.Vv;
        var tombstone = f.Tombstones.GetValueOrDefault(decision.Kind)?.FirstOrDefault(t =>
            decision.Keys.All.Any(k => t.Keys.All.Contains(k)));
        return tombstone?.Vv ?? DataSyncVersionVector.Empty;
    }

    // ---- the generator ---------------------------------------------------------------------------------

    private static DataSyncVersionVector RandomVv(Random random, long selfMax)
    {
        var vv = DataSyncVersionVector.Empty;
        foreach (var actor in Actors)
        {
            if (random.Next(3) == 0) continue;
            var max = actor == Self ? selfMax : 6;
            vv = vv.With(actor, 1 + random.Next((int)Math.Max(1, max)));
        }

        return vv.CompareTo(DataSyncVersionVector.Empty) == DataSyncVvRelation.Equal ? vv.With(Peer, 1) : vv;
    }

    private static object Content(Random random, string kind, string name) =>
        kind == GroupKind
            ? new ExtensionGroupContentV1(name, Extensions.Where(_ => random.Next(2) == 0))
            : kind == CustomPropertyKind
            ? CustomPropertyContent(random, name)
            : new TestItemContent(name, random.Next(3) == 0 ? "#0090ff" : null,
                Enumerable.Range(0, random.Next(4)).Select(i => new TestChild(
                    random.Next(2).ToString(CultureInfo.InvariantCulture) + i, Labels[random.Next(Labels.Length)])),
                random.Next(4) == 0 ? "Choice" : null);

    /// <summary>
    /// A custom property: one of the four reference types, few labels (so classes meet), option ids from one small
    /// pool (so records and local entities share some), now and then options this device never publishes.
    /// </summary>
    private static CustomPropertyContentV1 CustomPropertyContent(Random random, string name)
    {
        var generator = new Merge3Generator(random.Next());
        var content = generator.Content(generator.Type(), "c");
        if (random.Next(3) == 0) content = generator.Mutate(content, "m", generator.Edits());
        if (random.Next(4) == 0) content = generator.Unpublishable(content, "x");
        return content with { Name = name };
    }

    private static MergeFixture Generate(Random random, bool customProperties)
    {
        var primary = customProperties ? CustomPropertyKind : ItemKind;
        var f = new MergeFixture
        {
            ActorCounter = 3 + random.Next(4),
            Mode = random.Next(4) == 0 ? DataSyncLinkMode.Follow : DataSyncLinkMode.TwoWay,
            PeerComparisonFormVersion = random.Next(5) == 0 ? 2 : 1,
            LinkFlags = random.Next(6) switch
            {
                0 => new DataSyncMergeFlags(DeletionsAsItems: true),
                1 => new DataSyncMergeFlags(SkipDeletionBreaker: true, SkipLargeChange: true),
                _ => DataSyncMergeFlags.None,
            },
        };
        f.RetiredCounters[Retired.Value] = 4;
        var names = new[] { "Genre", "Mood", "Studio", "genre" };
        var keys = Enumerable.Range(1, 16).Select(K).OrderBy(_ => random.Next()).ToList();
        var used = new HashSet<SyncKey>();

        SyncKey TakeKey()
        {
            var key = keys.First(k => !used.Contains(k));   // at most 14 are taken
            used.Add(key);
            return key;
        }

        foreach (var kind in new[] { primary, GroupKind })
        {
            // Local entities and tombstones: each key belongs to one of them at most (v3.1 §5.3).
            for (var i = random.Next(4); i > 0; i--)
            {
                var key = TakeKey();
                var aliases = random.Next(4) == 0 ? [TakeKey()] : Array.Empty<SyncKey>();
                var state = random.Next(8) switch
                {
                    0 => DataSyncEntitySyncState.LocalOnly,
                    1 => DataSyncEntitySyncState.Detached,
                    _ => DataSyncEntitySyncState.Synced,
                };
                var entity = f.Local((f.EntitiesOf(kind).Count + 1 + (kind == GroupKind ? 50 : 0)).ToString(CultureInfo.InvariantCulture),
                    key, Content(random, kind, names[random.Next(names.Length)]), RandomVv(random, f.ActorCounter),
                    lastEditor: Editors[random.Next(3)], orderKey: kind != GroupKind ? "a" + i : null, state: state,
                    createdBySync: random.Next(2) == 0, publishHeld: random.Next(12) == 0, seq: 5 + random.Next(20),
                    kind: kind, aliases: aliases, valueCount: random.Next(3) == 0 ? null : random.Next(2));
                if (random.Next(2) == 0)
                {
                    // Extensions are never in use (§8.5.3).
                    f.Usage[(kind, entity.LocalKey)] = kind == GroupKind
                        ? new Dictionary<string, int>()
                        : CodecOf(kind).ChildrenOf(entity.Content).Select(c => c.Id).Distinct()
                            .ToDictionary(id => id, _ => random.Next(3));
                }
            }

            for (var i = random.Next(3); i > 0; i--)
            {
                f.Tombstone(TakeKey(), RandomVv(random, f.ActorCounter),
                    random.Next(4) == 0 ? DataSyncTombstoneKind.UndoneCreate : DataSyncTombstoneKind.Deleted,
                    served: random.Next(3) != 0, seq: 5 + random.Next(20), kind: kind);
            }
        }

        // Bases on some keys, in every state, with pending records of every reason.
        var reasons = Enum.GetValues<DataSyncPendingReason>();
        foreach (var key in Enumerable.Range(1, 16).Select(K).Where(_ => random.Next(3) == 0))
        {
            var kind = f.Entities.GetValueOrDefault(GroupKind)?.Any(e => e.Keys.All.Contains(key)) == true ||
                       f.Tombstones.GetValueOrDefault(GroupKind)?.Any(t => t.Keys.All.Contains(key)) == true
                ? GroupKind
                : primary;
            var record = random.Next(4) == 0
                ? null
                : f.Record(key, Content(random, kind, names[random.Next(names.Length)]), RandomVv(random, f.ActorCounter),
                    Editors[random.Next(Editors.Length)], deleted: random.Next(5) == 0, seq: 1 + random.Next(9), kind: kind);
            var state = random.Next(6) switch
            {
                0 => DataSyncBaseState.Unbound,
                1 => DataSyncBaseState.MissingAtPeer,
                2 => DataSyncBaseState.Excluded,
                _ => DataSyncBaseState.Normal,
            };
            DataSyncPendingRecord? pending = null;
            if (random.Next(3) == 0 && state != DataSyncBaseState.Excluded)
            {
                pending = PendingOf(f.Record(key, Content(random, kind, names[random.Next(names.Length)]),
                        RandomVv(random, f.ActorCounter), Editors[random.Next(Editors.Length)], deleted: random.Next(5) == 0,
                        seq: 1 + random.Next(9), kind: kind),
                    reasons[random.Next(reasons.Length)], random.Next(30));
                if (random.Next(2) == 0) f.PendingToMerge.Add((kind, key));

                // What a conflicted merge applied (§8.4 row K6): the record itself, or another, with a kept path.
                if (random.Next(3) == 0)
                {
                    var applied = random.Next(2) == 0 ? pending.Record : record ?? pending.Record;
                    pending = pending with
                    {
                        AppliedBase = new DataSyncAppliedBase(applied,
                            random.Next(2) == 0 ? [] : [new DataSyncKeptPath("name", new DataSyncDisplayValue("Mood"))]),
                    };
                }
            }

            f.Base(key, record, state, pending: pending,
                exclusion: state == DataSyncBaseState.Excluded ? DataSyncExclusionReason.Skipped : null, kind: kind);
        }

        // The pull: distinct keys across its records (a reader discards a pull that repeats one).
        var pulled = new HashSet<SyncKey>();
        foreach (var kind in new[] { primary, GroupKind })
        {
            for (var i = random.Next(5); i > 0; i--)
            {
                var candidates = Enumerable.Range(1, 18).Select(K).Where(k => !pulled.Contains(k)).ToList();
                var key = candidates[random.Next(candidates.Count)];
                pulled.Add(key);
                var aliases = new List<SyncKey>();
                if (random.Next(4) == 0)
                {
                    var alias = candidates.Where(k => !pulled.Contains(k)).OrderBy(_ => random.Next()).First();
                    pulled.Add(alias);
                    aliases.Add(alias);
                }

                var deleted = random.Next(5) == 0;
                var selfMax = random.Next(12) == 0 ? f.ActorCounter + 2 : f.ActorCounter;   // sometimes a regression
                var vv = RandomVv(random, selfMax);
                // Sometimes exactly a local vector: row A2's verdicts.
                if (random.Next(5) == 0 && f.EntitiesOf(kind).FirstOrDefault(e => e.Keys.All.Contains(key)) is { } same) vv = same.Vv;
                f.Pull(f.Record(key, deleted ? null : Content(random, kind, names[random.Next(names.Length)]), vv,
                    Editors[random.Next(Editors.Length)], deleted, kind != GroupKind ? "a" + random.Next(9) : null,
                    aliases: aliases, kind: kind, schemaVersion: random.Next(15) == 0 ? 2 : 1), kind);
            }
        }

        if (random.Next(4) == 0) f.FullReconciliation.Add(primary);
        if (random.Next(6) == 0) f.LiveCounts[GroupKind] = 0;
        if (random.Next(8) == 0) f.NoPull = true;
        if (random.Next(3) == 0 && f.Bases.Count > 0)
        {
            var (kind, key) = f.Bases.Keys.OrderBy(k => k.Key.Value, StringComparer.Ordinal).First();
            f.OpenItem(1, key, DataSyncInboxItemType.FieldConflict, "name", RandomVv(random, f.ActorCounter), kind);
        }

        return f;
    }
}
