using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Canonical;

[TestClass]
public class ContentFormsTests
{
    private static readonly ExtensionGroupContentV1 Video = new("Video", [".mkv"]);

    [TestMethod]
    public void PublishedContentMergesUnknownMembersBackWithoutOverridingKnownOnes()
    {
        var unknown = new JsonObject { ["x-icon"] = "film", ["name"] = "Not this one" };
        var content = DataSyncContentForms.PublishedContent(ExtensionGroupCodec.Instance, Video, unknown);
        Assert.AreEqual("{\"extensions\":[\".mkv\"],\"name\":\"Video\",\"x-icon\":\"film\"}", CanonicalJson.Serialize(content));
        Assert.AreEqual("{\"name\":\"Not this one\",\"x-icon\":\"film\"}", CanonicalJson.Serialize(unknown),
            "the stored unknown members are not taken apart");

        // What a reader of that content gets back: the same unknown member, for preservation.
        var read = ExtensionGroupCodec.Instance.Read(content, DataSyncLimits.Default);
        Assert.AreEqual("{\"x-icon\":\"film\"}", CanonicalJson.Serialize(read.Unknown));
    }

    [TestMethod]
    public void TheComparisonFormKeepsUnknownMembersVerbatim()
    {
        var unknown = new JsonObject { ["x-icon"] = new JsonObject { ["b"] = 2, ["a"] = 1 } };
        var form = DataSyncContentForms.ComparisonForm(ExtensionGroupCodec.Instance, Video, null, false, unknown);
        Assert.AreEqual("{\"extensions\":[\".mkv\"],\"name\":\"Video\",\"x-icon\":{\"a\":1,\"b\":2}}",
            CanonicalJson.Serialize(form));
        Assert.AreNotEqual(DataSyncContentForms.SharedHash(ExtensionGroupCodec.Instance, Video, null, false, null),
            DataSyncContentForms.SharedHash(ExtensionGroupCodec.Instance, Video, null, false, unknown));
        Assert.AreEqual(DataSyncContentForms.SharedHash(ExtensionGroupCodec.Instance, Video, null, false, null),
            DataSyncContentForms.SharedHash(ExtensionGroupCodec.Instance, Video, null, false, new JsonObject()));
    }

    [TestMethod]
    public void SharedHashIgnoresChildIdsAndLocalOrderButNotTheOrderKey()
    {
        var codec = TestItemCodec.Instance;
        var a = new TestItemContent("Genre", null, [new TestChild("1", "Action"), new TestChild("2", "Drama")]);
        var b = new TestItemContent("Genre", null, [new TestChild("x", "Drama"), new TestChild("y", "Action")]);
        Assert.AreEqual(DataSyncContentForms.SharedHash(codec, a, "a0", false, null),
            DataSyncContentForms.SharedHash(codec, b, "a0", false, null));
        Assert.AreNotEqual(DataSyncContentForms.SharedHash(codec, a, "a0", false, null),
            DataSyncContentForms.SharedHash(codec, a, "a1", false, null));
        Assert.AreEqual(ContentHash.Of(codec.ComparisonForm(a, "a0", false)),
            DataSyncContentForms.SharedHash(codec, a, "a0", false, null));
    }
}
