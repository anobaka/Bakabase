using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class MetadataSourceJournalTests
{
    private string _file = null!;
    private BakabaseDbContext _db = null!;
    private MetadataSourceJournal _journal = null!;
    [TestInitialize]
    public async Task Initialize()
    {
        _file = Path.Combine(Path.GetTempPath(), "bakabase-metadata-journal-" + Guid.NewGuid().ToString("N") + ".sqlite");
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + _file + ";Pooling=False").Options);
        await _db.Database.EnsureCreatedAsync();
        _journal = new(_db, new MetadataSourceJournalGate());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        foreach (var suffix in new[] {"", "-wal", "-shm"}) File.Delete(_file + suffix);
    }

    [TestMethod]
    public async Task Revision_lookup_filters_requested_ids_and_reads_committed_changes()
    {
        await _journal.EnsureAsync(default);
        _db.ResourcesV2.AddRange(new[] {7, 11, 99}.Select(id => new ResourceDbModel {Id = id, Path = "/" + id}));
        await _db.SaveChangesAsync();

        var revisions = await _journal.RevisionsAsync([99, 7, 404], default);
        CollectionAssert.AreEquivalent(new[] {7, 99}, revisions.Keys.ToArray());
        Assert.IsTrue(revisions.Values.All(revision => revision > 0));
        Assert.AreEqual(0, (await _journal.RevisionsAsync([], default)).Count);

        await _db.Database.ExecuteSqlRawAsync("UPDATE ResourcesV2 SET Path='/changed' WHERE Id=7");
        var updated = await _journal.RevisionsAsync([7, 99], default);
        Assert.IsTrue(updated[7] > revisions[7]);
        Assert.AreEqual(revisions[99], updated[99]);
    }

    [TestMethod]
    public async Task Lazy_activation_records_committed_mutations_and_rolls_back_with_their_transaction()
    {
        _db.ResourcesV2.Add(new ResourceDbModel {Id = 1, Path = "/original"});
        await _db.SaveChangesAsync();
        Assert.AreEqual(0, await _db.MetadataSourceChanges.CountAsync());
        var initial = await _journal.HeadAsync(default);
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.Database.ExecuteSqlRawAsync("UPDATE ResourcesV2 SET Path='/rolled-back' WHERE Id=1");
            Assert.IsTrue((await _journal.HeadAsync(default)).Revision > initial.Revision);
            await transaction.RollbackAsync();
        }
        Assert.AreEqual(initial, await _journal.HeadAsync(default));
        Assert.AreEqual("/original", await _db.ResourcesV2.AsNoTracking().Select(r => r.Path).SingleAsync());
        await _db.Database.ExecuteSqlRawAsync("UPDATE ResourcesV2 SET Path='/committed' WHERE Id=1");
        Assert.AreEqual(1, await _db.MetadataSourceChanges.CountAsync());
        Assert.IsTrue((await _journal.HeadAsync(default)).Revision > initial.Revision);
    }

    [TestMethod]
    public async Task Deletions_remain_discoverable_after_restart_without_a_process_event()
    {
        await _journal.EnsureAsync(default);
        _db.ResourcesV2.Add(new ResourceDbModel {Id = 1, Path = "/resource"});
        await _db.SaveChangesAsync();
        var before = await _journal.HeadAsync(default);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM ResourcesV2 WHERE Id=1");
        var restarted = new MetadataSourceJournal(_db, new MetadataSourceJournalGate());
        var head = await restarted.HeadAsync(default);
        Assert.AreEqual(before.Generation, head.Generation);
        var changes = await restarted.ChangesAsync(before.Revision, head.Revision, 100, default);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(1, changes[0].ResourceId);
        CollectionAssert.AreEqual(Array.Empty<int>(), await restarted.ResourceIdsAsync(0, 100, default));
    }

    [TestMethod]
    public async Task Repeated_edits_coalesce_and_keyset_pages_do_not_lose_updates_at_a_boundary()
    {
        await _journal.EnsureAsync(default);
        for (var id = 1; id <= 101; id++) _db.ResourcesV2.Add(new ResourceDbModel {Id = id});
        await _db.SaveChangesAsync();
        for (var edit = 0; edit < 200; edit++)
            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ResourcesV2 SET Path={edit.ToString()} WHERE Id=1");
        Assert.AreEqual(101, await _db.MetadataSourceChanges.CountAsync());
        var head = await _journal.HeadAsync(default);
        var first = await _journal.ChangesAsync(0, head.Revision, 100, default);
        var second = await _journal.ChangesAsync(first[^1].Revision, head.Revision, 100, default);
        Assert.AreEqual(101, first.Concat(second).Select(x => x.ResourceId).Distinct().Count());
        Assert.AreEqual(101, first.Concat(second).Select(x => x.Revision).Distinct().Count());
    }

    [TestMethod]
    public async Task Definition_edits_invalidate_generation_but_resource_edits_do_not()
    {
        var first = await _journal.HeadAsync(default);
        _db.ResourcesV2.Add(new ResourceDbModel {Id = 1});
        await _db.SaveChangesAsync();
        Assert.AreEqual(first.Generation, (await _journal.HeadAsync(default)).Generation);
        _db.MediaLibrariesV2.Add(new MediaLibraryV2DbModel {Name = "Library", Paths = "[]"});
        await _db.SaveChangesAsync();
        Assert.AreNotEqual(first.Generation, (await _journal.HeadAsync(default)).Generation);
    }
    [TestMethod]
    public async Task Additive_reference_options_and_property_names_refresh_catalog_without_invalidating_resources()
    {
        await _journal.EnsureAsync(default);
        await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CustomProperties (Id,Name,Type,CreatedAt,\"Order\",Options) VALUES(1,'Tags',16,'2026-01-01',0,{"{\"Tags\":[]}"})");
        var initial = await _journal.HeadAsync(default);
        await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CustomProperties SET Options={"{\"Tags\":[{\"Value\":\"a\",\"Name\":\"First\"}]}"} WHERE Id=1");
        var added = await _journal.HeadAsync(default);
        Assert.AreEqual(initial.Generation, added.Generation);
        Assert.IsTrue(added.CatalogRevision > initial.CatalogRevision);
        await _db.Database.ExecuteSqlRawAsync("UPDATE CustomProperties SET Name='New label' WHERE Id=1");
        var renamed = await _journal.HeadAsync(default);
        Assert.AreEqual(initial.Generation, renamed.Generation);
        Assert.IsTrue(renamed.CatalogRevision > added.CatalogRevision);
        await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CustomProperties SET Options={"{\"Tags\":[]}"} WHERE Id=1");
        Assert.AreNotEqual(initial.Generation, (await _journal.HeadAsync(default)).Generation,
            "Removing an existing option can invalidate indexed value semantics.");
        var beforeType = await _journal.HeadAsync(default);
        await _db.Database.ExecuteSqlRawAsync("UPDATE CustomProperties SET Type=4 WHERE Id=1");
        Assert.AreNotEqual(beforeType.Generation, (await _journal.HeadAsync(default)).Generation);
        var beforeDelete = await _journal.HeadAsync(default);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM CustomProperties WHERE Id=1");
        Assert.AreNotEqual(beforeDelete.Generation, (await _journal.HeadAsync(default)).Generation);
    }

    [TestMethod]
    public async Task Property_rename_used_by_a_name_template_rebuilds_derived_resource_metadata()
    {
        await _journal.EnsureAsync(default);
        await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CustomProperties (Id,Name,Type,CreatedAt,\"Order\",Options) VALUES(1,'Tags',16,'2026-01-01',0,{"{\"Tags\":[]}"})");
        _db.ResourceProfiles.Add(new ResourceProfileDbModel {Name = "Named", NameTemplate = "{Tags}",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now});
        await _db.SaveChangesAsync();
        var before = await _journal.HeadAsync(default);
        await _db.Database.ExecuteSqlRawAsync("UPDATE CustomProperties SET Name='Tags renamed' WHERE Id=1");
        Assert.AreNotEqual(before.Generation, (await _journal.HeadAsync(default)).Generation);
        before = await _journal.HeadAsync(default);
        await _db.Database.ExecuteSqlRawAsync("UPDATE CustomProperties SET Name='Tags' WHERE Id=1");
        Assert.AreNotEqual(before.Generation, (await _journal.HeadAsync(default)).Generation,
            "Restoring a formerly missing placeholder also changes derived display names.");
    }

}
