using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

public sealed partial class MetadataReplicaCoordinatorTests
{
    [TestMethod]
    public async Task Definition_catalogs_read_from_the_paired_owner_replica_without_network_or_id_merging()
    {
        await SeedAsync();
        var checkpoint = (await _store.GetStatusAsync(Owner)).Checkpoint!;
        await _store.ApplyBatchAsync(Owner, new(checkpoint, checkpoint with {CatalogRevision = 2}, [], [],
            [new("extensionGroups", "7", """{"id":7,"name":"Owner videos","extensions":[".mkv"]}"""),
             new("collections", "7", """{"id":7,"name":"Owner series","order":3,"hasRule":true}""")], []));
        var other = Owner with {ServerId = "other-owner"};
        var second = await _store.BeginSnapshotAsync(other, "other-generation", 1);
        await _store.ApplyBatchAsync(other, new(second, second with {Complete = true}, [], [],
            [new("extensionGroups", "7", """{"id":7,"name":"Not this owner","extensions":[".png"]}""")], []));
        _wire.Calls.Clear();
        foreach (var (operation, name) in new[] {("extensionGroups", "Owner videos"), ("getAllCollections", "Owner series")})
        {
            var response = JObject.Parse((await Service().ExecuteAsync(Owner.ServerId, new(Owner.LibraryEpoch, operation), default)).Body);
            Assert.AreEqual(7, (int)response["data"]![0]!["id"]!);
            Assert.AreEqual(name, (string)response["data"]![0]!["name"]!);
        }
        Assert.IsEmpty(_wire.Calls, "Reading a synced catalog must not fetch the owner's entire native catalog again.");
    }

    [TestMethod]
    public async Task Catalog_only_rename_and_reorder_overlay_resource_get_and_search_without_rewriting_values()
    {
        await SeedAsync();
        var property = JObject.Parse("""
            {"name":"Original name","type":3,"order":1,"visible":true,"profileScopePriority":[2,1],
             "values":[{"scope":1,"value":"choice-id","bizValue":"Choice label","aliasAppliedBizValue":"Preferred alias"}]}
            """);
        var resource = new JObject { ["id"] = 1, ["displayName"] = "Owner display name",
            ["properties"] = new JObject { ["4"] = new JObject { ["7"] = property } },
            ["parent"] = new JObject { ["id"] = 99, ["properties"] = new JObject { ["4"] = new JObject { ["7"] = property.DeepClone() } } } };
        var checkpoint = (await _store.GetStatusAsync(Owner)).Checkpoint!;
        var definition = new ReplicaCatalog("properties", "4:7", JsonConvert.SerializeObject(new
        {
            pool = 4, id = 7, type = 3, name = "Original name", order = 1,
            options = new { choices = new[] { new { value = "choice-id", label = "Choice label" } } }
        }));
        await _store.ApplyBatchAsync(Owner, new(checkpoint, checkpoint with {Sequence = checkpoint.Sequence + 1},
            [new(1, 1, resource.ToString(Formatting.None), new(), [])], [], [definition], []));
        checkpoint = (await _store.GetStatusAsync(Owner)).Checkpoint!;
        var changed = JObject.Parse(definition.Json);
        changed["name"] = "Renamed in current catalog";
        changed["order"] = 42;
        await _store.ApplyBatchAsync(Owner, new(checkpoint, checkpoint with {CatalogRevision = 2},
            [], [], [definition with {Json = changed.ToString(Formatting.None)}], []));
        _wire.Calls.Clear();
        foreach (var request in new[] {Get(), new ManagedLibraryRequest(Owner.LibraryEpoch, "resourceSearch",
                     Body: JObject.FromObject(new {page = 1, pageSize = 10}))})
        {
            var response = JObject.Parse((await Service().ExecuteAsync(Owner.ServerId, request, default)).Body);
            var returned = response["data"]![0]!;
            var current = returned["properties"]!["4"]!["7"]!;
            Assert.AreEqual("Renamed in current catalog", (string)current["name"]!);
            Assert.AreEqual(42, (int)current["order"]!);
            Assert.AreEqual("Renamed in current catalog", (string)returned["parent"]!["properties"]!["4"]!["7"]!["name"]!);
            Assert.IsTrue(JToken.DeepEquals(property["values"], current["values"]));
            Assert.IsTrue(JToken.DeepEquals(property["profileScopePriority"], current["profileScopePriority"]));
            Assert.IsTrue((bool)current["visible"]!);
            Assert.AreEqual("Owner display name", (string)returned["displayName"]!);
        }
        Assert.IsEmpty(_wire.Calls, "A local cached read must not contact the owner to repair display labels.");
        Assert.AreEqual("Original name", (string)JObject.Parse((await _store.GetByIdsAsync(Owner, [1])).Single())
            ["properties"]!["4"]!["7"]!["name"]!);
        var optionsResponse = JObject.Parse((await Service().ExecuteAsync(Owner.ServerId,
            new(Owner.LibraryEpoch, "properties"), default)).Body);
        Assert.AreEqual("choice-id", (string)optionsResponse["data"]![0]!["options"]!["choices"]![0]!["value"]!);
    }

    [TestMethod]
    public async Task Property_display_overlay_never_transplants_another_owner_or_reinterprets_a_different_type()
    {
        await SeedAsync();
        var checkpoint = (await _store.GetStatusAsync(Owner)).Checkpoint!;
        const string resource = """{"id":1,"properties":{"4":{"7":{"name":"Stored name","type":1,"values":[]}}}}""";
        await _store.ApplyBatchAsync(Owner, new(checkpoint, checkpoint with {Sequence = checkpoint.Sequence + 1},
            [new(1, 1, resource, new(), [])], [], [new("properties", "4:7", """{"pool":4,"id":7,"name":"Wrong type","type":5,"order":9}""")], []));
        var result = JObject.Parse((await Service().ExecuteAsync(Owner.ServerId, Get(), default)).Body);
        Assert.AreEqual("Stored name", (string)result["data"]![0]!["properties"]!["4"]!["7"]!["name"]!);
        var secondOwner = Owner with {ServerId = "different-owner"};
        var otherStart = await _store.BeginSnapshotAsync(secondOwner, "other-generation", 1);
        await _store.ApplyBatchAsync(secondOwner, new(otherStart, otherStart with {Complete = true}, [], [],
            [new("properties", "4:7", """{"pool":4,"id":7,"name":"Other device name","type":1,"order":99}""")], []));
        result = JObject.Parse((await Service().ExecuteAsync(Owner.ServerId, Get(), default)).Body);
        Assert.AreEqual("Stored name", (string)result["data"]![0]!["properties"]!["4"]!["7"]!["name"]!);
    }
    [TestMethod]
    public async Task Display_catalog_rejects_a_generation_swap_even_for_a_page_without_properties()
    {
        await SeedAsync();
        var previous = (await _store.GetStatusAsync(Owner)).ActiveGeneration!;
        var next = await _store.BeginSnapshotAsync(Owner, "new-display-generation", 2);
        await _store.ApplyBatchAsync(Owner, new(next, next with {Complete = true}, [], [], [], []));
        await Assert.ThrowsExactlyAsync<ReplicaCheckpointConflictException>(() =>
            _store.ReadPropertyDisplayAsync(Owner, previous, [], default));
    }

}
