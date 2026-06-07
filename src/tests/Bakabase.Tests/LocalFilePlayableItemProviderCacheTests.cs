using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using LocalFilePlayableItemProvider =
    Bakabase.InsideWorld.Business.Components.Providers.PlayableItem.LocalFilePlayableItemProvider;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for the save/load (cache) side of playable-file discovery via
/// <see cref="LocalFilePlayableItemProvider"/>: a discovered result is persisted to the
/// resource cache and reused without rescanning, an empty result is a valid cached
/// result (regression for the cache-flag-but-null-paths rescan bug), InvalidateAsync
/// clears the cache, and GetStatus reflects cache state.
/// </summary>
[TestClass]
public sealed class LocalFilePlayableItemProviderCacheTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PlayableCacheTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    /// <summary>Creates one layer-1 directory resource with the given files inside it.</summary>
    private async Task<Resource> SeedResource(string dirName, params string[] files)
    {
        var dir = Path.Combine(_testRoot, dirName);
        Directory.CreateDirectory(dir);
        foreach (var f in files)
        {
            File.WriteAllBytes(Path.Combine(dir, f), Array.Empty<byte>());
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

        return (await _sp.GetRequiredService<IResourceService>().GetAll()).Single();
    }

    private async Task AddPlayableProfile(params string[] extensions)
        => await _sp.GetRequiredService<IResourceProfileService>().Add(
            "playable", "{}", null, null,
            new ResourceProfilePlayableFileOptions { Extensions = extensions.ToList() },
            null, null, 100);

    private LocalFilePlayableItemProvider Provider()
        => _sp.GetServices<IPlayableItemProvider>().OfType<LocalFilePlayableItemProvider>().Single();

    private Task<ResourceCacheDbModel?> GetCacheRow(int resourceId)
        => _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>()
            .GetByKey(resourceId, true);

    [TestMethod]
    public async Task CacheHit_ReturnsCachedPaths_WithoutRescanning()
    {
        // Directory is empty; a rescan would find nothing. The cached path proves the
        // cache branch was taken.
        var resource = await SeedResource("Movie");
        resource.Cache = new ResourceFileSystemCache
        {
            CachedTypes = [ResourceCacheType.PlayableFiles],
            PlayableFilePaths = ["/elsewhere/cached.mp4"]
        };

        var result = await Provider().GetPlayableItemsAsync(resource, CancellationToken.None);

        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual("/elsewhere/cached.mp4", result.Items[0].Key);
    }

    [TestMethod]
    public async Task CacheHit_EmptyCachedList_ReturnsEmpty_WithoutRescanning()
    {
        // Regression: the cache row has the PlayableFiles flag but null paths (the form a
        // zero-playable-file result is persisted as). That is a valid cached "empty" and
        // must NOT trigger a rescan — even though the directory now contains an mp4 and a
        // matching profile exists.
        var resource = await SeedResource("Movie", "a.mp4");
        await AddPlayableProfile("mp4");
        resource.Cache = new ResourceFileSystemCache
        {
            CachedTypes = [ResourceCacheType.PlayableFiles],
            PlayableFilePaths = null
        };

        var result = await Provider().GetPlayableItemsAsync(resource, CancellationToken.None);

        Assert.AreEqual(0, result.Items.Count);
    }

    [TestMethod]
    public async Task NoCache_DiscoversFromFilesystem()
    {
        var resource = await SeedResource("Movie", "a.mp4", "b.mp4", "c.txt");
        await AddPlayableProfile("mp4");

        var result = await Provider().GetPlayableItemsAsync(resource, CancellationToken.None);

        Assert.AreEqual(2, result.Items.Count);
    }

    [TestMethod]
    public async Task Discovery_PersistsResultToCache()
    {
        var resource = await SeedResource("Movie", "a.mp4", "b.mp4");
        await AddPlayableProfile("mp4");

        await Provider().GetPlayableItemsAsync(resource, CancellationToken.None);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsNotNull(cache);
        Assert.IsTrue(cache!.CachedTypes.HasFlag(ResourceCacheType.PlayableFiles));
        Assert.IsFalse(string.IsNullOrEmpty(cache.PlayableFilePaths));
    }

    [TestMethod]
    public async Task Discovery_EmptyResult_StillPersistsCacheFlag()
    {
        // No profile -> nothing is playable. The cache flag must still be set (so the next
        // call is a cache hit) with null paths.
        var resource = await SeedResource("Movie", "a.mp4");

        var result = await Provider().GetPlayableItemsAsync(resource, CancellationToken.None);
        Assert.AreEqual(0, result.Items.Count);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsNotNull(cache);
        Assert.IsTrue(cache!.CachedTypes.HasFlag(ResourceCacheType.PlayableFiles));
        Assert.IsTrue(string.IsNullOrEmpty(cache.PlayableFilePaths));
    }

    [TestMethod]
    public async Task RefreshResourceCache_RediscoversPlayableFiles_OverStaleEmptyCache()
    {
        // Regression: a resource was cached as "no playable files" (flag set, paths null) — e.g.
        // discovered before it had been indexed against a playable profile. The file exists and a
        // matching profile now exists. "Refresh cache" must re-discover it; previously it reloaded
        // the stale cache via the nested Get and the provider short-circuited, re-saving empty.
        var resource = await SeedResource("Movie", "a.mp4");
        await AddPlayableProfile("mp4");

        var cacheOrm = _sp
            .GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>();
        await cacheOrm.Add(new ResourceCacheDbModel
        {
            ResourceId = resource.Id,
            CachedTypes = ResourceCacheType.PlayableFiles,
            PlayableFilePaths = null
        });

        await _sp.GetRequiredService<IResourceService>().RefreshResourceCache(resource.Id, CancellationToken.None);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsNotNull(cache);
        Assert.IsTrue(cache!.CachedTypes.HasFlag(ResourceCacheType.PlayableFiles));
        Assert.IsFalse(string.IsNullOrEmpty(cache.PlayableFilePaths),
            "RefreshResourceCache should re-discover the playable file instead of keeping the stale empty cache");
    }

    [TestMethod]
    public async Task RefreshResourcesCache_RefreshesEverySelectedResource_AndReportsProgress()
    {
        // Two freshly-synced resources both cached as "no playable files"; the batch refresh must
        // re-discover every one of them and report progress through to 100%.
        foreach (var dirName in new[] { "Movie1", "Movie2" })
        {
            var dir = Path.Combine(_testRoot, dirName);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "a.mp4"), Array.Empty<byte>());
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
        await AddPlayableProfile("mp4");

        var resources = (await _sp.GetRequiredService<IResourceService>().GetAll()).ToList();
        Assert.AreEqual(2, resources.Count);

        var cacheOrm = _sp
            .GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>();
        foreach (var r in resources)
        {
            await cacheOrm.Add(new ResourceCacheDbModel
            {
                ResourceId = r.Id,
                CachedTypes = ResourceCacheType.PlayableFiles,
                PlayableFilePaths = null
            });
        }

        var lastPercentage = 0;
        await _sp.GetRequiredService<IResourceService>().RefreshResourcesCache(
            resources.Select(r => r.Id).ToList(),
            (percentage, _) =>
            {
                lastPercentage = percentage;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(100, lastPercentage);
        foreach (var r in resources)
        {
            var cache = await GetCacheRow(r.Id);
            Assert.IsNotNull(cache);
            Assert.IsFalse(string.IsNullOrEmpty(cache!.PlayableFilePaths),
                $"Batch refresh should have re-discovered the playable file for resource {r.Id}");
        }
    }

    [TestMethod]
    public async Task InvalidateAsync_ClearsCacheFlag()
    {
        var resource = await SeedResource("Movie", "a.mp4");
        await AddPlayableProfile("mp4");
        await Provider().GetPlayableItemsAsync(resource, CancellationToken.None);

        await Provider().InvalidateAsync(resource.Id);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsFalse(cache!.CachedTypes.HasFlag(ResourceCacheType.PlayableFiles));
    }

    [TestMethod]
    public async Task GetStatus_ReflectsCacheState()
    {
        var resource = await SeedResource("Movie");

        Assert.AreEqual(DataStatus.NotStarted, Provider().GetStatus(resource));

        resource.Cache = new ResourceFileSystemCache { CachedTypes = [ResourceCacheType.PlayableFiles] };
        Assert.AreEqual(DataStatus.Ready, Provider().GetStatus(resource));
    }
}
