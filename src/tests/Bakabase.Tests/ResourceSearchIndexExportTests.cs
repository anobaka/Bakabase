using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class ResourceSearchIndexIncrementalTests
{
    private IResourceSearchIndexExportService IndexExporter() => (IResourceSearchIndexExportService)Index();

    [TestMethod]
    public async Task ExportIndex_PostingsPreserveNormalizationAndTypedRanges()
    {
        await Seed("MiXeD");
        var id = await ResourceId("MiXeD");
        var number = await AddMemoryTestProperty(PropertyType.Number);
        var time = await AddMemoryTestProperty(PropertyType.Time);
        var value = new TimeSpan(0, 2, 3);
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([
            MemoryTestValue(id, number.Id, 1.25m), MemoryTestValue(id, time.Id, value)
        ]);
        await BuildIndex();

        var exported = await IndexExporter().ReadIndexEntriesAsync([id]);
        var entries = exported[id];
        Assert.IsTrue(entries.Any(entry => entry.Pool == PropertyPool.Internal &&
            entry.PropertyId == (int)InternalProperty.Filename && entry.ValueKey == "mixed"));
        Assert.IsTrue(entries.Any(entry => entry.Pool == PropertyPool.Custom &&
            entry.PropertyId == number.Id && Equals(entry.RangeValue, 1.25m)));
        Assert.IsTrue(entries.Any(entry => entry.Pool == PropertyPool.Custom &&
            entry.PropertyId == time.Id && Equals(entry.RangeValue, value)));
        Assert.AreEqual(IndexStorage().ResourceIndexKeys[id].Count, entries.Count);
    }

    [TestMethod]
    public async Task ExportIndex_WaitsForQueuedUpdatesAndReturnsAnIndependentSnapshot()
    {
        await Seed("before");
        var id = await ResourceId("before");
        await BuildIndex();
        var old = await IndexExporter().ReadIndexEntriesAsync([id]);
        var rows = _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();
        await rows.UpdateByKey(id, resource => resource.Path = "/changed/AFTER");
        Index().InvalidateResource(id);

        var fresh = await IndexExporter().ReadIndexEntriesAsync([id, id, int.MaxValue]);
        Assert.AreEqual(1, fresh.Count);
        Assert.IsTrue(fresh[id].Any(entry => entry.PropertyId == (int)InternalProperty.Filename &&
            entry.Pool == PropertyPool.Internal && entry.ValueKey == "after"));
        Assert.IsTrue(old[id].Any(entry => entry.PropertyId == (int)InternalProperty.Filename &&
            entry.Pool == PropertyPool.Internal && entry.ValueKey == "before"));
        Index().RemoveResource(id);
        Assert.AreEqual(0, (await IndexExporter().ReadIndexEntriesAsync([id])).Count);
        Assert.IsTrue(old[id].Count > 0);
    }

    [TestMethod]
    public async Task ExportIndex_RejectsUnreadyInvalidAndCancelledReads()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            IndexExporter().ReadIndexEntriesAsync([1]));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            IndexExporter().ReadIndexEntriesAsync(Enumerable.Range(1, 101).ToArray()));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            IndexExporter().ReadIndexEntriesAsync([0]));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            IndexExporter().ReadIndexEntriesAsync([1], new CancellationToken(true)));
    }

    [TestMethod]
    public async Task ExportIndex_RejectsOversizedValuesWithoutTruncatingOrDamagingTheIndex()
    {
        await Seed("large");
        var id = await ResourceId("large");
        var text = await AddMemoryTestProperty(PropertyType.SingleLineText);
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([
            MemoryTestValue(id, text.Id, new string('a', IResourceSearchIndexExportService.MaximumEncodedBytes / 6))
        ]);
        await BuildIndex();

        await Assert.ThrowsExceptionAsync<ResourceSearchIndexExportBudgetException>(() =>
            IndexExporter().ReadIndexEntriesAsync([id]));
        Assert.IsTrue(Index().IsReady);
        Assert.AreEqual(1, Index().GetStatus().TotalResourceCount);
    }
}
