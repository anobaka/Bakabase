using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// The convergence property tests (§13.3): seeded random scenarios over 2–4 nodes (pair, chain, star with a
/// headless hub, mesh; TwoWay, a mix with Follow, mutual Follow) with every step of the list the simulator's kinds
/// can express, checked against invariants I1–I10. CI runs 500 fixed seeds; <c>DATASYNC_FUZZ_SEED</c> sets the
/// first seed and <c>DATASYNC_FUZZ_RUNS</c> how many to run (for longer local runs). A failing seed is shrunk to
/// fewer steps and printed with its trace and final states. Every fifth seed writes its feed with small wire limits
/// (<see cref="SimWorld.SmallWireLimits"/>), so paging and chunk reassembly run under the invariants too.
/// </summary>
[TestClass]
public class DataSyncConvergenceTests
{
    private const int CiRuns = 500;

    private static (int First, int Runs) Seeds()
    {
        var first = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_FUZZ_SEED"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var seed) ? seed : 1;
        var runs = int.TryParse(Environment.GetEnvironmentVariable("DATASYNC_FUZZ_RUNS"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var count) && count > 0 ? count : CiRuns;
        return (first, runs);
    }

    [TestMethod]
    public void SeededScenariosConverge()
    {
        var (first, runs) = Seeds();
        var failures = new List<string>();
        var coverage = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var seed = first; seed < first + runs && failures.Count < 3; seed++)
        {
            var spec = SimScenarioSpec.Generate(seed);
            var scenario = SimScenario.Execute(spec);
            foreach (var (what, count) in scenario.World.Counters) coverage[what] = coverage.GetValueOrDefault(what) + count;
            if (scenario.Failures.Count == 0) continue;
            failures.Add(SimScenario.Execute(SimScenario.Shrink(spec)).Report());
        }

        if (Environment.GetEnvironmentVariable("DATASYNC_FUZZ_COVERAGE") is { Length: > 0 } path)
            File.WriteAllLines(path, coverage.Select(c => $"{c.Key} {c.Value}"));
        Assert.AreEqual(0, failures.Count, "\n" + string.Join("\n\n", failures));

        // The CI seeds keep reaching the paths the step list exists for; a generator change that stops reaching
        // one fails here rather than passing on easier scenarios. Other seeds (local runs) reach what they reach.
        if (first != 1 || runs < CiRuns) return;
        foreach (var covered in CoveredPaths)
            Assert.IsTrue(coverage.GetValueOrDefault(covered) > 0, $"no scenario reached {covered}");
    }

    private static readonly string[] CoveredPaths =
    [
        "restoreChoice:ThisDeviceWins", "restoreChoice:OthersWin", "evidence:reader:LocalRestoreDetected",
        "evidence:watermark:LocalRestoreDetected", "evidence:peer:LocalRestoreSuspected", "pause:PeerReset:restored",
        "pause:KindEmptied", "anomaly:regression:retired", "residualWindow:verifiedByTimeout",
        "resolve:IdentityConflict:KeepWithEntity", "resolve:IdentityConflict:KeepRecordLinked", "rekey",
        "resolve:LinkSuggestion:Link", "resolve:TypeChange:Convert", "resolve:DeletedThere:RestoreEverywhere",
        "resolve:ChildDeletedInUse:DeleteHere", "resolve:MassChildDeletion:ApplyAll", "resolve:LargeChange:ApplyAll",
        "resolve:SuspectedLostUpdate:Publish", "resolve:rederived", "undo:created", "undo:updated", "undo:deleted",
        "revision:Revive", "revision:FollowMerged", "revision:MergedWithConflicts", "tombstoneServedAgain",
        "changedDuringApply", "chooser:headless", "hold",
        "step:LongPartition", "step:RestoreOffline", "step:SyncTwice", "step:SyncStaleWrite", "step:SyncConcurrentWrite",
        "step:ChildrenLocal", "step:SyncCrossed", "note:childrenLocalTurnedOff",
        "step:UndoRelink", "undoRelink:excluded", "step:EditApplied", "editApplied:kept", "wire:multiPage",
        "wire:chunked",
    ];

    /// <summary>
    /// Seeds beyond CI's range that found a defect in longer local runs (the seeds below 500 run above anyway).
    /// Each replays the scenario that failed; the engine's side of each finding has its own test in
    /// <c>SimulatorFindingsTests</c>.
    /// </summary>
    [TestMethod]
    [DataRow(1144, DisplayName = "'sync the definition only' turned off while a child was renamed elsewhere")]
    [DataRow(4761, DisplayName = "a retired actor's counter issued twice (a collision)")]
    [DataRow(6165, DisplayName = "revisions go on while a type change waits for its decision")]
    [DataRow(7533, DisplayName = "a deletion and a live version under one vector")]
    [DataRow(7667, DisplayName = "a collision's question closed by dominance")]
    [DataRow(7705, DisplayName = "a re-merged agreement the peer no longer offers")]
    [DataRow(9709, DisplayName = "a tombstone read before its entity, behind reissued Seqs")]
    [DataRow(9710, DisplayName = "a conflict re-merged while the peer offers nothing live")]
    [DataRow(10190, DisplayName = "the same, delivered twice")]
    [DataRow(10669, DisplayName = "counters reissued in the residual restore window")]
    [DataRow(10698, DisplayName = "an identity question whose candidate was deleted")]
    [DataRow(11887, DisplayName = "a union without a base under a bare Max")]
    [DataRow(13474, DisplayName = "a tombstone of two lineages, one deleted and one still published")]
    [DataRow(17507, DisplayName = "revisions go on while other type changes wait, after a decision")]
    [DataRow(19072, DisplayName = "an undone create excluded under every key it has")]
    [DataRow(21823, DisplayName = "a stored conflict delivered again under Follow")]
    [DataRow(24763, DisplayName = "a restored device's older bases under a bare Max")]
    [DataRow(25302, DisplayName = "a bind that retires a tombstone, delivered twice")]
    [DataRow(29739, DisplayName = "counters reissued by a device with readers and no link of its own")]
    [DataRow(31181, DisplayName = "an alias a detached entity owns, refused at every apply")]
    [DataRow(55919, DisplayName = "a deletion decided while a restored device reissued counters")]
    [DataRow(61432, DisplayName = "an alias that changes the tie key, delivered twice")]
    [DataRow(70521, DisplayName = "a waiting identity question and a tombstone asked about, delivered twice")]
    public void SeedsThatFoundDefectsConverge(int seed)
    {
        var scenario = SimScenario.Execute(SimScenarioSpec.Generate(seed));
        Assert.AreEqual(0, scenario.Failures.Count, "\n" + scenario.Report());
    }

    [TestMethod]
    public void TheSameSeedGivesTheSameFinalState()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var spec = SimScenarioSpec.Generate(seed * 7919);
            var first = SimScenario.Execute(spec);
            var second = SimScenario.Execute(spec);
            Assert.AreEqual(SimDigest.Of(first.World), SimDigest.Of(second.World), $"seed {spec.Seed} is not deterministic");
        }
    }
}
