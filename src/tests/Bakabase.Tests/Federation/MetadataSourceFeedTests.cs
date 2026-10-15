using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

[TestClass]
[DoNotParallelize]
public sealed class MetadataSourceFeedTests
{
    [TestMethod]
    public async Task New_tags_and_renames_during_scan_keep_resource_cursor_and_refresh_catalog_before_completion()
    {
        var provider = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton<INodeIdentityProvider>(new Identity());
            services.AddSingleton<MetadataSourceJournalGate>();
            services.AddScoped<MetadataSourceJournal>();
            services.AddScoped<MetadataReplicaProjection>();
            services.AddScoped<MetadataSourceFeed>();
        });
        try
        {
            await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<BakabaseDbContext>();
            db.ResourcesV2.AddRange(Enumerable.Range(1, 5).Select(id => new ResourceDbModel {Id = id, Path = "/resource/" + id}));
            db.CustomProperties.Add(new CustomPropertyDbModel {Id = 73, Name = "Tags", Type = PropertyType.Tags,
                CreatedAt = DateTime.Now, Options = "{\"Tags\":[]}"});
            await db.SaveChangesAsync();
            var feed = sp.GetRequiredService<MetadataSourceFeed>();
            var checkpoint = (await feed.ManifestAsync(default)).Start;
            var catalog = new Dictionary<string, string>();
            var scanned = new List<int>();
            var updated = false;
            for (var page = 0; page < 150 && !checkpoint.Complete; page++)
            {
                var batch = await feed.PageAsync(new(checkpoint, 2), default);
                Assert.AreEqual(checkpoint.Generation, batch.Next.Generation);
                foreach (var row in batch.CatalogUpserts) catalog[row.Kind + ":" + row.Key] = row.Json;
                scanned.AddRange(batch.Upserts.Select(row => row.ResourceId));
                checkpoint = batch.Next;
                if (!updated && scanned.Count == 2)
                {
                    updated = true;
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CustomProperties SET Name={"Renamed"},Options={"{\"Tags\":[{\"Value\":\"new-tag\",\"Name\":\"New tag\"}]}"} WHERE Id=73");
                }
            }
            Assert.IsTrue(updated);
            Assert.IsTrue(checkpoint.Complete);
            Assert.IsNull(checkpoint.Cursor);
            CollectionAssert.AreEqual(new[] {1,2,3,4,5}, scanned,
                "A catalog-only edit must not repeat or discard already scanned resources.");
            var property = JObject.Parse(catalog["customProperties:73"]);
            Assert.AreEqual("Renamed", (string?)property["name"]);
            StringAssert.Contains(property.ToString(), "new-tag");
            Assert.AreEqual((await feed.ManifestAsync(default)).Start.CatalogRevision, checkpoint.CatalogRevision);

            // A completed replica refreshes the same small catalog without replaying resources.
            await db.Database.ExecuteSqlRawAsync("UPDATE CustomProperties SET Name='Renamed again' WHERE Id=73");
            var refreshed = false;
            for (var page = 0; page < 100; page++)
            {
                var batch = await feed.PageAsync(new(checkpoint, 2), default);
                Assert.AreEqual(0, batch.Upserts.Count);
                Assert.IsTrue(batch.Next.Complete);
                refreshed |= batch.CatalogUpserts.Any(c => c.Kind == "customProperties" && c.Json.Contains("Renamed again"));
                checkpoint = batch.Next;
                if (checkpoint.Cursor == null) break;
            }
            Assert.IsTrue(refreshed);
            Assert.IsNull(checkpoint.Cursor);
            Assert.AreEqual((await feed.ManifestAsync(default)).Start.CatalogRevision, checkpoint.CatalogRevision);
        }
        finally { if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
    }
    private sealed class Identity : INodeIdentityProvider
    { public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NodeIdentity("owner", "epoch", "Owner")); }
}
