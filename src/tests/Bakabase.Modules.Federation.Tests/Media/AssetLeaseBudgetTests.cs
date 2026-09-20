using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Media;

[TestClass]
public sealed class AssetLeaseBudgetTests
{
    [TestMethod]
    public void RejectsOversizedIdentityPathAndExecutableContentTypesBeforeRetainingThem()
    {
        var store = new AssetLeaseStore();
        var reference = new ResourceRef("node", "epoch", 1);
        Assert.AreEqual("InvalidAssetIdentity", Assert.ThrowsExactly<FederationQueryException>(() =>
            store.Issue(reference with { NodeId = new string('a', 129) }, "grant", 1, "/a.mp4", "video/mp4", "video")).Code);
        Assert.AreEqual("InvalidAssetIdentity", Assert.ThrowsExactly<FederationQueryException>(() =>
            store.Issue(reference, new string('a', 129), 1, "/a.mp4", "video/mp4", "video")).Code);
        Assert.AreEqual("AssetMetadataTooLarge", Assert.ThrowsExactly<FederationQueryException>(() =>
            store.Issue(reference, "grant", 1, "/" + new string('x', AssetLeaseStore.MaximumPathChars) + ".mp4",
                "video/mp4", "video")).Code);
        Assert.AreEqual("UnsupportedAsset", Assert.ThrowsExactly<FederationQueryException>(() =>
            store.Issue(reference, "grant", 1, "/image.png", "text/html", "image")).Code);
        Assert.AreEqual("AssetExpired", Assert.ThrowsExactly<FederationQueryException>(() => store.Get(null!, "grant")).Code);
        Assert.IsNotNull(store.Issue(reference, "grant", 1, "/a.mp4", "video/mp4", "video"));
    }

    [TestMethod]
    public void RetainedBytesAreBoundedReuseIsFreeAndExpiryAndRevocationReleaseTheBudget()
    {
        var clock = new Clock();
        var store = new AssetLeaseStore(clock);
        var reference = new ResourceRef("node", "epoch", 1);
        var prefix = "/" + new string('x', 3990);
        var first = store.Issue(reference, "grant", 1, prefix + "0.mp4", "video/mp4", "video");
        var count = FillUntilFull(1);
        Assert.IsTrue(count < AssetLeaseStore.MaximumLeases, "The byte budget must bind before the object-count limit.");
        Assert.AreEqual(first.AssetId, store.Issue(reference, "grant", 1, first.Path, "video/mp4", "video").AssetId);
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.AreEqual("AssetExpired", Assert.ThrowsExactly<FederationQueryException>(() => store.Get(first.AssetId, "grant")).Code);
        Assert.AreEqual(count, FillUntilFull(0), "Expired leases must release all of their retained byte charges.");
        store.Revoke("grant");
        Assert.IsNotNull(store.Issue(reference, "grant-two", 1, prefix + "after-revoke.mp4", "video/mp4", "video"));

        int FillUntilFull(int start)
        {
            for (var i = start; i <= AssetLeaseStore.MaximumLeases; i++)
            {
                try { store.Issue(reference, "grant", 1, prefix + i + ".mp4", "video/mp4", "video"); }
                catch (FederationQueryException error)
                {
                    Assert.AreEqual("Busy", error.Code);
                    Assert.AreEqual(503, error.StatusCode);
                    return i;
                }
            }
            Assert.Fail("Expected the finite asset byte budget to refuse additional leases.");
            return 0;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
