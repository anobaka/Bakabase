using Bakabase.Modules.Enhancer.Components;
using FluentAssertions;

namespace Bakabase.Modules.Enhancer.Tests;

/// <summary>
/// Pure unit tests for <see cref="CycleDetector.DetectFirstCycle{T}"/>. This
/// helper was lifted out of <c>EnhancerService.BuildContextCreationTasks</c>
/// so we can exercise every cycle topology without DB fixtures. A bug here
/// resurfaces as the original symptom: a single bad enhancer-dependency
/// config silently tanks an entire EnhanceAll batch.
/// </summary>
[TestClass]
public sealed class CycleDetectorTests
{
    /// <summary>Trivial node class for graph fixtures.</summary>
    private sealed class N(string id)
    {
        public string Id { get; } = id;
        public List<N> Deps { get; } = [];
        public override string ToString() => Id;
    }

    private static string? Detect(params N[] nodes)
        => CycleDetector.DetectFirstCycle(nodes, n => n.Deps, n => n.Id);

    #region Acyclic shapes — must return null

    [TestMethod]
    public void EmptyGraph_ReturnsNull()
        => CycleDetector.DetectFirstCycle(Array.Empty<N>(), n => n.Deps, n => n.Id).Should().BeNull();

    [TestMethod]
    public void SingleNodeNoEdges_ReturnsNull()
        => Detect(new N("a")).Should().BeNull();

    [TestMethod]
    public void LinearChain_ReturnsNull()
    {
        // a -> b -> c
        var a = new N("a"); var b = new N("b"); var c = new N("c");
        a.Deps.Add(b); b.Deps.Add(c);
        Detect(a, b, c).Should().BeNull();
    }

    [TestMethod]
    public void DiamondAcyclic_ReturnsNull()
    {
        // a -> b, a -> c, b -> d, c -> d (shared dependency, no cycle)
        var a = new N("a"); var b = new N("b"); var c = new N("c"); var d = new N("d");
        a.Deps.AddRange([b, c]);
        b.Deps.Add(d);
        c.Deps.Add(d);
        Detect(a, b, c, d).Should().BeNull();
    }

    [TestMethod]
    public void DisconnectedComponents_AllAcyclic_ReturnsNull()
    {
        var a = new N("a"); var b = new N("b");
        var c = new N("c"); var d = new N("d");
        a.Deps.Add(b);
        c.Deps.Add(d);
        Detect(a, b, c, d).Should().BeNull();
    }

    #endregion

    #region Cyclic shapes — must return a labelled cycle

    [TestMethod]
    public void SelfEdge_ReportsSelfCycle()
    {
        // a -> a. The helper does not strip self-edges — callers (like
        // BuildContextCreationTasks) filter those before calling.
        var a = new N("a"); a.Deps.Add(a);
        Detect(a).Should().Be("a->a");
    }

    [TestMethod]
    public void TwoNodeMutualCycle_ReportsFullPath()
    {
        // a -> b -> a
        var a = new N("a"); var b = new N("b");
        a.Deps.Add(b); b.Deps.Add(a);
        Detect(a, b).Should().Be("a->b->a");
    }

    [TestMethod]
    public void ThreeNodeCycle_ReportsFullPath()
    {
        // a -> b -> c -> a
        var a = new N("a"); var b = new N("b"); var c = new N("c");
        a.Deps.Add(b); b.Deps.Add(c); c.Deps.Add(a);
        Detect(a, b, c).Should().Be("a->b->c->a");
    }

    [TestMethod]
    public void CycleAmongSubsetOfNodes_OnlyReportsCycleNodes()
    {
        // a -> b -> c, c -> b (cycle b->c->b); a is a clean entry into it.
        var a = new N("a"); var b = new N("b"); var c = new N("c");
        a.Deps.Add(b); b.Deps.Add(c); c.Deps.Add(b);
        // DFS starts from a, descends a->b->c, sees c.Deps→b which is on the
        // stack. Cycle label walks the stack from c down to b, then closes.
        Detect(a, b, c).Should().Be("b->c->b");
    }

    [TestMethod]
    public void TwoDisjointCycles_ReturnsFirstEncountered()
    {
        // Cycle 1: a -> b -> a.  Cycle 2: c -> d -> c.
        var a = new N("a"); var b = new N("b");
        var c = new N("c"); var d = new N("d");
        a.Deps.Add(b); b.Deps.Add(a);
        c.Deps.Add(d); d.Deps.Add(c);
        // Iteration order is insertion order, so cycle 1 wins.
        Detect(a, b, c, d).Should().Be("a->b->a");
    }

    [TestMethod]
    public void CycleNotReachableFromFirstNode_StillFoundOnLaterIteration()
    {
        // a -> b (acyclic), c -> d -> c (cycle, unreachable from a).
        // DFS starting at a visits a, b. Next iteration picks c (not yet
        // visited) and finds the cycle.
        var a = new N("a"); var b = new N("b");
        var c = new N("c"); var d = new N("d");
        a.Deps.Add(b);
        c.Deps.Add(d); d.Deps.Add(c);
        Detect(a, b, c, d).Should().Be("c->d->c");
    }

    [TestMethod]
    public void AcyclicGraphLargerThanStack_StackDoesNotLeakBetweenBranches()
    {
        // a -> b, a -> c, b -> d, c -> d (diamond). When DFS pops back from
        // b after visiting d, the stack must be cleared down to a before
        // descending into c; otherwise visiting d via c would think it's
        // back-edging into the stack.
        var a = new N("a"); var b = new N("b"); var c = new N("c"); var d = new N("d");
        a.Deps.AddRange([b, c]);
        b.Deps.Add(d);
        c.Deps.Add(d);
        Detect(a, b, c, d).Should().BeNull(
            "stack must pop on the way back up — d visited via b should not register as on-stack when we descend via c");
    }

    #endregion

    #region Determinism / ordering invariants

    [TestMethod]
    public void StartNodeOrder_AffectsFirstCycleFound()
    {
        // If two disjoint cycles exist, the one whose root appears first in
        // the input order is the one reported. This is the contract callers
        // rely on for deterministic logging.
        var a = new N("a"); var b = new N("b");
        var c = new N("c"); var d = new N("d");
        a.Deps.Add(b); b.Deps.Add(a);
        c.Deps.Add(d); d.Deps.Add(c);

        CycleDetector.DetectFirstCycle(new[] { c, d, a, b }, n => n.Deps, n => n.Id)
            .Should().Be("c->d->c", "reversing the input order swaps which cycle is hit first");
    }

    [TestMethod]
    public void DuplicateNodesInInput_DoNotCauseInfiniteLoop()
    {
        // Passing the same node twice in the input collection is unusual but
        // shouldn't break the visited-set guard.
        var a = new N("a"); var b = new N("b");
        a.Deps.Add(b);
        CycleDetector.DetectFirstCycle(new[] { a, a, b }, n => n.Deps, n => n.Id).Should().BeNull();
    }

    #endregion
}
