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
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using LocalFilePlayableItemProvider =
    Bakabase.InsideWorld.Business.Components.Providers.PlayableItem.LocalFilePlayableItemProvider;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for resource profiles: profile CRUD, profile-to-resource
/// matching (empty/null search behaves as a catch-all), priority-ordered effective
/// option resolution, and end-to-end playable-file discovery driven by a profile's
/// playable-file options through <see cref="LocalFilePlayableItemProvider"/>.
/// </summary>
[TestClass]
public sealed class ResourceProfileApplicationTests
{
    // An empty serialized ResourceSearchDbModel: a profile with this search has no
    // filter criteria, so it matches every resource.
    private const string MatchAllSearchJson = "{}";

    private string _testRoot = null!;
    private IServiceProvider _sp = null!;
    private IResourceProfileService _profiles = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _profiles = _sp.GetRequiredService<IResourceProfileService>();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceProfileApplicationTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private void Dir(string relative) => Directory.CreateDirectory(Path.Combine(_testRoot, relative));

    private void Touch(string relative)
    {
        var full = Path.Combine(_testRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Array.Empty<byte>());
    }

    /// <summary>Marks _testRoot as a layer-1 directory resource root, syncs, returns the resources.</summary>
    private async Task<System.Collections.Generic.List<Resource>> Seed()
    {
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
        return await _sp.GetRequiredService<IResourceService>().GetAll();
    }

    private Task<ResourceProfile> AddProfile(
        string name,
        ResourceProfilePlayableFileOptions? playableFile = null,
        string? nameTemplate = null,
        int priority = 100,
        string? searchJson = MatchAllSearchJson)
        => _profiles.Add(name, searchJson, nameTemplate, null, playableFile, null, null, priority);

    private LocalFilePlayableItemProvider PlayableProvider()
        => _sp.GetServices<IPlayableItemProvider>().OfType<LocalFilePlayableItemProvider>().Single();

    #region Profile CRUD

    [TestMethod]
    public async Task Add_PersistsProfile()
    {
        var added = await AddProfile("p",
            playableFile: new ResourceProfilePlayableFileOptions { Extensions = ["mp4"] }, priority: 55);

        Assert.IsTrue(added.Id > 0);
        var got = await _profiles.Get(added.Id);
        Assert.IsNotNull(got);
        Assert.AreEqual("p", got!.Name);
        Assert.AreEqual(55, got.Priority);
        Assert.AreEqual("mp4", got.PlayableFileOptions!.Extensions!.Single());
    }

    [TestMethod]
    public async Task GetAll_ReturnsEveryProfile()
    {
        await AddProfile("p1");
        await AddProfile("p2");
        await AddProfile("p3");
        Assert.AreEqual(3, (await _profiles.GetAll()).Count);
    }

    [TestMethod]
    public async Task Update_ChangesNameAndPriority()
    {
        var p = await AddProfile("before", priority: 10);
        await _profiles.Update(p.Id, "after", MatchAllSearchJson, null, null, null, null, null, 99);

        var got = await _profiles.Get(p.Id);
        Assert.AreEqual("after", got!.Name);
        Assert.AreEqual(99, got.Priority);
    }

    [TestMethod]
    public async Task Delete_RemovesProfile()
    {
        var p = await AddProfile("doomed");
        await _profiles.Delete(p.Id);
        Assert.IsNull(await _profiles.Get(p.Id));
    }

    [TestMethod]
    public async Task Get_NonExistent_ReturnsNull()
        => Assert.IsNull(await _profiles.Get(999999));

    #endregion

    #region Profile-to-resource matching

    [TestMethod]
    public async Task GetMatchingProfiles_EmptySearchProfile_MatchesResource()
    {
        Dir("R1");
        var resources = await Seed();
        await AddProfile("catch-all");

        var matching = await _profiles.GetMatchingProfiles(resources.Single());
        Assert.AreEqual(1, matching.Count);
    }

    [TestMethod]
    public async Task GetMatchingProfiles_NullSearchProfile_MatchesResourceAsCatchAll()
    {
        Dir("R1");
        var resources = await Seed();
        // A null search deserializes to an empty ResourceSearch, so it still matches everything.
        await AddProfile("no-search", searchJson: null);

        var matching = await _profiles.GetMatchingProfiles(resources.Single());
        Assert.AreEqual(1, matching.Count);
    }

    [TestMethod]
    public async Task GetMatchingProfiles_NoProfiles_ReturnsEmpty()
    {
        Dir("R1");
        var resources = await Seed();
        var matching = await _profiles.GetMatchingProfiles(resources.Single());
        Assert.AreEqual(0, matching.Count);
    }

    #endregion

    #region Effective option resolution

    [TestMethod]
    public async Task GetEffectivePlayableFileOptions_ReturnsMatchingProfileOptions()
    {
        Dir("R1");
        var resources = await Seed();
        await AddProfile("p", playableFile: new ResourceProfilePlayableFileOptions { Extensions = ["mkv"] });

        var opts = await _profiles.GetEffectivePlayableFileOptions(resources.Single());
        Assert.IsNotNull(opts);
        Assert.AreEqual("mkv", opts!.Extensions!.Single());
    }

    [TestMethod]
    public async Task GetEffectiveNameTemplate_ReturnsMatchingProfileTemplate()
    {
        Dir("R1");
        var resources = await Seed();
        await AddProfile("p", nameTemplate: "{Name} - tpl");

        Assert.AreEqual("{Name} - tpl", await _profiles.GetEffectiveNameTemplate(resources.Single()));
    }

    [TestMethod]
    public async Task GetEffectivePlayableFileOptions_NoProfiles_ReturnsNull()
    {
        Dir("R1");
        var resources = await Seed();
        Assert.IsNull(await _profiles.GetEffectivePlayableFileOptions(resources.Single()));
    }

    [TestMethod]
    public async Task GetEffectivePlayableFileOptions_HigherPriorityProfileWins()
    {
        Dir("R1");
        var resources = await Seed();
        await AddProfile("low", playableFile: new ResourceProfilePlayableFileOptions { Extensions = ["avi"] },
            priority: 10);
        await AddProfile("high", playableFile: new ResourceProfilePlayableFileOptions { Extensions = ["mp4"] },
            priority: 20);

        var opts = await _profiles.GetEffectivePlayableFileOptions(resources.Single());
        Assert.AreEqual("mp4", opts!.Extensions!.Single());
    }

    #endregion

    #region Playable-file discovery

    [TestMethod]
    public async Task PlayableItems_DiscoversFilesMatchingProfileExtensions()
    {
        Touch("Movie/a.mp4");
        Touch("Movie/b.mp4");
        Touch("Movie/c.txt");
        var resources = await Seed();
        await AddProfile("p", playableFile: new ResourceProfilePlayableFileOptions { Extensions = ["mp4"] });

        var result = await PlayableProvider().GetPlayableItemsAsync(resources.Single(), CancellationToken.None);

        Assert.AreEqual(2, result.Items.Count);
        Assert.IsTrue(result.Items.All(i => i.Key.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task PlayableItems_FileNamePatternFilter_NarrowsResults()
    {
        Touch("Show/ep01.mp4");
        Touch("Show/ep02.mp4");
        Touch("Show/trailer.mp4");
        var resources = await Seed();
        await AddProfile("p", playableFile: new ResourceProfilePlayableFileOptions
        {
            Extensions = ["mp4"],
            FileNamePattern = "^ep"
        });

        var result = await PlayableProvider().GetPlayableItemsAsync(resources.Single(), CancellationToken.None);

        Assert.AreEqual(2, result.Items.Count);
        Assert.IsTrue(result.Items.All(i => Path.GetFileName(i.Key).StartsWith("ep", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PlayableItems_NoProfile_ReturnsEmpty()
    {
        Touch("Movie/a.mp4");
        var resources = await Seed();
        // No profile -> no effective playable-file options -> nothing is treated as playable.
        var result = await PlayableProvider().GetPlayableItemsAsync(resources.Single(), CancellationToken.None);
        Assert.AreEqual(0, result.Items.Count);
    }

    #endregion
}
