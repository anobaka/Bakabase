using System.Text.Json;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Canonical;

[TestClass]
public class DataSyncJsonTests
{
    [TestMethod]
    public void WritesCamelCaseMembersEnumNamesAndNoNulls()
    {
        var flags = new DataSyncMergeFlags(DeletionsAsItems: true, ChildDeletions: DataSyncChildDeletionMode.ReviewEach);
        var json = JsonSerializer.Serialize(flags, DataSyncJson.Options);
        Assert.AreEqual(
            "{\"deletionsAsItems\":true,\"skipDeletionBreaker\":false,\"skipLargeChange\":false,\"childDeletions\":\"ReviewEach\"}",
            json);
        Assert.AreEqual(flags, JsonSerializer.Deserialize<DataSyncMergeFlags>(json, DataSyncJson.Options));

        var overlay = new DataSyncOverlay(["a"], [new DataSyncHeldChild("b", 3)]);
        Assert.AreEqual("{\"localOnlyChildren\":[\"a\"],\"heldChildren\":[{\"childId\":\"b\",\"linkId\":3}]}",
            JsonSerializer.Serialize(overlay, DataSyncJson.Options));
        Assert.AreEqual("{\"nodeId\":\"n\",\"name\":\"PC\",\"actorId\":\"a\"}",
            JsonSerializer.Serialize(new DataSyncEditorRef("n", "PC", "a"), DataSyncJson.Options));
    }

    [TestMethod]
    public void KeepsDictionaryKeysAsTheyAre()
    {
        var cursors = new Dictionary<string, long> { ["customProperty"] = 4, ["ExtensionGroup"] = 2 };
        Assert.AreEqual("{\"customProperty\":4,\"ExtensionGroup\":2}", JsonSerializer.Serialize(cursors, DataSyncJson.Options));
    }

    [TestMethod]
    public void OptionsAreReadOnly()
    {
        Assert.IsTrue(DataSyncJson.Options.IsReadOnly);
        Assert.AreEqual(64, DataSyncJson.Options.MaxDepth);
    }
}
