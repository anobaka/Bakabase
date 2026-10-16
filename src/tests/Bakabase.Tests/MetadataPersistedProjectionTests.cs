using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

public sealed partial class ResourceSearchIndexIncrementalTests
{
    [TestMethod]
    public async Task PersistedMetadata_ExportsCommittedRowsAndPostingsWithoutAnEventOrGlobalCacheMutation()
    {
        await Seed("before");
        var id = await ResourceId("before");
        var number = await AddMemoryTestProperty(PropertyType.Number);
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([MemoryTestValue(id, number.Id, 1m)]);
        await BuildIndex();
        var old = (await Projection().ReadResourcesAsync([id], 0)).Single();
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var journal = new MetadataSourceJournal(db, new MetadataSourceJournalGate());
        await journal.HeadAsync(default); // installs transactional triggers
        var oldRevision = (await journal.RevisionsAsync([id], default)).GetValueOrDefault(id);
        var nextValue = 9.25m.SerializeAsStandardValue(StandardValueType.Decimal);
        // Deliberately bypass every cache/event path: this is also the interval between a
        // successful DB commit and the application's later PublishResourceChanged call.
        await db.ResourcesV2.Where(row => row.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(row => row.Path, "/committed/AFTER").SetProperty(row => row.Tags, ResourceTag.Pinned));
        await db.CustomPropertyValues.Where(row => row.ResourceId == id && row.PropertyId == number.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Value, nextValue));
        var revision = (await journal.RevisionsAsync([id], default)).GetValueOrDefault(id);
        Assert.IsTrue(revision > oldRevision);
        var status = Index().GetStatus();
        var fresh = (await Projection().ReadResourcesAsync([id], revision)).Single();
        var json = JObject.Parse(fresh.ResourceJson);
        Assert.AreEqual("AFTER", (string)json["fileName"]!);
        Assert.IsTrue((bool)json["pinned"]!);
        Assert.IsTrue(fresh.ResourceJson.Contains("9.25", StringComparison.Ordinal));
        Assert.AreEqual("/committed/AFTER", fresh.Sort.Path);
        Assert.IsTrue(fresh.Postings.Any(p => p.Pool == PropertyPool.Custom && p.PropertyId == number.Id &&
            Equals(p.RangeValue?.ToComparable(), 9.25m)));
        Assert.IsTrue(fresh.Postings.Any(p => p.Pool == PropertyPool.Internal && p.PropertyId == (int)InternalProperty.Filename && p.ValueKey == "after"));
        Assert.IsFalse(fresh.Postings.Any(p => p.Pool == PropertyPool.Internal && p.PropertyId == (int)InternalProperty.Filename && p.ValueKey == "before"));
        Assert.AreEqual("before", (string)JObject.Parse(old.ResourceJson)["fileName"]!);
        // The normal desktop cache/index path is unchanged; exporting never installs a
        // potentially older page over a newer concurrent index update.
        Assert.AreEqual("before", (await _sp.GetRequiredService<IResourceService>().Get(id, ResourceAdditionalItem.None))!.FileName);
        Assert.AreEqual(status.Version, Index().GetStatus().Version);
        Assert.IsTrue((await IndexExporter().ReadIndexEntriesAsync([id]))[id].Any(p =>
            p.PropertyId == (int)InternalProperty.Filename && p.Pool == PropertyPool.Internal && p.ValueKey == "before"));
    }

    [TestMethod]
    public async Task PersistedMetadata_BypassesDerivedMembershipProfileAndScoreCaches()
    {
        await Seed("A", "B");
        var id = await ResourceId("A");
        var other = await ResourceId("B");
        await BuildIndex();
        _ = await Projection().ReadResourcesAsync([id], 0); // warm ordinary dependency caches too
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var library = new MediaLibraryV2DbModel { Name = "Fresh library", Paths = "[]" };
        var collection = new CollectionDbModel { Name = "Fresh collection", RuleSearchJson = "{}" };
        var profile = new ResourceProfileDbModel { Name = "Fresh template", SearchJson = "{}", NameTemplate = "fresh-display", Priority = 4 };
        var health = new HealthScoreProfileDbModel { Name = "Fresh score", Enabled = true, Priority = 2 };
        db.AddRange(library, collection, profile, health);
        await db.SaveChangesAsync();
        db.MediaLibraryResourceMappings.Add(new() { MediaLibraryId = library.Id, ResourceId = id });
        db.ResourceHealthScores.Add(new() { ResourceId = id, ProfileId = health.Id, Score = 81.25m });
        await db.SaveChangesAsync();
        var fresh = (await Projection().ReadResourcesAsync([id], 0)).Single();
        var json = JObject.Parse(fresh.ResourceJson);
        Assert.AreEqual("fresh-display", (string)json["displayName"]!);
        Assert.AreEqual("Fresh library", (string)json["mediaLibraries"]![0]!["name"]!);
        Assert.AreEqual("Fresh collection", (string)json["collections"]![0]!["name"]!);
        Assert.AreEqual(81.25m, fresh.Sort.HealthScore);
        Assert.IsTrue(fresh.Postings.Any(p => p.PropertyId == (int)InternalProperty.MediaLibraryV2Multi && p.ValueKey == library.Id.ToString()));
        Assert.IsTrue(fresh.Postings.Any(p => p.PropertyId == (int)InternalProperty.CollectionMulti && p.ValueKey == collection.Id.ToString()));
        Assert.IsTrue(fresh.Postings.Any(p => p.PropertyId == (int)InternalProperty.HealthScore && Equals(p.RangeValue?.ToComparable(), 81.25m)));
        using (ResourceMetadataReadScope.Enter([id]))
        {
            var ids = await _sp.GetRequiredService<IResourceProfileService>().GetMatchingResourceIds(profile.Id);
            CollectionAssert.AreEquivalent(new[] {id}, ids.ToArray());
            Assert.IsFalse(ids.Contains(other), "A per-page rule must never materialize or return the rest of the library.");
        }
    }

    [TestMethod]
    public async Task PersistedMetadata_FeedAcknowledgesFreshCommittedDataWhenNoNotificationWasPublished()
    {
        await Seed("before");
        var id = await ResourceId("before");
        await BuildIndex();
        var feed = Feed();
        var checkpoint = (await feed.ManifestAsync(default)).Start;
        for (var page = 0; !checkpoint.Complete && page < 100; page++)
            checkpoint = (await feed.PageAsync(new(checkpoint), default)).Next;
        Assert.IsTrue(checkpoint.Complete);
        await _sp.GetRequiredService<BakabaseDbContext>().ResourcesV2.Where(r => r.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Path, "/committed/NEW"));
        var delta = await feed.PageAsync(new(checkpoint), default);
        var row = delta.Upserts.Single(r => r.ResourceId == id);
        Assert.IsTrue(row.Revision > checkpoint.Sequence);
        Assert.AreEqual("NEW", (string)JObject.Parse(row.ResourceJson)["fileName"]!);
        Assert.IsTrue(row.Postings.Any(p => p.PropertyId == (int)InternalProperty.Filename && p.ValueKey == "new"));
        Assert.IsTrue(delta.Next.Sequence >= row.Revision);
        Assert.AreEqual(0, (await feed.PageAsync(new(delta.Next), default)).Upserts.Count,
            "The revision is acknowledged only together with its fresh JSON and index entries.");
    }

    [TestMethod]
    public async Task PersistedMetadata_CatalogReadsCommittedDefinitionsWithoutReplacingOrdinaryCachedOnes()
    {
        var property = await AddMemoryTestProperty(PropertyType.SingleLineText);
        var originalName = property.Name;
        await foreach (var _ in Projection().ReadCatalogAsync()) { }
        await _sp.GetRequiredService<BakabaseDbContext>().CustomProperties.Where(p => p.Id == property.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, "Committed catalog name"));
        var catalog = new List<ReplicaCatalog>();
        await foreach (var item in Projection().ReadCatalogAsync()) catalog.Add(item);
        var fresh = catalog.Single(item => item.Kind == "customProperties" && item.Key == property.Id.ToString());
        Assert.AreEqual("Committed catalog name", (string)JObject.Parse(fresh.Json)["name"]!);
        Assert.AreEqual(originalName, (await _sp.GetRequiredService<ICustomPropertyService>().GetAll())
            .Single(p => p.Id == property.Id).Name);
        Assert.IsFalse(PersistedReadScope.IsActive);
    }

    [TestMethod]
    public async Task PersistedMetadata_ScopeRestoresNestedAndConcurrentReadersAndRejectsDelegateSearch()
    {
        await Seed("before");
        var id = await ResourceId("before");
        var rows = _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
        await rows.GetByKey(id);
        await _sp.GetRequiredService<BakabaseDbContext>().ResourcesV2.Where(r => r.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Path, "/AFTER"));
        Assert.IsFalse(PersistedReadScope.IsActive);
        using (ResourceMetadataReadScope.Enter([id]))
        {
            Assert.AreEqual("/AFTER", (await rows.GetByKey(id))!.Path);
            Assert.AreEqual("/AFTER", (await rows.GetByKeys([id])).Single().Path);
            Assert.AreEqual("/AFTER", (await rows.GetFirstOrDefault(row => row.Id == id))!.Path);
            Assert.AreEqual("/AFTER", (await rows.GetAll(row => row.Id == id)).Single().Path);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => rows.Search(row => row.Id == id, 1, 20));
            using (ResourceMetadataReadScope.Enter([])) Assert.AreEqual(0, ResourceMetadataReadScope.ResourceIds!.Count);
            Assert.AreEqual(id, ResourceMetadataReadScope.ResourceIds!.Single());
        }
        Assert.IsFalse(PersistedReadScope.IsActive);
        Assert.IsNull(ResourceMetadataReadScope.ResourceIds);
        Assert.IsTrue((await rows.GetByKey(id))!.Path!.EndsWith("before"));
        await Task.Run(async () =>
        {
            using var fresh = ResourceMetadataReadScope.Enter([id]);
            await Task.Yield();
            Assert.IsTrue(PersistedReadScope.IsActive);
        });
        Assert.IsFalse(PersistedReadScope.IsActive);
    }
}
