using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Queries;

[TestClass]
public class CoordinatorTests
{
    private static readonly FederationQueryLimits Limits = new() { BlockSize = 2, MaxBlockSize = 4 };

    [TestMethod]
    public async Task CompleteUnevenThreeNodeTraversalMatchesReferenceSortWithoutMissingOrDuplicateRows()
    {
        using var a = new TestPeer("a", Limits, null, "a", "b", "c", "d", "e", "f", "z", "same", null);
        using var b = new TestPeer("b", Limits, null, "w", "same", "y");
        using var c = new TestPeer("c", Limits, null, "same", "x", null, "r", "s");
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b, c), Limits);
        foreach (var sort in new[] { "NameAsc", "NameDesc" })
        {
            var expected = new List<FederatedResourceSummary>();
            foreach (var peer in new[] { a, b, c })
            {
                var capture = await peer.Reader.CaptureAsync(new(1_000_000, 1000), default);
                expected.AddRange(capture.Resources.Select(r => QueryProtocol.Project(capture, r)));
            }
            expected.Sort(QueryProtocol.Comparer(sort));
            var page = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b", "c"], PageSize = 3, Query = new() { Sort = sort } });
            var all = page.Items.ToList();
            Assert.IsTrue(page.CoverageComplete);
            while (page.NextCursor != null)
            {
                page = await coordinator.ReadAsync("ui", page.SessionId, page.NextCursor);
                all.AddRange(page.Items);
            }
            CollectionAssert.AreEqual(expected.Select(i => i.Ref).ToArray(), all.Select(i => i.Ref).ToArray());
            Assert.AreEqual(all.Count, all.Select(i => i.Ref).Distinct().Count());
            await coordinator.ReleaseAsync("ui", page.SessionId);
        }
    }

    [TestMethod]
    public async Task LostResponseAndConcurrentNextReplayExactlyOneCommittedPage()
    {
        using var a = new TestPeer("a", Limits, null, "a", "b", "c", "d", "e", "f");
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a), Limits);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a"], PageSize = 1 });
        var cursor = first.NextCursor!;
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => coordinator.ReadAsync("ui", first.SessionId, cursor)));
        Assert.IsTrue(concurrent.All(p => p.Items.Single().Title == "b"));
        Assert.AreEqual(1, concurrent.Select(p => p.NextCursor).Distinct().Count());
        var replay = await coordinator.ReadAsync("ui", first.SessionId, cursor);
        Assert.AreEqual("b", replay.Items.Single().Title);
        var third = await coordinator.ReadAsync("ui", first.SessionId, replay.NextCursor!);
        Assert.AreEqual("c", third.Items.Single().Title);
        var old = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => coordinator.ReadAsync("ui", first.SessionId, cursor));
        Assert.AreEqual("CursorSuperseded", old.Code);
    }

    [TestMethod]
    public async Task FailedMidPageRefillDoesNotCommitOtherNodesPositions()
    {
        using var a = new TestPeer("a", Limits, null, "a", "b", "c", "d", "e", "f");
        using var b = new TestPeer("b", Limits, null, "z", "zz");
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b), Limits);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b"], PageSize = 3 });
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, first.Items.Select(i => i.Title).ToArray());
        a.Client.FailReadCall = a.Client.ReadCalls + 1;
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!));
        Assert.AreEqual("QuerySessionInterrupted", error.Code);
        var recovered = await coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!);
        CollectionAssert.AreEqual(new[] { "d", "e", "f" }, recovered.Items.Select(i => i.Title).ToArray());
    }

    [TestMethod]
    public async Task BufferedAndPreviouslyCachedPagesAreReauthorized()
    {
        using var a = new TestPeer("a", Limits, null, "a", "b", "c");
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a), Limits);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a"], PageSize = 1 });
        await coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!);
        a.Access.Revoked = true;
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!));
        Assert.AreEqual("GrantRevoked", error.Code);
    }

    [TestMethod]
    public async Task InitialFailuresAreExplicitAndSuccessfulZeroIsNotFailure()
    {
        using var a = new TestPeer("a", Limits, null);
        using var b = new TestPeer("b", Limits, null, "b");
        b.Client.FailCreate = true;
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b), Limits);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b"], PageSize = 1 });
        Assert.IsFalse(first.CoverageComplete);
        Assert.AreEqual(0L, first.TotalWithinParticipants);
        Assert.AreEqual("a", first.Participants.Single().NodeId);
        Assert.AreEqual("b", first.OmittedNodes.Single().NodeId);
        await coordinator.ReleaseAsync("ui", first.SessionId);
        a.Client.FailCreate = true;
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => coordinator.CreateAsync("ui",
            new() { NodeIds = ["a", "b"] }));
        Assert.AreEqual(2, error.OmittedNodes!.Count);
    }

    [TestMethod]
    public async Task DifferentNodeBlockSizesAndExhaustedNodeRevocationKeepTheParticipantContract()
    {
        using var a = new TestPeer("a", Limits, null, "a");
        using var b = new TestPeer("b", Limits, null, "b", "c", "d", "e", "f", "g");
        a.Client.BlockSizeOverride = 1;
        b.Client.BlockSizeOverride = 3;
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b), Limits);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b"], PageSize = 2 });
        CollectionAssert.AreEqual(new[] { "a", "b" }, first.Items.Select(i => i.Title).ToArray());
        var second = await coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!);
        CollectionAssert.AreEqual(new[] { "c", "d" }, second.Items.Select(i => i.Title).ToArray());
        a.Access.Revoked = true; // Its stream is exhausted but still contributes to this session's count/coverage.
        var revoked = await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
            coordinator.ReadAsync("ui", first.SessionId, second.NextCursor!));
        Assert.AreEqual("GrantRevoked", revoked.Code);
    }

    [TestMethod]
    public async Task DelayedNodeIsOmittedAtPreparationDeadlineAndNeverInsertedOnNextPage()
    {
        var limits = Limits with { PreparationTimeout = TimeSpan.FromMilliseconds(50) };
        using var a = new TestPeer("a", limits, null, "a", "c", "e");
        using var b = new TestPeer("b", limits, null, "b", "d");
        b.Client.CreationDelay = TimeSpan.FromSeconds(1);
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b), limits);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b"], PageSize = 1 });
        Assert.AreEqual("a", first.Participants.Single().NodeId);
        Assert.AreEqual("b", first.OmittedNodes.Single().NodeId);
        b.Client.CreationDelay = TimeSpan.Zero;
        var second = await coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!);
        Assert.AreEqual("c", second.Items.Single().Title);
        Assert.AreEqual("a", second.Participants.Single().NodeId);
    }

    [TestMethod]
    public async Task ReleasingAndRepeatedFailedPreparationsReturnAllReservationsAndQuota()
    {
        var limits = Limits with { MaxSessionsPerOwner = 1, MaxCoordinatorSessions = 1 };
        using var peer = new TestPeer("a", limits, null, "a", "b", "c", "d");
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(peer), limits);
        for (var i = 0; i < 5; i++)
        {
            peer.Client.FailCreate = true;
            await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
                coordinator.CreateAsync("ui", new() { NodeIds = ["a"], PageSize = 1 }));
            peer.Client.FailCreate = false;
            var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a"], PageSize = 1 });
            var second = await coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!);
            Assert.AreEqual("b", second.Items.Single().Title);
            await coordinator.ReleaseAsync("ui", first.SessionId);
        }
        Assert.AreEqual(5, peer.Client.ReleaseCalls);
    }

    [TestMethod]
    public async Task LateSuccessfulSnapshotIsReleasedEvenWhenThePeerIgnoresCancellation()
    {
        var limits = Limits with { PreparationTimeout = TimeSpan.FromMilliseconds(30) };
        using var a = new TestPeer("a", limits, null, "a", "c");
        using var b = new TestPeer("b", limits, null, "b");
        b.Client.CreationDelay = TimeSpan.FromMilliseconds(100);
        b.Client.IgnoreCreateCancellation = true;
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b), limits);
        var result = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b"], PageSize = 1 });
        Assert.AreEqual("a", result.Participants.Single().NodeId);
        await Task.Delay(150);
        Assert.AreEqual(1, b.Client.ReleaseCalls);
        var next = await coordinator.ReadAsync("ui", result.SessionId, result.NextCursor!);
        Assert.AreEqual("c", next.Items.Single().Title);
    }

    [TestMethod]
    public async Task CompletionAfterDeadlineCannotJoinWhenTheDeadlineCallbackHasNotRunYet()
    {
        var clock = new ManualClock();
        var limits = Limits with { PreparationTimeout = TimeSpan.FromMilliseconds(30) };
        using var a = new TestPeer("a", limits, clock, "a", "c");
        using var b = new TestPeer("b", limits, clock, "b");
        b.Client.AfterCreate = () => clock.Advance(TimeSpan.FromMilliseconds(31));
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a, b), limits, clock);
        var result = await coordinator.CreateAsync("ui", new() { NodeIds = ["a", "b"], PageSize = 1 });
        Assert.AreEqual("a", result.Participants.Single().NodeId);
        Assert.AreEqual("QueryDeadlineExceeded", result.OmittedNodes.Single().Code);
        Assert.AreEqual(1, b.Client.ReleaseCalls);
    }

    [TestMethod]
    public async Task SessionTtlIsAbsoluteAndDoesNotRenewWithPages()
    {
        var clock = new ManualClock();
        using var a = new TestPeer("a", Limits, clock, "a", "b", "c");
        using var coordinator = new FederatedQueryCoordinator(new FixedTargets(a), Limits, clock);
        var first = await coordinator.CreateAsync("ui", new() { NodeIds = ["a"], PageSize = 1 });
        clock.Advance(TimeSpan.FromMinutes(4));
        var second = await coordinator.ReadAsync("ui", first.SessionId, first.NextCursor!);
        Assert.IsTrue(second.ExpiresInMs <= 60_000);
        clock.Advance(TimeSpan.FromMinutes(2));
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => coordinator.ReadAsync("ui", first.SessionId, second.NextCursor!));
        Assert.AreEqual("QuerySessionExpired", error.Code);
    }
}
