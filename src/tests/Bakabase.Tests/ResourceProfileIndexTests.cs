using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for ResourceProfileIndexService — the inverted index that maps
/// resources to matching profiles. Covers RebuildAsync / readiness, the forward and
/// reverse lookups, priority-ordered results, and (filling a round-25 gap) profile
/// matching driven by a real search filter rather than a catch-all.
/// </summary>
[TestClass]
public sealed class ResourceProfileIndexTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceProfileIndexTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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
    }

    /// <summary>A profile search that matches only resources whose Filename equals the given value.</summary>
    private static string FilenameEqualsSearchJson(string filename)
        => JsonConvert.SerializeObject(new ResourceSearchDbModel
        {
            Group = new ResourceSearchFilterGroupDbModel
            {
                Combinator = SearchCombinator.And,
                Filters =
                [
                    new ResourceSearchFilterDbModel
                    {
                        PropertyPool = PropertyPool.Internal,
                        PropertyId = (int)InternalProperty.Filename,
                        Operation = SearchOperation.Equals,
                        Value = filename
                    }
                ]
            }
        });

    private Task<ResourceProfile> AddProfile(string name, string? searchJson, int priority = 100)
        => _sp.GetRequiredService<IResourceProfileService>()
            .Add(name, searchJson, null, null, null, null, null, priority);

    private IResourceProfileIndexService Index() => _sp.GetRequiredService<IResourceProfileIndexService>();

    private async Task RebuildIndex() => await Index().RebuildAsync(null, CancellationToken.None);

    private async Task<int> ResourceId(string filename)
        => (await _sp.GetRequiredService<IResourceService>().GetAll()).Single(r => r.FileName == filename).Id;

    [TestMethod]
    public async Task RebuildAsync_MakesIndexReady()
    {
        await Seed("a", "b");
        Assert.IsFalse(Index().IsReady);
        await RebuildIndex();
        Assert.IsTrue(Index().IsReady);
    }

    [TestMethod]
    public async Task GetMatchingResourceIds_CatchAllProfile_MatchesEveryResource()
    {
        await Seed("a", "b", "c");
        var profile = await AddProfile("all", "{}");
        await RebuildIndex();

        var matched = await Index().GetMatchingResourceIds(profile.Id);
        Assert.AreEqual(3, matched.Count);
    }

    [TestMethod]
    public async Task GetMatchingResourceIds_FilteringProfile_MatchesOnlyMatchingResources()
    {
        await Seed("apple", "banana", "cherry");
        var profile = await AddProfile("banana-only", FilenameEqualsSearchJson("banana"));
        await RebuildIndex();

        var matched = await Index().GetMatchingResourceIds(profile.Id);
        Assert.AreEqual(1, matched.Count);
        Assert.IsTrue(matched.Contains(await ResourceId("banana")));
    }

    [TestMethod]
    public async Task GetMatchingResourceIds_UnknownProfile_ReturnsEmpty()
    {
        await Seed("a");
        await RebuildIndex();
        Assert.AreEqual(0, (await Index().GetMatchingResourceIds(999999)).Count);
    }

    [TestMethod]
    public async Task GetMatchingProfileIds_ReturnsMatchingProfile()
    {
        await Seed("a");
        var profile = await AddProfile("all", "{}");
        await RebuildIndex();

        var profileIds = await Index().GetMatchingProfileIds(await ResourceId("a"));
        CollectionAssert.Contains(profileIds.ToList(), profile.Id);
    }

    [TestMethod]
    public async Task GetMatchingProfileIds_FilteringProfile_ExcludesNonMatchingResource()
    {
        await Seed("apple", "banana");
        var profile = await AddProfile("banana-only", FilenameEqualsSearchJson("banana"));
        await RebuildIndex();

        var appleProfiles = await Index().GetMatchingProfileIds(await ResourceId("apple"));
        Assert.IsFalse(appleProfiles.Contains(profile.Id));

        var bananaProfiles = await Index().GetMatchingProfileIds(await ResourceId("banana"));
        Assert.IsTrue(bananaProfiles.Contains(profile.Id));
    }

    [TestMethod]
    public async Task GetMatchingProfileIds_SortedByPriorityDescending()
    {
        await Seed("a");
        var low = await AddProfile("low", "{}", priority: 10);
        var high = await AddProfile("high", "{}", priority: 20);
        await RebuildIndex();

        var profileIds = await Index().GetMatchingProfileIds(await ResourceId("a"));
        CollectionAssert.AreEqual(new[] { high.Id, low.Id }, profileIds.ToList());
    }

    [TestMethod]
    public async Task GetMatchingProfileIdsForResources_BatchReturnsEachResource()
    {
        await Seed("a", "b");
        var profile = await AddProfile("all", "{}");
        await RebuildIndex();

        var resourceIds = (await _sp.GetRequiredService<IResourceService>().GetAll()).Select(r => r.Id).ToList();
        var map = await Index().GetMatchingProfileIdsForResources(resourceIds);

        Assert.AreEqual(2, map.Count);
        Assert.IsTrue(map.Values.All(ps => ps.Contains(profile.Id)));
    }

    [TestMethod]
    public async Task GetMatchingProfileIds_NoProfiles_ReturnsEmpty()
    {
        await Seed("a");
        await RebuildIndex();
        Assert.AreEqual(0, (await Index().GetMatchingProfileIds(await ResourceId("a"))).Count);
    }
}
