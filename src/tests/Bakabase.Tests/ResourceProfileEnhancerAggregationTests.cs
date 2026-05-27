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
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for the enhancer-options aggregation seam in
/// <c>ResourceProfileService</c>. Two layers are pinned here:
///
/// 1. <c>GetEffectiveEnhancerOptions(resource)</c> — calls the private
///    <c>AggregateEnhancerOptions</c> after matching+sorting profiles.
///    The aggregation contract is **first-wins per enhancer id**, applied
///    as a full block — there is no per-target merge across profiles.
///    A bug here means a user with multiple matching profiles gets the
///    wrong scraper config, with no error.
///
/// 2. <c>GetMatchingProfiles</c> already sorts by Priority descending, so
///    profile order out of that method matches what the user configured.
///    These tests rely on that — if it changes, several below will fail.
///
/// All tests run with <c>IResourceProfileIndexService</c> *not* yet ready,
/// which forces the fallback evaluation path (covered alongside the index
/// path under <see cref="ResourceProfileApplicationTests"/>).
/// </summary>
[TestClass]
public sealed class ResourceProfileEnhancerAggregationTests
{
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
            $"ResourceProfileEnhancerAggregationTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    #region tests

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_SingleProfile_ReturnsThatProfilesEnhancers()
    {
        var resource = await SeedSingleResourceAsync();
        await AddProfileWithEnhancersAsync(
            "solo", priority: 100,
            enhancers:
            [
                EnhancerOpts(bangumiId: 3, targets: [(1, 42)]),
            ]);

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);

        effective.Should().ContainSingle();
        effective[0].EnhancerId.Should().Be(3);
        effective[0].TargetOptions!.Should().ContainSingle()
            .Which.PropertyId.Should().Be(42);
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_TwoProfilesSameEnhancer_HighPriorityWinsAsAFullBlock()
    {
        // Same enhancer id (3 = Bangumi) configured in both profiles with
        // different target bindings. The high-priority profile's WHOLE block
        // wins; the low-priority profile's bindings are discarded.
        var resource = await SeedSingleResourceAsync();
        await AddProfileWithEnhancersAsync(
            "low", priority: 10,
            enhancers:
            [
                EnhancerOpts(bangumiId: 3, targets: [(2, 7)]),  // would map Target=2 -> Property=7
            ]);
        await AddProfileWithEnhancersAsync(
            "high", priority: 100,
            enhancers:
            [
                EnhancerOpts(bangumiId: 3, targets: [(1, 42)]),  // maps Target=1 -> Property=42
            ]);

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);

        effective.Should().ContainSingle("aggregation collapses on EnhancerId, not on Target");
        effective[0].EnhancerId.Should().Be(3);
        effective[0].TargetOptions.Should().ContainSingle()
            .Which.PropertyId.Should().Be(42,
                "high-priority profile's TargetOptions must REPLACE the low-priority's, not merge alongside");
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_TwoProfilesDifferentEnhancers_BothAggregated()
    {
        // Different enhancer ids → no collision; both blocks should appear
        // in the result.
        var resource = await SeedSingleResourceAsync();
        await AddProfileWithEnhancersAsync(
            "p1", priority: 100,
            enhancers: [EnhancerOpts(bangumiId: 3, targets: [(1, 42)])]);
        await AddProfileWithEnhancersAsync(
            "p2", priority: 50,
            enhancers: [EnhancerOpts(bangumiId: 7 /* Tmdb */, targets: [(1, 99)])]);

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);

        effective.Should().HaveCount(2);
        effective.Select(e => e.EnhancerId).Should().BeEquivalentTo(new[] { 3, 7 });
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_ThreeProfilesMixed_FirstWinsPerEnhancerId()
    {
        // p1 (pri 100): Bangumi + Tmdb
        // p2 (pri 50):  Tmdb + Av
        // p3 (pri 10):  Bangumi + Av
        // Expected: Bangumi from p1, Tmdb from p1, Av from p2.
        var resource = await SeedSingleResourceAsync();
        await AddProfileWithEnhancersAsync(
            "p1", priority: 100,
            enhancers:
            [
                EnhancerOpts(bangumiId: 3, targets: [(1, 11)]),
                EnhancerOpts(bangumiId: 7, targets: [(1, 21)]),
            ]);
        await AddProfileWithEnhancersAsync(
            "p2", priority: 50,
            enhancers:
            [
                EnhancerOpts(bangumiId: 7, targets: [(1, 22)]),
                EnhancerOpts(bangumiId: 8, targets: [(1, 32)]),
            ]);
        await AddProfileWithEnhancersAsync(
            "p3", priority: 10,
            enhancers:
            [
                EnhancerOpts(bangumiId: 3, targets: [(1, 13)]),
                EnhancerOpts(bangumiId: 8, targets: [(1, 33)]),
            ]);

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);

        effective.Should().HaveCount(3);
        var byId = effective.ToDictionary(e => e.EnhancerId);
        byId[3].TargetOptions!.Single().PropertyId.Should().Be(11, "Bangumi: p1 wins");
        byId[7].TargetOptions!.Single().PropertyId.Should().Be(21, "Tmdb: p1 wins (already seen before p2)");
        byId[8].TargetOptions!.Single().PropertyId.Should().Be(32, "Av: p2 wins (p1 didn't configure it)");
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_ProfileWithNullEnhancerOptions_IsSkippedNotCrashed()
    {
        // A profile that has no EnhancerOptions at all must be skipped cleanly.
        // The lower-priority profile that DOES have options must still apply.
        var resource = await SeedSingleResourceAsync();
        await AddProfileAsync("no-enhancers", priority: 200, enhancerOptions: null);
        await AddProfileWithEnhancersAsync(
            "with-enhancers", priority: 100,
            enhancers: [EnhancerOpts(bangumiId: 3, targets: [(1, 42)])]);

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);

        effective.Should().ContainSingle();
        effective[0].EnhancerId.Should().Be(3);
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_NoMatchingProfiles_ReturnsEmpty()
    {
        // No profiles configured at all → aggregation returns empty list.
        var resource = await SeedSingleResourceAsync();
        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);
        effective.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_ProfileWithEmptyEnhancerList_TreatedAsNoOp()
    {
        // EnhancerOptions present but Enhancers list is empty — aggregation
        // must produce nothing for this profile.
        var resource = await SeedSingleResourceAsync();
        await AddProfileAsync(
            "empty-enhancer-list",
            priority: 100,
            enhancerOptions: new ResourceProfileEnhancerOptions { Enhancers = new List<EnhancerFullOptions>() });

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);
        effective.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetEffectiveEnhancerOptions_TiedPriority_IsDeterministicByInsertionOrder()
    {
        // Two profiles at the same priority — the aggregation does
        // OrderByDescending(p => p.Priority), which is a stable sort in
        // LINQ-to-Objects. The fallback profile-fetch order is GetAll's
        // ordering (insertion order from the DB). We pin that:
        // the first profile added is the "first" at the tied priority and
        // its enhancer block wins.
        var resource = await SeedSingleResourceAsync();
        await AddProfileWithEnhancersAsync(
            "first-at-50", priority: 50,
            enhancers: [EnhancerOpts(bangumiId: 3, targets: [(1, 100)])]);
        await AddProfileWithEnhancersAsync(
            "second-at-50", priority: 50,
            enhancers: [EnhancerOpts(bangumiId: 3, targets: [(1, 200)])]);

        var effective = await _profiles.GetEffectiveEnhancerOptions(resource);

        effective.Should().ContainSingle();
        effective[0].TargetOptions!.Single().PropertyId.Should().Be(100,
            "ties resolve by insertion order; first added wins. Change this expectation if the tie-break rule moves to explicit (e.g. by Id or by Name).");
    }

    #endregion

    #region helpers

    /// <summary>
    /// Set up a single layer-1 directory resource via PathMark + sync, then
    /// return the resulting Resource. Mirrors the pattern used by
    /// <see cref="ResourceProfileApplicationTests"/>.
    /// </summary>
    private async Task<Resource> SeedSingleResourceAsync()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, "R1"));

        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
            }),
            Priority = 100,
        });
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);

        var resources = await _sp.GetRequiredService<IResourceService>().GetAll();
        resources.Should().ContainSingle("seed produces exactly one resource");
        return resources.Single();
    }

    /// <summary>
    /// Builds an <see cref="EnhancerFullOptions"/> with the given enhancer id
    /// and a TargetOptions list mapping (target, propertyId) tuples into
    /// PropertyPool.Custom entries. PropertyPool/PropertyId are required
    /// because <c>StripInvalidEnhancerTargetOptions</c> would drop unbound
    /// entries during persistence.
    /// </summary>
    private static EnhancerFullOptions EnhancerOpts(int bangumiId, (int Target, int PropertyId)[] targets) =>
        new()
        {
            EnhancerId = bangumiId,
            TargetOptions = targets.Select(t => new EnhancerTargetFullOptions
            {
                Target = t.Target,
                PropertyId = t.PropertyId,
                PropertyPool = PropertyPool.Custom,
            }).ToList(),
        };

    private Task<ResourceProfile> AddProfileAsync(
        string name,
        int priority,
        ResourceProfileEnhancerOptions? enhancerOptions)
        => _profiles.Add(name, MatchAllSearchJson, null, enhancerOptions, null, null, null, priority);

    private Task<ResourceProfile> AddProfileWithEnhancersAsync(
        string name,
        int priority,
        EnhancerFullOptions[] enhancers)
        => AddProfileAsync(name, priority,
            new ResourceProfileEnhancerOptions { Enhancers = enhancers.ToList() });

    #endregion
}
