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
using LocalFileCoverProvider =
    Bakabase.InsideWorld.Business.Components.Providers.Cover.LocalFileCoverProvider;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for the save/load (cache) side of cover discovery via
/// <see cref="LocalFileCoverProvider"/>: a resolved result is reused without rediscovery,
/// an empty result is a valid cached result, a missing directory or coverless directory
/// still persists the cache flag, InvalidateAsync clears it, and GetStatus reflects state.
/// </summary>
[TestClass]
public sealed class LocalFileCoverProviderCacheTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"CoverCacheTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private LocalFileCoverProvider Provider()
        => _sp.GetServices<ICoverProvider>().OfType<LocalFileCoverProvider>().Single();

    private Task<ResourceCacheDbModel?> GetCacheRow(int resourceId)
        => _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>()
            .GetByKey(resourceId, true);

    [TestMethod]
    public async Task CacheHit_ReturnsCachedCoverPaths()
    {
        var resource = new Resource
        {
            Path = Path.Combine(_testRoot, "Movie"),
            Cache = new ResourceFileSystemCache
            {
                CachedTypes = [ResourceCacheType.Covers],
                CoverPaths = ["/elsewhere/cover.jpg"]
            }
        };

        var result = await Provider().GetCoversAsync(resource, CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { "/elsewhere/cover.jpg" }, result!);
    }

    [TestMethod]
    public async Task CacheHit_EmptyCoverPaths_ReturnsNull()
    {
        var resource = new Resource
        {
            Path = Path.Combine(_testRoot, "Movie"),
            Cache = new ResourceFileSystemCache
            {
                CachedTypes = [ResourceCacheType.Covers],
                CoverPaths = null
            }
        };

        Assert.IsNull(await Provider().GetCoversAsync(resource, CancellationToken.None));
    }

    [TestMethod]
    public async Task NoCoverInDirectory_CachesEmptyResult()
    {
        var resource = await SeedResource("Movie", "notes.txt");

        var result = await Provider().GetCoversAsync(resource, CancellationToken.None);
        Assert.IsNull(result);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsNotNull(cache);
        Assert.IsTrue(cache!.CachedTypes.HasFlag(ResourceCacheType.Covers));
    }

    [TestMethod]
    public async Task MissingDirectory_CachesEmptyResult()
    {
        var resource = await SeedResource("Movie");
        Directory.Delete(resource.Path!, true);

        var result = await Provider().GetCoversAsync(resource, CancellationToken.None);
        Assert.IsNull(result);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsTrue(cache!.CachedTypes.HasFlag(ResourceCacheType.Covers));
    }

    [TestMethod]
    public async Task InvalidateAsync_ClearsCoversCacheFlag()
    {
        var resource = await SeedResource("Movie", "notes.txt");
        await Provider().GetCoversAsync(resource, CancellationToken.None);

        await Provider().InvalidateAsync(resource.Id);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsFalse(cache!.CachedTypes.HasFlag(ResourceCacheType.Covers));
    }

    [TestMethod]
    public async Task GetStatus_ReflectsCacheState()
    {
        var resource = new Resource { Path = Path.Combine(_testRoot, "Movie") };

        Assert.AreEqual(DataStatus.NotStarted, Provider().GetStatus(resource));

        resource.Cache = new ResourceFileSystemCache { CachedTypes = [ResourceCacheType.Covers] };
        Assert.AreEqual(DataStatus.Ready, Provider().GetStatus(resource));
    }

    [TestMethod]
    public async Task ConcurrentDiscoveryOfSameResource_NoUniqueConstraintViolation()
    {
        // Regression: two concurrent GetCoversAsync calls for the same resource
        // both observed cache==null and raced on _resourceCacheOrm.Add, hitting
        // SQLite UNIQUE constraint on ResourceCaches.ResourceId.
        var resource = await SeedResource("Movie", "notes.txt");

        var provider = Provider();
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => provider.GetCoversAsync(resource, CancellationToken.None)))
            .ToArray();

        await Task.WhenAll(tasks);

        var cache = await GetCacheRow(resource.Id);
        Assert.IsNotNull(cache);
        Assert.IsTrue(cache!.CachedTypes.HasFlag(ResourceCacheType.Covers));
    }
}
