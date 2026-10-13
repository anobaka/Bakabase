using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Search.Index;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for the inverted index's incremental update path —
/// RemoveResource(s) / InvalidateResource(s) and the batched background processor.
/// Earlier tests only exercised a full RebuildAllAsync.
/// </summary>
[TestClass]
public sealed partial class ResourceSearchIndexIncrementalTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;
    private Property _filenameProperty = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"IndexIncrementalTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private async Task Seed(params string[] names)
    {
        foreach (var n in names)
        {
            Directory.CreateDirectory(Path.Combine(_testRoot, n));
        }

        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);

        var internalProps = await _sp.GetRequiredService<IPropertyService>().GetProperties(PropertyPool.Internal);
        _filenameProperty = internalProps.First(p => p.Id == (int)InternalProperty.Filename);
    }

    private IResourceSearchIndexService Index() => _sp.GetRequiredService<IResourceSearchIndexService>();

    private async Task BuildIndex()
    {
        var idx = Index();
        await idx.RebuildAllAsync(CancellationToken.None);
        await idx.WaitForReadyAsync(TimeSpan.FromSeconds(10));
        await idx.WaitForPendingUpdatesAsync(CancellationToken.None);
    }

    private async Task WaitForPendingUpdatesAndAssertCount(int expected)
    {
        await Index().WaitForPendingUpdatesAsync(CancellationToken.None);
        Assert.AreEqual(expected, Index().GetStatus().TotalResourceCount);
    }

    private async Task<int> ResourceId(string filename)
        => (await _sp.GetRequiredService<IResourceService>().GetAll()).Single(r => r.FileName == filename).Id;

    private async Task<int> SearchCountByFilename(string value)
    {
        var resp = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch
        {
            PageSize = 100,
            Group = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And,
                Filters =
                [
                    new ResourceSearchFilter
                    {
                        PropertyPool = PropertyPool.Internal,
                        PropertyId = (int)InternalProperty.Filename,
                        Operation = SearchOperation.Equals,
                        DbValue = value,
                        Property = _filenameProperty
                    }
                ]
            }
        });
        return resp.Data!.Count;
    }

    private async Task<HashSet<int>?> SearchIdsByFilename(
        IResourceSearchIndexService index,
        string value)
    {
        return await index.SearchResourceIdsAsync(new ResourceSearchFilterGroup
        {
            Combinator = SearchCombinator.And,
            Filters =
            [
                new ResourceSearchFilter
                {
                    PropertyPool = PropertyPool.Internal,
                    PropertyId = (int)InternalProperty.Filename,
                    Operation = SearchOperation.Equals,
                    DbValue = value,
                    Property = _filenameProperty
                }
            ]
        });
    }

    [TestMethod]
    public async Task RemoveResource_RemovesFromIndex()
    {
        await Seed("a", "b", "c");
        await BuildIndex();
        Assert.AreEqual(3, Index().GetStatus().TotalResourceCount);

        Index().RemoveResource(await ResourceId("a"));
        await WaitForPendingUpdatesAndAssertCount(2);
    }

    [TestMethod]
    public async Task RemoveResource_RemovedResourceDropsOutOfSearch()
    {
        await Seed("apple", "banana");
        await BuildIndex();
        Assert.AreEqual(1, await SearchCountByFilename("apple"));

        Index().RemoveResource(await ResourceId("apple"));
        await WaitForPendingUpdatesAndAssertCount(1);

        Assert.AreEqual(0, await SearchCountByFilename("apple"));
    }

    [TestMethod]
    public async Task RemoveResources_BatchRemovesEveryGivenResource()
    {
        await Seed("a", "b", "c");
        await BuildIndex();

        Index().RemoveResources([await ResourceId("a"), await ResourceId("b")]);
        await WaitForPendingUpdatesAndAssertCount(1);
    }

    [TestMethod]
    public async Task RemoveResource_LeavesOtherResourcesSearchable()
    {
        await Seed("apple", "banana");
        await BuildIndex();

        Index().RemoveResource(await ResourceId("apple"));
        await WaitForPendingUpdatesAndAssertCount(1);

        Assert.AreEqual(1, await SearchCountByFilename("banana"));
    }

    [TestMethod]
    public async Task InvalidateResource_ReindexesResourceFromDatabase()
    {
        await Seed("a", "b");
        await BuildIndex();
        var id = await ResourceId("a");

        Index().RemoveResources([id, id]);
        await WaitForPendingUpdatesAndAssertCount(1);

        // The resource still exists in the database; invalidation must re-read and re-index it.
        Index().InvalidateResources([id, id]);
        await WaitForPendingUpdatesAndAssertCount(2);
    }

    [TestMethod]
    public async Task IndexVersion_AdvancesOnIncrementalUpdate()
    {
        await Seed("a", "b");
        await BuildIndex();
        var versionBefore = Index().Version;

        Index().RemoveResource(await ResourceId("a"));
        await WaitForPendingUpdatesAndAssertCount(1);

        Assert.IsTrue(Index().Version > versionBefore);
    }

    [TestMethod]
    public async Task WaitForPendingUpdates_CoalescedUpdateAndRemove_RemoveWins()
    {
        await Seed("a", "b");
        await BuildIndex();
        var id = await ResourceId("a");

        // Repeated and adjacent notifications for one resource collapse into one logical
        // operation. Remove wins within the barrier-delimited segment so an update cannot
        // resurrect an entry deleted by the same logical change.
        for (var i = 0; i < 100; i++)
        {
            Index().InvalidateResource(id);
        }
        Index().RemoveResources([id, id]);

        await WaitForPendingUpdatesAndAssertCount(1);
        Assert.AreEqual(0, await SearchCountByFilename("a"));

        // A new segment after the barrier is allowed to re-read an extant database row.
        Index().InvalidateResources([id, id]);
        await WaitForPendingUpdatesAndAssertCount(2);
        Assert.AreEqual(1, await SearchCountByFilename("a"));
    }

    [TestMethod]
    public async Task BulkInvalidations_DeduplicateAcrossAdjacentQueueItems()
    {
        await Seed("a", "b");
        var scopeFactory = new CountingScopeFactory(_sp.GetRequiredService<IServiceScopeFactory>());
        var index = new ResourceSearchIndexService(
            scopeFactory,
            _sp.GetRequiredService<IResourceDataChangeEvent>(),
            _sp.GetRequiredService<ILogger<ResourceSearchIndexService>>(),
            _sp.GetRequiredService<IBakabaseLocalizer>());

        await index.RebuildAllAsync(CancellationToken.None);
        scopeFactory.Reset();
        var versionBefore = index.Version;
        var a = await ResourceId("a");
        var b = await ResourceId("b");

        // More than the former 100-operation batch size, spread across adjacent bulk
        // notifications and containing duplicates both within and across calls.
        for (var i = 0; i < 150; i++)
        {
            index.InvalidateResources([a, a, b]);
            index.InvalidateResources([b, a]);
        }
        index.InvalidateResource(a);
        await index.WaitForPendingUpdatesAsync(CancellationToken.None);

        Assert.AreEqual(1, scopeFactory.ScopeCount,
            "adjacent bulk and single notifications should share one data-loading pass");
        Assert.AreEqual(versionBefore + 1, index.Version,
            "one coalesced batch should advance the index version once");
        CollectionAssert.AreEquivalent(new[] {a}, (await SearchIdsByFilename(index, "a"))!.ToArray());
        CollectionAssert.AreEquivalent(new[] {b}, (await SearchIdsByFilename(index, "b"))!.ToArray());
    }

    [TestMethod]
    public async Task WaitForPendingUpdates_WaitsAcrossCoalescedBatchBoundary()
    {
        var scopeFactory = new CountingScopeFactory(_sp.GetRequiredService<IServiceScopeFactory>());
        var index = new ResourceSearchIndexService(
            scopeFactory,
            _sp.GetRequiredService<IResourceDataChangeEvent>(),
            _sp.GetRequiredService<ILogger<ResourceSearchIndexService>>(),
            _sp.GetRequiredService<IBakabaseLocalizer>());

        await index.RebuildAllAsync(CancellationToken.None);
        scopeFactory.Reset();
        var versionBefore = index.Version;

        // Two adjacent notifications exceed the 4096-distinct-resource processing bound.
        // The second item is therefore split by the reader, and the FIFO barrier must wait
        // for both resulting batches rather than acknowledging only the first prefix.
        index.InvalidateResources(Enumerable.Range(10_000, 3_000));
        index.InvalidateResources(Enumerable.Range(13_000, 3_000));

        await index.WaitForPendingUpdatesAsync(CancellationToken.None);

        Assert.AreEqual(2, scopeFactory.ScopeCount,
            "the bounded update should require exactly two data-loading passes");
        Assert.AreEqual(versionBefore + 2, index.Version,
            "both bounded batches must be committed before the barrier completes");
        Assert.AreEqual(0, index.GetStatus().PendingUpdateCount);
    }

    [TestMethod]
    public async Task Barrier_DoesNotIncludeFailureFromFollowingBatch()
    {
        await Seed("a");
        var scopeFactory = new FailNextScopeFactory(_sp.GetRequiredService<IServiceScopeFactory>());
        var index = new ResourceSearchIndexService(
            scopeFactory,
            _sp.GetRequiredService<IResourceDataChangeEvent>(),
            _sp.GetRequiredService<ILogger<ResourceSearchIndexService>>(),
            _sp.GetRequiredService<IBakabaseLocalizer>());

        await index.RebuildAllAsync(CancellationToken.None);
        var id = await ResourceId("a");

        index.RemoveResource(id);
        var firstBarrier = index.WaitForPendingUpdatesAsync(CancellationToken.None);

        // This permanently failing update is queued after the first barrier. It must not
        // be merged into the preceding removal or retroactively fault that barrier.
        scopeFactory.FailNext(3);
        index.InvalidateResource(id);

        await firstBarrier;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => index.WaitForPendingUpdatesAsync(CancellationToken.None));
        Assert.IsFalse(index.IsReady);
    }

    [TestMethod]
    public async Task IncrementalBatch_RetriesThenFallsBackUntilSuccessfulRebuild()
    {
        await Seed("a");
        var scopeFactory = new FailNextScopeFactory(_sp.GetRequiredService<IServiceScopeFactory>());
        var index = new ResourceSearchIndexService(
            scopeFactory,
            _sp.GetRequiredService<IResourceDataChangeEvent>(),
            _sp.GetRequiredService<ILogger<ResourceSearchIndexService>>(),
            _sp.GetRequiredService<IBakabaseLocalizer>());

        await index.RebuildAllAsync(CancellationToken.None);
        var versionBeforeRetry = index.Version;
        scopeFactory.FailNext(2);

        index.InvalidateResource(await ResourceId("a"));
        await index.WaitForPendingUpdatesAsync(CancellationToken.None);

        Assert.IsTrue(index.IsReady);
        Assert.AreEqual(versionBeforeRetry + 1, index.Version,
            "two failed attempts followed by a successful retry must advance the version once");

        var versionBeforeFailure = index.Version;
        scopeFactory.FailNext(3);

        index.InvalidateResource(await ResourceId("a"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => index.WaitForPendingUpdatesAsync(CancellationToken.None));

        Assert.IsFalse(index.IsReady);
        Assert.AreEqual(versionBeforeFailure, index.Version,
            "a failed batch must not advance the observable index version");

        var filter = new ResourceSearchFilterGroup
        {
            Combinator = SearchCombinator.And,
            Filters =
            [
                new ResourceSearchFilter
                {
                    PropertyPool = PropertyPool.Internal,
                    PropertyId = (int)InternalProperty.Filename,
                    Operation = SearchOperation.Equals,
                    DbValue = "a",
                    Property = _filenameProperty
                }
            ]
        };
        Assert.IsNull(await index.SearchResourceIdsAsync(filter),
            "an index with an unrecovered batch failure must force the caller to full-scan");

        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(index.IsReady);
        CollectionAssert.AreEqual(
            new[] {await ResourceId("a")},
            (await index.SearchResourceIdsAsync(filter))!.OrderBy(x => x).ToArray());
    }

    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _scopeCount;

        public int ScopeCount => Volatile.Read(ref _scopeCount);

        public void Reset() => Volatile.Write(ref _scopeCount, 0);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _scopeCount);
            return inner.CreateScope();
        }
    }

    private sealed class FailNextScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _remainingFailures;

        public void FailNext(int count) => Volatile.Write(ref _remainingFailures, count);

        public IServiceScope CreateScope()
        {
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new InvalidOperationException("Injected scope creation failure");
            }

            return inner.CreateScope();
        }
    }
}
