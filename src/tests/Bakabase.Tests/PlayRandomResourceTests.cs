using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// "Random play" picks a resource that has playable files and opens the first one.
/// It used to draw straight from the resource cache table, which has no foreign key
/// to the resource: deleting a resource (e.g. after removing its media library and
/// the owning path mark) left the cache row behind, and drawing one of those handed
/// an unknown id to PlayItem — surfacing to the user as a 404 "data does not exist".
///
/// These tests lock down both halves of the fix: deleting a resource drops its cache
/// row, and random play ignores any dangling row that predates the fix.
/// </summary>
[TestClass]
public class PlayRandomResourceTests
{
    private IServiceProvider _sp = null!;
    private IResourceService _resourceService = null!;
    private string _testRoot = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _resourceService = _sp.GetRequiredService<IResourceService>();
        _testRoot = Path.Combine(Path.GetTempPath(), $"PlayRandomTests_{Guid.NewGuid():N}");
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

    private FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> CacheOrm =>
        _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>();

    private UIOptions.UIResourceOptions ResourceUiOptions =>
        _sp.GetRequiredService<IBOptions<UIOptions>>().Value.Resource;

    /// <summary>Creates a file resource backed by a real file, optionally with a cache row.</summary>
    private async Task<int> SeedPlayableResource(string fileName, bool withCache = true)
    {
        var path = Path.Combine(_testRoot, fileName);
        await File.WriteAllTextAsync(path, string.Empty);

        var added = await _resourceService.AddAll([new ResourceDbModel { Path = path, IsFile = true }]);
        var id = added.Single().Id;

        if (withCache)
        {
            await AddCacheRow(id, path);
        }

        return id;
    }

    private async Task AddCacheRow(int resourceId, params string[] playableFiles) =>
        await CacheOrm.Add(new ResourceCacheDbModel
        {
            ResourceId = resourceId,
            CachedTypes = ResourceCacheType.PlayableFiles,
            PlayableFilePaths = new ListStringValueBuilder(playableFiles.ToList()).Value
                .SerializeAsStandardValue(StandardValueType.ListString)
        });

    [TestMethod]
    public async Task DeleteByKeys_DropsTheResourceCacheRow()
    {
        var id = await SeedPlayableResource("doomed.mp4");
        (await CacheOrm.GetByKey(id, true)).Should().NotBeNull();

        await _resourceService.DeleteByKeys([id]);

        (await CacheOrm.GetByKey(id, true)).Should().BeNull(
            "a cache row keyed by a deleted resource is unreachable data, and random play " +
            "draws from this table — a leftover row is exactly what produced the 404");
    }

    [TestMethod]
    public async Task PlayRandomResource_IgnoresCacheRowsWhoseResourceIsGone()
    {
        // One live resource plus a dangling cache row of the shape an older build left
        // behind. Every draw must land on the live resource.
        var liveId = await SeedPlayableResource("live.mp4");
        await AddCacheRow(liveId + 1000, Path.Combine(_testRoot, "vanished.mp4"));

        for (var i = 0; i < 30; i++)
        {
            var response = await _resourceService.PlayRandomResource();
            response.Code.Should().Be(0,
                $"attempt {i} must never pick the dangling cache row: {response.Message}");
        }
    }

    [TestMethod]
    public async Task PlayRandomResource_ReportsNoPlayableResource_WhenOnlyDanglingRowsRemain()
    {
        // The library is empty but a stale cache row survives. The user gets the
        // "nothing to play" message rather than a 404 about a resource they deleted.
        await AddCacheRow(12345, Path.Combine(_testRoot, "vanished.mp4"));

        var response = await _resourceService.PlayRandomResource();

        response.Code.Should().NotBe(0);
        response.Message.Should().Contain("No playable resource");
    }

    [TestMethod]
    public async Task PlayRandomResource_DiscoversLive_WhenNothingIsCached()
    {
        // No cache row at all — random play falls back to probing resources live so the
        // feature still works before the cache is warm (and while it is switched off).
        await SeedPlayableResource("live.mp4", withCache: false);
        await _sp.GetRequiredService<IResourceProfileService>().Add(
            "playable", "{}", null, null,
            new ResourceProfilePlayableFileOptions { Extensions = ["mp4"] }, null, null, 100);

        var response = await _resourceService.PlayRandomResource();

        response.Code.Should().Be(0, response.Message);
    }

    [TestMethod]
    public async Task PlayRandomResource_IgnoresTheCache_WhenTheCacheIsDisabled()
    {
        // With the cache switched off, a cache row pointing at a file that no longer
        // exists must not be trusted; discovery runs live against the real resource.
        var id = await SeedPlayableResource("live.mp4", withCache: false);
        await AddCacheRow(id, Path.Combine(_testRoot, "stale-and-gone.mp4"));
        await _sp.GetRequiredService<IResourceProfileService>().Add(
            "playable", "{}", null, null,
            new ResourceProfilePlayableFileOptions { Extensions = ["mp4"] }, null, null, 100);

        ResourceUiOptions.DisablePlayableFileCache = true;

        var response = await _resourceService.PlayRandomResource();

        response.Code.Should().Be(0, response.Message);
    }
}
