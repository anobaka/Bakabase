using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Models.Input;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

public sealed partial class ResourceSearchIndexIncrementalTests
{
    private sealed class MetadataTestIdentity : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("source-device", "source-epoch", "Source"));
    }

    private MetadataSourceFeed Feed() => new(new(_sp.GetRequiredService<BakabaseDbContext>(),
            new MetadataSourceJournalGate()), Projection(), new MetadataTestIdentity(),
        _sp.GetRequiredService<IBOptionsManager<ResourceOptions>>());

    [TestMethod]
    public async Task MetadataFeed_ResumesAfterSnapshotPageAndReplaysChangesAndDeletesBehindTheScan()
    {
        await Seed("A", "B", "C");
        await BuildIndex();
        var a = await ResourceId("A");
        var b = await ResourceId("B");
        var c = await ResourceId("C");
        var feed = Feed();
        var head = await feed.ManifestAsync(default);
        var directory = Path.Combine(_testRoot, "replica");
        var store = new MetadataReplicaStore(directory);
        var checkpoint = await store.BeginSnapshotAsync(head.Identity, head.Start.Generation,
            head.Start.Sequence, head.Start.Cursor);
        for (var page = 0; page < 300; page++)
        {
            var batch = await feed.PageAsync(new(checkpoint, 1), default);
            await store.ApplyBatchAsync(head.Identity, batch);
            checkpoint = batch.Next;
            if (batch.Upserts.Any(r => r.ResourceId == a)) break;
            if (page == 299) Assert.Fail("The resource scan did not begin.");
        }
        Assert.IsFalse(checkpoint.Complete);
        await Assert.ThrowsExactlyAsync<ReplicaNotReadyException>(() => store.GetByIdsAsync(head.Identity, [a]));
        var resources = _sp.GetRequiredService<IResourceService>();
        await resources.DeleteByKeys([a]);
        await resources.Pin(b, true);
        await Index().WaitForPendingUpdatesAsync(default);
        // Both sides can reconstruct the continuation using only committed on-disk state.
        store = new MetadataReplicaStore(directory);
        feed = Feed();
        checkpoint = (await store.GetStatusAsync(head.Identity)).Checkpoint!;
        for (var page = 0; !checkpoint.Complete && page < 300; page++)
        {
            var batch = await feed.PageAsync(new(checkpoint, 1), default);
            await store.ApplyBatchAsync(head.Identity, batch);
            checkpoint = batch.Next;
        }
        Assert.IsTrue(checkpoint.Complete);
        var rows = await store.GetRowsByIdsAsync(head.Identity, [a, b, c]);
        CollectionAssert.AreEqual(new[] {b, c}, rows.Select(r => r.ResourceId).ToArray());
        Assert.IsTrue((bool)JObject.Parse(rows.Single(r => r.ResourceId == b).ResourceJson)["pinned"]!);
        var local = await new MetadataReplicaQuery(store).SearchAsync(head.Identity,
            new ResourceSearchInputModel {Keyword = "B", Page = 1, PageSize = 20});
        Assert.AreEqual(1L, local.TotalCount);
        Assert.AreEqual(b, (int)JObject.Parse(local.ResourceJson.Single())["id"]!);
        await resources.DeleteByKeys([c]);
        await Index().WaitForPendingUpdatesAsync(default);
        var delta = await feed.PageAsync(new(checkpoint), default);
        await store.ApplyBatchAsync(head.Identity, delta);
        Assert.AreEqual(1L, (await store.GetStatusAsync(head.Identity)).ResourceCount);
    }

    [TestMethod]
    public async Task MetadataFeed_RejectsAnOldCatalogCursorInsteadOfMixingPropertyDefinitions()
    {
        await Seed("Resource");
        await BuildIndex();
        var feed = Feed();
        var head = await feed.ManifestAsync(default);
        await AddMemoryTestProperty(Bakabase.Abstractions.Models.Domain.Constants.PropertyType.Number);
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            feed.PageAsync(new(head.Start), default));
        Assert.AreEqual("MetadataGenerationChanged", error.ErrorCode);
        Assert.AreNotEqual(head.Start.Generation, (await feed.ManifestAsync(default)).Start.Generation);
    }
}
