using Bakabase.Modules.Downloader.Cloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PikPakShareLocatorTests
{
    private const string ShareId = "VP34KSLprDtv30ftAvk2FVfao2";
    private const string FolderId = "VP34AFYjetF32396Hp3N0Sp3o2";
    private const string EncodedFolderId = "AAAAKXTK1uoYBDiXHGT7xH2to2_VP3";

    [TestMethod]
    public void Official_folder_path_decodes_without_discarding_selection_or_password()
    {
        var url = $"https://mypikpak.com/s/{ShareId}/{EncodedFolderId}?pwd=access";
        Assert.IsTrue(PikPakShareLocator.TryParse(url, out var locator));
        Assert.AreEqual(ShareId, locator!.ShareId);
        CollectionAssert.AreEqual(new[] {FolderId}, locator.NodeIds.ToArray());
        Assert.AreEqual("pikpak", CloudSourceClassifier.Classify(url));
        var root = new CloudSource {Id = "one", ProviderId = "pikpak", Url = $"https://mypikpak.com/s/{ShareId}"};
        Assert.AreNotEqual(CloudSourceIdentity.Key(root), CloudSourceIdentity.Key(root with {Url = url}));
        StringAssert.Contains(CloudSourceIdentity.CanonicalLocator(root with {Url = url}), "?pwd=access");
    }

    [TestMethod]
    public void Raw_node_ids_and_multiple_folder_segments_are_preserved_in_order()
    {
        var child = "VP34AKMeppAzZZ4DNMYgaTp1o2";
        Assert.IsTrue(PikPakShareLocator.TryParse($"https://www.mypikpak.com/s/{ShareId}/{EncodedFolderId}/{child}/", out var locator));
        CollectionAssert.AreEqual(new[] {FolderId, child}, locator!.NodeIds.ToArray());
    }

    [DataTestMethod]
    [DataRow("http://mypikpak.com/s/fixture")]
    [DataRow("https://mypikpak.com:8080/s/fixture")]
    [DataRow("https://user:password@mypikpak.com/s/fixture")]
    [DataRow("https://mypikpak.com.attacker.test/s/fixture")]
    [DataRow("https://mypikpak.com/s/VP34KSLprDtv30ftAvk2FVfao2/archive/encoded")]
    [DataRow("https://mypikpak.com/s/VP34KSLprDtv30ftAvk2FVfao2/AAAAKXTK1uoYBDiXHGT7xH2to2_XXX")]
    [DataRow("https://mypikpak.com/s/VP34KSLprDtv30ftAvk2FVfao2//")]
    public void Unknown_or_unsafe_paths_are_not_treated_as_the_share_root(string url) =>
        Assert.IsFalse(PikPakShareLocator.TryParse(url, out _));
}
