using Bakabase.Abstractions.Components.ResourceMove;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ResourceMoveGuardExtensionTests
{
    [TestMethod]
    public void RetryingMoreRecords_ExtendsResourceAndPathReservation()
    {
        var guard = new ResourceMoveGuard();
        Assert.IsTrue(guard.TryReserve("batch", [1], ["/a"], out _));
        guard.Retain("batch");
        Assert.IsTrue(guard.TryExtendReservation("batch", [2], ["/b"], out _));
        Assert.IsTrue(guard.IsResourceLocked(1));
        Assert.IsTrue(guard.IsResourceLocked(2));
        Assert.IsTrue(guard.HasRetainedReservations);
        Assert.IsFalse(guard.TryReserve("other", [3], ["/b/child"], out _));
        Assert.IsFalse(guard.TryReserve("batch", [1], ["/a"], out _));
    }

    [TestMethod]
    public void ConflictingExtension_LeavesOriginalReservationUnchanged()
    {
        var guard = new ResourceMoveGuard();
        guard.TryReserve("batch", [1], ["/a"], out _);
        guard.TryReserve("other", [2], ["/b"], out _);
        Assert.IsFalse(guard.TryExtendReservation("batch", [3], ["/b/child"], out var conflict));
        Assert.AreEqual("/b", conflict);
        Assert.IsFalse(guard.IsResourceLocked(3));
        CollectionAssert.AreEquivalent(new[] { 1 }, guard.GetReservedResourceIds("batch"));
        CollectionAssert.AreEquivalent(new[] { "/a" }, guard.GetReservedPaths("batch"));
    }
}
