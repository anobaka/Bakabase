using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Queries;

[TestClass]
public class LocalSnapshotTests
{
    private static readonly FederationQueryLimits Limits = new() { BlockSize = 2, MaxBlockSize = 4 };

    [TestMethod]
    public async Task FrozenBlocksAreReplayableDespiteLiveEditsAndReturnedArrayMutation()
    {
        using var peer = new TestPeer("a", Limits, null, "c", "b", "a", "d");
        var first = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 2 });
        CollectionAssert.AreEqual(new[] { "a", "b" }, first.Items.Select(i => i.Title).ToArray());
        peer.Reader.Rows.Clear();
        first.Items[0] = first.Items[0] with { Title = "mutated" };
        var second = await peer.Store.ReadAsync("grant", first.SnapshotId, first.NextCursor!);
        var replay = await peer.Store.ReadAsync("grant", first.SnapshotId, first.NextCursor!);
        CollectionAssert.AreEqual(new[] { "c", "d" }, second.Items.Select(i => i.Title).ToArray());
        CollectionAssert.AreEqual(second.Items.Select(i => i.Ref).ToArray(), replay.Items.Select(i => i.Ref).ToArray());
        Assert.AreEqual(4L, second.TotalCount);
    }

    [TestMethod]
    public async Task CursorsCannotCrossGrantSnapshotOrEpoch()
    {
        using var peer = new TestPeer("a", Limits, null, "a", "b", "c");
        var first = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 });
        var denied = await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
            peer.Store.ReadAsync("other", first.SnapshotId, first.NextCursor!));
        Assert.AreEqual("CapabilityDenied", denied.Code);
        var second = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 });
        var invalid = await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
            peer.Store.ReadAsync("grant", second.SnapshotId, first.NextCursor!));
        Assert.AreEqual("InvalidCursor", invalid.Code);
        peer.Reader.Epoch = "new-epoch";
        var changed = await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
            peer.Store.ReadAsync("grant", first.SnapshotId, first.NextCursor!));
        Assert.AreEqual("LibraryEpochChanged", changed.Code);
    }

    [TestMethod]
    public async Task RevocationAndAbsoluteTtlApplyToEveryReplay()
    {
        var clock = new ManualClock();
        using var peer = new TestPeer("a", Limits, clock, "a", "b", "c");
        var first = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 });
        await peer.Store.ReadAsync("grant", first.SnapshotId, first.NextCursor!);
        peer.Access.Revoked = true;
        var revoked = await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
            peer.Store.ReadAsync("grant", first.SnapshotId, first.NextCursor!));
        Assert.AreEqual("GrantRevoked", revoked.Code);
        peer.Access.Revoked = false;
        var fresh = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 });
        clock.Advance(TimeSpan.FromMinutes(11));
        var expired = await Assert.ThrowsExceptionAsync<FederationQueryException>(() =>
            peer.Store.ReadAsync("grant", fresh.SnapshotId, fresh.NextCursor!));
        Assert.AreEqual("QuerySessionExpired", expired.Code);
    }

    [TestMethod]
    public async Task FailedOversizeCaptureDoesNotLeaveAValidPartialSnapshotOrQuotaSlot()
    {
        var limits = Limits with { MaxCaptureBytes = 300, MaxSnapshotsPerGrant = 1 };
        using var peer = new TestPeer("a", limits, null, "a", "b", "c");
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => peer.Store.CreateAsync("grant",
            new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 }));
        Assert.AreEqual("ScanBudgetExceeded", error.Code);
        peer.Reader.Rows.RemoveRange(1, 2);
        var result = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 });
        Assert.AreEqual(1L, result.TotalCount);
    }

    [TestMethod]
    public async Task ByteBoundedBlocksUseReplayableOffsetsRatherThanPageSizeMultiples()
    {
        using var peer = new TestPeer("a", Limits with { MaxBlockBytes = 1200 }, null,
            new string('a', 40), new string('b', 40), new string('c', 40));
        var first = await peer.Store.CreateAsync("grant", new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 3 });
        Assert.AreEqual(1, first.Items.Length);
        var second = await peer.Store.ReadAsync("grant", first.SnapshotId, first.NextCursor!);
        Assert.AreEqual(1, second.Offset);
        Assert.AreEqual(new string('b', 40), second.Items.Single().Title);
        var third = await peer.Store.ReadAsync("grant", first.SnapshotId, second.NextCursor!);
        Assert.IsNull(third.NextCursor);
    }

    [TestMethod]
    public async Task ResultBudgetIsDistinctFromZeroMatchScanBudget()
    {
        using var peer = new TestPeer("a", Limits with { MaxSnapshotBytes = 300 }, null, "a");
        var large = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => peer.Store.CreateAsync("grant",
            new() { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1 }));
        Assert.AreEqual("ResultSnapshotTooLarge", large.Code);
        var empty = await peer.Store.CreateAsync("grant", new()
            { ExpectedLibraryEpoch = peer.Reader.Epoch, BlockSize = 1, Query = new() { Text = "unmatched" } });
        Assert.AreEqual(0L, empty.TotalCount);
        Assert.IsNull(empty.NextCursor);
    }
}
