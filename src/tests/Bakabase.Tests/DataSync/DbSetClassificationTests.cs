using System.Reflection;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Every table is classified for data sync (spec Appendix A, §13.5): 80 DbSets (72 + the 8 data sync tables).
/// </summary>
[TestClass]
public class DbSetClassificationTests
{
    private const string Rule = "Classify it in DataSyncDbSetClassification (see .claude/rules/data-sync.md, \"Adding a DbSet\").";

    private static readonly IReadOnlyList<string> DbSets = typeof(BakabaseDbContext)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
        .Select(p => p.Name)
        .ToList();

    [TestMethod]
    public void Every_DbSet_is_classified_and_nothing_else()
    {
        var unclassified = DbSets.Where(d => DataSyncDbSetClassification.Get(d) is null).ToList();
        Assert.AreEqual(0, unclassified.Count, $"Unclassified DbSets: {string.Join(", ", unclassified)}. {Rule}");
        var stale = DataSyncDbSetClassification.All.Keys.Except(DbSets).ToList();
        Assert.AreEqual(0, stale.Count, $"Classified but not a DbSet: {string.Join(", ", stale)}.");
        Assert.AreEqual(80, DbSets.Count);
        Assert.AreEqual(80, DataSyncDbSetClassification.All.Count);
    }

    [TestMethod]
    public void The_counts_follow_Appendix_A()
    {
        var byClass = DataSyncDbSetClassification.All.Values.GroupBy(c => c.Class)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.AreEqual(2, byClass[DataSyncTableClass.Synced]);
        Assert.AreEqual(28, byClass[DataSyncTableClass.LibraryData]);
        Assert.AreEqual(50, byClass[DataSyncTableClass.NeverSync], "42 existing + 8 data sync tables");
        Assert.IsTrue(DataSyncDbSetClassification.All.Values.All(c => !string.IsNullOrWhiteSpace(c.Reason)));
        Assert.IsTrue(DataSyncDbSetClassification.All.All(e => e.Key == e.Value.DbSetName));
    }

    [TestMethod]
    public void Every_kind_is_claimed_by_exactly_one_synced_table_and_only_synced_tables_name_a_kind()
    {
        CollectionAssert.AreEquivalent(DataSyncKindIds.All.ToList(), DataSyncDbSetClassification.SyncedKinds.ToList());
        Assert.AreEqual("customProperty", DataSyncDbSetClassification.Get("CustomProperties")!.Kind);
        Assert.AreEqual("extensionGroup", DataSyncDbSetClassification.Get("ExtensionGroups")!.Kind);
        Assert.IsTrue(DataSyncDbSetClassification.All.Values
            .Where(c => c.Class != DataSyncTableClass.Synced).All(c => c.Kind is null));
    }

    [TestMethod]
    public void The_data_sync_tables_never_sync()
    {
        var own = DbSets.Where(d => d.StartsWith("DataSync", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(8, own.Count);
        foreach (var dbSet in own)
        {
            var classification = DataSyncDbSetClassification.Get(dbSet)!;
            Assert.AreEqual(DataSyncTableClass.NeverSync, classification.Class, dbSet);
            Assert.AreEqual(DataSyncDbSetClassification.SyncStateReason, classification.Reason, dbSet);
        }

        Assert.AreEqual(DataSyncTableClass.NeverSync, DataSyncDbSetClassification.Get("Passwords")!.Class);
        Assert.AreEqual(DataSyncTableClass.NeverSync, DataSyncDbSetClassification.Get("AiProviders")!.Class);
    }

    [TestMethod]
    public async Task Every_registered_kind_is_a_known_kind_that_claims_a_DbSet()
    {
        // The kind adapters register themselves next to the service that owns their table; whichever are registered
        // must be known kinds with a synced table (the registered set equals DataSyncKindIds.All once both land).
        var sp = await TestServiceBuilder.BuildServiceProvider();
        var registered = sp.GetServices<IDataSyncKind>().Select(k => k.Codec.Descriptor.Kind).ToList();

        Assert.AreEqual(registered.Count, registered.Distinct().Count(), "a kind is registered twice");
        foreach (var kind in registered)
        {
            CollectionAssert.Contains(DataSyncKindIds.All.ToList(), kind);
            CollectionAssert.Contains(DataSyncDbSetClassification.SyncedKinds.ToList(), kind);
        }
    }
}
