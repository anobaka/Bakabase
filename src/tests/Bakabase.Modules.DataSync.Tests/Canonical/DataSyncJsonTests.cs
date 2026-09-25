using System.Text.Json;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
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

    private sealed record IdentityHolder(SyncKey Key, DataSyncActorId Actor, DataSyncActorId? LastActor, EntityKeys Keys,
        EntityKeys None);

    [TestMethod]
    public void WritesIdentityValuesAsTheirPlainForms()
    {
        var key = new SyncKey(new string('a', 32));
        var alias = new SyncKey(new string('b', 32));
        var actor = new DataSyncActorId("0123456789abcdef");
        var holder = new IdentityHolder(key, actor, actor, new EntityKeys([key, alias]), EntityKeys.None);
        var json = JsonSerializer.Serialize(holder, DataSyncJson.Options);
        Assert.AreEqual(
            $"{{\"key\":\"{key}\",\"actor\":\"{actor}\",\"lastActor\":\"{actor}\",\"keys\":[\"{key}\",\"{alias}\"],\"none\":[]}}",
            json);

        var read = JsonSerializer.Deserialize<IdentityHolder>(json, DataSyncJson.Options)!;
        Assert.AreEqual(key, read.Key);
        Assert.AreEqual(actor, read.Actor);
        Assert.AreEqual(actor, read.LastActor);
        CollectionAssert.AreEqual(holder.Keys.All.ToArray(), read.Keys.All.ToArray());
        Assert.AreSame(EntityKeys.None, read.None);

        var withoutLastActor = JsonSerializer.Serialize(holder with { LastActor = null }, DataSyncJson.Options);
        Assert.IsFalse(withoutLastActor.Contains("lastActor"));
        Assert.IsNull(JsonSerializer.Deserialize<IdentityHolder>(withoutLastActor, DataSyncJson.Options)!.LastActor);
    }

    [TestMethod]
    public void RefusesMalformedIdentityValues()
    {
        var actor = "0123456789abcdef";
        var key = new string('a', 32);
        foreach (var json in new[]
                 {
                     $"{{\"key\":\"nope\",\"actor\":\"{actor}\",\"keys\":[],\"none\":[]}}",
                     $"{{\"key\":\"{key}\",\"actor\":\"NOPE\",\"keys\":[],\"none\":[]}}",
                     $"{{\"key\":\"{key}\",\"actor\":\"{actor}\",\"keys\":[\"x\"],\"none\":[]}}",
                     $"{{\"key\":\"{key}\",\"actor\":\"{actor}\",\"keys\":\"{key}\",\"none\":[]}}",
                     $"{{\"key\":1,\"actor\":\"{actor}\",\"keys\":[],\"none\":[]}}",
                 })
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<IdentityHolder>(json, DataSyncJson.Options),
                json);
    }

    [TestMethod]
    public void OptionsAreReadOnly()
    {
        Assert.IsTrue(DataSyncJson.Options.IsReadOnly);
        Assert.AreEqual(64, DataSyncJson.Options.MaxDepth);
    }
}
