using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Media;

[TestClass]
public sealed class AssetBoundaryTests
{
    [TestMethod]
    public void AssetCapabilitiesAreBoundToGrantAndFullResourceIdentity()
    {
        var store = new AssetLeaseStore();
        var a = new ResourceRef("node-a", "epoch-a", 1);
        var b = new ResourceRef("node-b", "epoch-b", 1);
        var first = store.Issue(a, "grant-a", 1, "/a.mp4", "video/mp4", "video");
        var second = store.Issue(b, "grant-b", 1, "/b.mp4", "video/mp4", "video");
        Assert.AreNotEqual(first.AssetId, second.AssetId);
        Assert.ThrowsException<FederationQueryException>(() => store.Get(first.AssetId, "grant-b"));
        store.Revoke("grant-a");
        Assert.ThrowsException<FederationQueryException>(() => store.Get(first.AssetId, "grant-a"));
        Assert.AreEqual(b, store.Get(second.AssetId, "grant-b").ResourceRef);
    }

    [TestMethod]
    public void PathMappingRejectsTraversalSiblingPrefixAndAncestorSymlinks()
    {
        var temp = Path.Combine(Path.GetTempPath(), "bakabase-federation-paths-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "library");
        var outside = Path.Combine(temp, "library-extra");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(root, "ok.mp4"), "media");
            File.WriteAllText(Path.Combine(outside, "private.mp4"), "private");
            Assert.IsNotNull(MediaPathBoundary.Map(root, "ok.mp4"));
            Assert.IsNull(MediaPathBoundary.Map(root, "../library-extra/private.mp4"));
            Assert.IsNull(MediaPathBoundary.Map(root, "/etc/passwd"));
            Assert.IsNull(MediaPathBoundary.Map(root, "..\\library-extra\\private.mp4"));
            Assert.IsFalse(MediaPathBoundary.IsWithin(root, Path.Combine(outside, "private.mp4")));
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "escape"), outside);
                Assert.IsNull(MediaPathBoundary.Map(root, "escape/private.mp4"));
            }
        }
        finally { Directory.Delete(temp, true); }
    }
}
