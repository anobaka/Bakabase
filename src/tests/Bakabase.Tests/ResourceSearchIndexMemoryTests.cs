using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Search.Index;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class ResourceSearchIndexIncrementalTests
{
    // Inspect ownership after an acknowledged update instead of relying on a GC run
    // or an unstable working-set threshold. No production diagnostic API is needed.
    private ResourceSearchIndex IndexStorage() => (ResourceSearchIndex)typeof(ResourceSearchIndexService)
        .GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(_sp.GetRequiredService<ResourceSearchIndexService>())!;

    private async Task<CustomProperty> AddMemoryTestProperty(PropertyType type)
        => await _sp.GetRequiredService<ICustomPropertyService>().Add(new CustomPropertyAddOrPutDto
        {
            Name = $"Index ownership {Guid.NewGuid():N}",
            Type = type
        });

    private static CustomPropertyValue MemoryTestValue(int resourceId, int propertyId, object value)
        => new()
        {
            ResourceId = resourceId,
            PropertyId = propertyId,
            Value = value,
            Scope = (int)PropertyValueScope.Manual
        };

    private static ResourceSearchFilter MemoryPropertyFilter(Property property,
        SearchOperation operation, object? value = null) => new()
    {
        PropertyPool = property.Pool,
        PropertyId = property.Id,
        Property = property,
        Operation = operation,
        DbValue = value
    };

    private Task<HashSet<int>?> MemorySearch(params ResourceSearchFilter[] filters)
        => Index().SearchResourceIdsAsync(new ResourceSearchFilterGroup
        {
            Combinator = SearchCombinator.And,
            Filters = filters.ToList()
        });

    [TestMethod]
    public async Task UserText_IsNotInterned_AndSharesOnlyTheLiveBucketString()
    {
        await Seed("a", "b");
        var a = await ResourceId("a");
        var b = await ResourceId("b");
        var property = await AddMemoryTestProperty(PropertyType.SingleLineText);
        var text = $"index-user-data-{Guid.NewGuid():N}";
        var separateCopy = new string(text.ToCharArray());
        Assert.AreNotSame(text, separateCopy);
        Assert.IsNull(string.IsInterned(text));
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([
            MemoryTestValue(a, property.Id, text),
            MemoryTestValue(b, property.Id, separateCopy)
        ]);
        await BuildIndex();

        var storage = IndexStorage();
        var aKey = storage.ResourceIndexKeys[a].Single(k => k.Pool == PropertyPool.Custom &&
            k.PropertyId == property.Id && k.RangeValue == null);
        var bKey = storage.ResourceIndexKeys[b].Single(k => k.Pool == PropertyPool.Custom &&
            k.PropertyId == property.Id && k.RangeValue == null);
        Assert.IsNull(string.IsInterned(text),
            "user values must not be retained by the process-wide intern pool");
        Assert.AreSame(aKey.ValueKey, bKey.ValueKey,
            "resources sharing a posting should share its live canonical key");
        var posting = storage.ValueIndex[PropertyPool.Custom][property.Id][text];
        CollectionAssert.AreEquivalent(new[] { a, b }, posting.ToArray());

        Index().RemoveResource(a);
        await WaitForPendingUpdatesAndAssertCount(1);
        Assert.AreSame(posting, storage.ValueIndex[PropertyPool.Custom][property.Id][text]);
        CollectionAssert.AreEquivalent(new[] { b }, posting.ToArray());
        CollectionAssert.AreEquivalent(new[] { b }, (await MemorySearch(
            MemoryPropertyFilter(property.ToProperty(), SearchOperation.Equals, text)))!.ToArray());

        Index().RemoveResource(b);
        await WaitForPendingUpdatesAndAssertCount(0);
        Assert.IsTrue(storage.ValueIndex.IsEmpty, "the final resource must release value buckets and their parents");
        Assert.IsTrue(storage.RangeIndex.IsEmpty, "the final resource must release range buckets and their parents");
        Assert.IsTrue(storage.ResourceIndexKeys.IsEmpty);
        Assert.AreEqual(0, storage.AllResourceIds.Count);
        Assert.IsNull(string.IsInterned(text));
    }

    [TestMethod]
    public async Task RepeatedPathPlayedAtAndNumberUpdates_DoNotRetainHistoricalKeys()
    {
        await Seed("original");
        var id = await ResourceId("original");
        var property = await AddMemoryTestProperty(PropertyType.Number);
        var values = _sp.GetRequiredService<ICustomPropertyValueService>();
        await values.AddRange([MemoryTestValue(id, property.Id, 1m)]);
        var valueId = (await values.GetAllDbModels(v => v.PropertyId == property.Id)).Single().Id;
        await BuildIndex();
        var storage = IndexStorage();
        var resources = _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>();

        for (var i = 0; i < 12; i++)
        {
            var path = Path.Combine(_testRoot, $"updated-{i:D2}-{Guid.NewGuid():N}");
            var playedAt = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc).AddSeconds(i);
            var number = (decimal)i + 2;
            await resources.UpdateByKey(id, row =>
            {
                row.Path = path;
                row.PlayedAt = playedAt;
            });
            var value = MemoryTestValue(id, property.Id, number);
            value.Id = valueId;
            await values.UpdateRange([value]);
            Index().InvalidateResource(id);
            await WaitForPendingUpdatesAndAssertCount(1);

            var paths = storage.ValueIndex[PropertyPool.Internal][(int)InternalProperty.RootPath];
            var filenames = storage.ValueIndex[PropertyPool.Internal][(int)InternalProperty.Filename];
            Assert.AreEqual(1, paths.Count, "old path postings must be removed");
            Assert.IsTrue(paths.ContainsKey(path.Trim().ToLowerInvariant()));
            Assert.AreEqual(1, filenames.Count, "old filename postings must be removed");
            var playedTimes = storage.RangeIndex[PropertyPool.Internal][(int)InternalProperty.PlayedAt];
            Assert.AreEqual(1, playedTimes.Count, "each play must replace the previous time bucket");
            Assert.AreEqual(playedAt, playedTimes.Keys.Single());
            Assert.AreEqual(1, storage.ValueIndex[PropertyPool.Custom][property.Id].Count);
            Assert.AreEqual(1, storage.RangeIndex[PropertyPool.Custom][property.Id].Count);
            Assert.AreEqual(number, storage.RangeIndex[PropertyPool.Custom][property.Id].Keys.Single());
            CollectionAssert.AreEquivalent(new[] { id }, (await MemorySearch(
                MemoryPropertyFilter(property.ToProperty(), SearchOperation.Equals, number)))!.ToArray());
        }

        Index().RemoveResource(id);
        await WaitForPendingUpdatesAndAssertCount(0);
        Assert.IsTrue(storage.ValueIndex.IsEmpty);
        Assert.IsTrue(storage.RangeIndex.IsEmpty);
    }

    [DataTestMethod]
    [DataRow(PropertyType.Number)]
    [DataRow(PropertyType.Date)]
    [DataRow(PropertyType.DateTime)]
    [DataRow(PropertyType.Time)]
    public async Task TypedRangeRemoval_PreservesOtherResourcesSharingTheValue(PropertyType type)
    {
        await Seed("a", "b");
        var a = await ResourceId("a");
        var b = await ResourceId("b");
        var property = await AddMemoryTestProperty(type);
        object value = type switch
        {
            PropertyType.Number => 1.25m,
            PropertyType.Date => new DateTime(2026, 10, 13),
            PropertyType.DateTime => new DateTime(2026, 10, 13, 12, 34, 56),
            PropertyType.Time => new TimeSpan(12, 34, 56),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([
            MemoryTestValue(a, property.Id, value),
            MemoryTestValue(b, property.Id, value)
        ]);
        await BuildIndex();
        var storage = IndexStorage();
        var ranges = storage.RangeIndex[PropertyPool.Custom][property.Id];
        Assert.AreEqual(1, ranges.Count);

        Index().RemoveResource(a);
        await WaitForPendingUpdatesAndAssertCount(1);
        Assert.AreEqual(1, ranges.Count);
        CollectionAssert.AreEquivalent(new[] { b }, ranges.Values.Single().ToArray());
        CollectionAssert.AreEquivalent(new[] { b }, (await MemorySearch(
            MemoryPropertyFilter(property.ToProperty(), SearchOperation.Equals, value)))!.ToArray());

        Index().RemoveResource(b);
        await WaitForPendingUpdatesAndAssertCount(0);
        Assert.IsFalse(storage.RangeIndex.ContainsKey(PropertyPool.Custom));
        Assert.IsFalse(storage.ValueIndex.ContainsKey(PropertyPool.Custom));
    }

    [TestMethod]
    public async Task DeletingLastNumericValue_DoesNotTurnAnAndComparisonIntoAnUnconstrainedFilter()
    {
        await Seed("apple", "holder");
        var apple = await ResourceId("apple");
        var holder = await ResourceId("holder");
        var number = await AddMemoryTestProperty(PropertyType.Number);
        var text = await AddMemoryTestProperty(PropertyType.SingleLineText);
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([
            MemoryTestValue(holder, number.Id, 10m),
            MemoryTestValue(holder, text.Id, "held-value")
        ]);
        await BuildIndex();
        Assert.AreEqual(0, (await MemorySearch(
            MemoryPropertyFilter(_filenameProperty, SearchOperation.Equals, "apple"),
            MemoryPropertyFilter(number.ToProperty(), SearchOperation.GreaterThan, 0m)))!.Count);

        await _sp.GetRequiredService<IResourceService>().DeleteByKeys([holder]);
        await WaitForPendingUpdatesAndAssertCount(1);
        Assert.IsFalse(IndexStorage().RangeIndex.ContainsKey(PropertyPool.Custom));
        Assert.IsFalse(IndexStorage().ValueIndex.ContainsKey(PropertyPool.Custom));

        async Task AssertMissingValueSearches()
        {
            var andComparison = await MemorySearch(
                MemoryPropertyFilter(_filenameProperty, SearchOperation.Equals, "apple"),
                MemoryPropertyFilter(number.ToProperty(), SearchOperation.GreaterThan, 0m));
            Assert.IsNotNull(andComparison, "an empty numeric index is data, not an unsupported query");
            Assert.AreEqual(0, andComparison.Count,
                "the remaining filename match has no numeric value and must not pass the AND comparison");
            Assert.AreEqual(0, (await MemorySearch(
                MemoryPropertyFilter(number.ToProperty(), SearchOperation.NotEquals, 0m)))!.Count,
                "missing range values must retain the established range-query semantics");
            CollectionAssert.AreEquivalent(new[] { apple }, (await MemorySearch(
                MemoryPropertyFilter(text.ToProperty(), SearchOperation.NotEquals, "held-value")))!.ToArray());
            CollectionAssert.AreEquivalent(new[] { apple }, (await MemorySearch(
                MemoryPropertyFilter(number.ToProperty(), SearchOperation.IsNull)))!.ToArray());
            Assert.AreEqual(0, (await MemorySearch(
                MemoryPropertyFilter(number.ToProperty(), SearchOperation.IsNotNull)))!.Count);
        }

        await AssertMissingValueSearches();
        await BuildIndex();
        await AssertMissingValueSearches();
    }
}
