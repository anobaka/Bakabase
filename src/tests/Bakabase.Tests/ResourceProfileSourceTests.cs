using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Service.Extensions;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceProfileSourceTests
{
    private IServiceProvider _services = null!;
    private IResourceProfileService Profiles => _services.GetRequiredService<IResourceProfileService>();
    private IResourceProfileSourceEvaluator Evaluator => _services.GetRequiredService<IResourceProfileSourceEvaluator>();

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_services is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else if (_services is IDisposable disposable) disposable.Dispose();
    }

    private async Task<Resource> Resource(string title)
    {
        var id = (await _services.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title)).ResourceId;
        return (await _services.GetRequiredService<IResourceService>().Get(id))!;
    }

    private Task<ResourceProfile> SourceProfile(string source = "mac", string? epoch = "one", int priority = 100,
        string? search = "{}", ResourceProfilePlayerOptions? players = null, string? mode = null) =>
        Profiles.Add("Local opener", search, null, null, null, players, null, priority, source, epoch, mode);

    private static string Filter(PropertyPool pool, int property, SearchOperation operation, string? value) =>
        JsonConvert.SerializeObject(new ResourceSearchDbModel
        {
            Group = new ResourceSearchFilterGroupDbModel
            {
                Combinator = SearchCombinator.And,
                Filters = [new() { PropertyPool = pool, PropertyId = property, Operation = operation, Value = value }]
            }
        });

    [TestMethod]
    public async Task ForeignProfilesNeverMatchLocalResourceIdsBeforeOrAfterIndexBuild()
    {
        var local = await Resource("Local resource");
        var profile = await Profiles.Add("Local", "{}", null, null, null,
            new ResourceProfilePlayerOptions { Players = [new() { ExecutablePath = "local.exe" }] }, null, 1);
        var remote = await SourceProfile(players: new() { Players = [new() { ExecutablePath = "remote.exe" }] });
        CollectionAssert.AreEqual(new[] { profile.Id }, (await Profiles.GetMatchingProfiles(local)).Select(p => p.Id).ToArray());
        Assert.AreEqual(0, (await Profiles.GetMatchingResourceIds(remote.Id)).Count);
        await _services.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { profile.Id }, (await Profiles.GetMatchingProfiles(local)).Select(p => p.Id).ToArray());
        Assert.AreEqual("local.exe", (await Profiles.GetEffectivePlayerOptions(local))!.Players![0].ExecutablePath);
    }

    [TestMethod]
    public async Task SourceFiltersRoundTripWithoutResolvingAgainstLocalPropertyIds()
    {
        var json = Filter(PropertyPool.Custom, 987654, SearchOperation.Equals, "\"foreign-option-id\"");
        var profile = await SourceProfile(search: json);
        var saved = (await Profiles.Get(profile.Id))!;
        Assert.AreEqual(json, saved.SearchJson);
        Assert.IsNull(saved.Search.Group);
        var view = saved.ToViewModel(_services.GetRequiredService<IPropertyLocalizer>());
        var filter = view.Search!.Group!.Filters!.Single();
        Assert.AreEqual(987654, filter.PropertyId);
        Assert.AreEqual("\"foreign-option-id\"", filter.DbValue);
        Assert.IsNull(filter.Property, "Foreign custom fields must not be hydrated from this device's definitions.");
    }

    [TestMethod]
    public async Task SourceSelectionIsDeviceAndEpochScoped()
    {
        var first = await SourceProfile();
        await SourceProfile(source: "other");
        await SourceProfile(epoch: "old");
        CollectionAssert.AreEqual(new[] { first.Id },
            (await Profiles.GetSourceProfiles("mac", "one")).Select(p => p.Id).ToArray());
        Assert.AreEqual(0, (await Profiles.GetSourceProfiles("mac", "reset")).Count);
        Assert.AreEqual(0, (await Profiles.GetSourceProfiles("mac")).Count);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task SourceProfilesCannotBeCreatedOrUnpinnedWithoutALibraryEpoch(string? epoch)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => SourceProfile(epoch: epoch));
        var profile = await SourceProfile();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Profiles.Update(profile.Id, "Changed", "{}",
            null, null, null, null, null, 0, "mac", epoch));
        var unchanged = (await Profiles.Get(profile.Id))!;
        Assert.AreEqual("one", unchanged.SourceLibraryEpoch);
        Assert.AreEqual(profile.Name, unchanged.Name);
    }

    [TestMethod]
    public async Task ExistingNullSourceLocalProfilesKeepTheirBehaviorWithoutAnEpoch()
    {
        var resource = await Resource("Local resource");
        var profile = await Profiles.Add("Local", "{}", "{Name}", null, null,
            new() {Players = [new() {ExecutablePath = "local-player.exe"}]}, null, 5);
        await Profiles.Update(profile.Id, "Renamed local", "{}", profile.NameTemplate, null, null,
            profile.PlayerOptions, null, 6);
        var saved = (await Profiles.Get(profile.Id))!;
        Assert.IsNull(saved.SourceDeviceId);
        Assert.IsNull(saved.SourceLibraryEpoch);
        Assert.AreEqual("{Name}", saved.NameTemplate);
        Assert.AreEqual("local-player.exe",
            (await Profiles.GetEffectivePlayerOptions(resource))!.Players!.Single().ExecutablePath);
    }

    [TestMethod]
    public async Task EvaluationUsesSourceValuesAndOnlyReturnsRequestedExistingResources()
    {
        var first = await Resource("Owned alpha");
        var second = await Resource("Owned beta");
        var result = await Evaluator.EvaluateAsync(new()
        {
            ResourceIds = [first.Id, second.Id, int.MaxValue],
            Profiles = [new() { Id = 41, SearchJson = Filter(PropertyPool.Reserved, (int)ReservedProperty.Name,
                SearchOperation.Equals, "Owned alpha") }, new() { Id = 42, SearchJson = "{}" }]
        });
        Assert.AreEqual(2, result.Matches.Length);
        CollectionAssert.AreEquivalent(new[] { 41, 42 }, result.Matches.Single(m => m.ResourceId == first.Id).ProfileIds);
        CollectionAssert.AreEqual(new[] { 42 }, result.Matches.Single(m => m.ResourceId == second.Id).ProfileIds);
    }

    [TestMethod]
    public async Task MissingSourceFieldsAndUnsupportedOperationsCannotBecomeCatchAll()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.ParseSearchAsync(
            Filter(PropertyPool.Custom, 987654, SearchOperation.Equals, "\"value\"")));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.ParseSearchAsync(
            Filter(PropertyPool.Reserved, (int)ReservedProperty.Name, (SearchOperation)987654, "\"value\"")));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.ParseSearchAsync("{not-json"));
    }

    [TestMethod]
    public async Task OpeningBlocksKeepIndependentPriorityAndExplicitSystemDefault()
    {
        var lower = await SourceProfile(priority: 10, players: new() { Players = [new()
            { ExecutablePath = @"C:\Tools\Custom Viewer.exe", Command = "--open {0}", Extensions = [".custom"], SupportsHttp = false }] });
        var higher = await SourceProfile(priority: 20, mode: "ask");
        var effective = (await Profiles.GetSourceProfiles("mac", "one")).ResolveOpeningOptions();
        Assert.AreEqual("ask", effective.OpeningMode);
        Assert.AreEqual(lower.PlayerOptions!.Players![0].ExecutablePath, effective.PlayerOptions!.Players![0].ExecutablePath);
        await SourceProfile(priority: 30, players: new() { Players = [] });
        effective = (await Profiles.GetSourceProfiles("mac", "one")).ResolveOpeningOptions();
        Assert.AreEqual("ask", effective.OpeningMode);
        Assert.AreEqual(0, effective.PlayerOptions!.Players!.Count);
        Assert.IsTrue(effective.ProfileIds.Contains(higher.Id));
    }

    [TestMethod]
    public async Task SourceProfilesRejectSourceMutatingBlocksButLocalProfilesKeepThem()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Profiles.Add("remote", "{}", "{Name}", null,
            null, null, null, 0, "mac", "one"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Profiles.Add("remote", "{}", null, null,
            new() { Extensions = [".mp4"] }, null, null, 0, "mac", "one"));
        var local = await Profiles.Add("local", "{}", "{Name}", null,
            new() { Extensions = [".mp4"] }, null, null, 0);
        Assert.IsNull(local.SourceDeviceId);
        Assert.AreEqual("{Name}", local.NameTemplate);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => SourceProfile(mode: "anything"));
    }

    [TestMethod]
    public async Task EvaluationRejectsOversizedAndAmbiguousRequests()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.EvaluateAsync(new()
            { ResourceIds = Enumerable.Range(1, 257).ToArray() }));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.EvaluateAsync(new()
            { Profiles = [new() { Id = 1 }, new() { Id = 1 }] }));
    }

    [TestMethod]
    public async Task CandidateMatchingUsesSourceCustomFieldsLibrariesParentsTagsAndSources()
    {
        var resources = _services.GetRequiredService<IResourceService>();
        var parent = await Resource("Parent");
        var rows = await resources.AddAll([
            new() { Path = "/media/movie.mp4", ParentId = parent.Id, Tags = ResourceTag.Pinned },
            new() { Path = "/media/book.cbz" },
            new() { Path = "/outside/also-movie.mp4", Tags = ResourceTag.Pinned }
        ]);
        var movie = rows[0].Id;
        var book = rows[1].Id;
        var field = await _services.GetRequiredService<ICustomPropertyService>().Add(new()
            { Name = "Source-only category", Type = PropertyType.SingleLineText });
        await _services.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange([
            new() { PropertyId = field.Id, ResourceId = movie, Scope = 1, Value = "cinema" },
            new() { PropertyId = field.Id, ResourceId = rows[2].Id, Scope = 1, Value = "cinema" }
        ]);
        await _services.GetRequiredService<IMediaLibraryResourceMappingService>().EnsureMappings(movie, [817]);
        await _services.GetRequiredService<IResourceSourceLinkService>().Add(new()
            { ResourceId = movie, Source = ResourceSource.DLsite, SourceKey = "RJ100" });
        var rules = new ResourceProfileSearchRule[]
        {
            new() { Id = 1, SearchJson = Filter(PropertyPool.Custom, field.Id, SearchOperation.Equals, "cinema") },
            new() { Id = 2, SearchJson = Filter(PropertyPool.Internal, (int)InternalProperty.MediaLibraryV2Multi,
                SearchOperation.Contains, "817") },
            new() { Id = 3, SearchJson = Filter(PropertyPool.Internal, (int)InternalProperty.ParentResource,
                SearchOperation.Equals, parent.Id.ToString()) },
            new() { Id = 4, SearchJson = JsonConvert.SerializeObject(new ResourceSearchDbModel { Tags = [ResourceTag.Pinned] }) },
            new() { Id = 5, SearchJson = Filter(PropertyPool.Internal, (int)InternalProperty.Source,
                SearchOperation.Contains, ((int)ResourceSource.DLsite).ToString()) },
            new() { Id = 6, SearchJson = JsonConvert.SerializeObject(new ResourceSearchDbModel { Keyword = "cinema" }) },
            new() { Id = 7, SearchJson = JsonConvert.SerializeObject(new ResourceSearchDbModel { Keyword = "movie" }) },
            new() { Id = 8, SearchJson = Filter(PropertyPool.Internal, (int)InternalProperty.ParentResource,
                SearchOperation.IsNull, null) }
        };
        var result = await Evaluator.EvaluateAsync(new() { ResourceIds = [movie, book], Profiles = rules });
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6, 7 },
            result.Matches.Single(m => m.ResourceId == movie).ProfileIds,
            "Matched: " + string.Join(',', result.Matches.Single(m => m.ResourceId == movie).ProfileIds));
        CollectionAssert.AreEqual(new[] { 8 }, result.Matches.Single(m => m.ResourceId == book).ProfileIds);
        Assert.AreEqual(2, result.Matches.Length, "A matching resource outside the candidate page must not be returned.");
        // The old local profile filter path retains its result and is independent of source profiles.
        var localRule = await Profiles.Add("Local cinema", rules[0].SearchJson, null, null, null, null, null, 0);
        CollectionAssert.AreEquivalent(new[] { movie, rows[2].Id },
            (await Profiles.GetMatchingResourceIds(localRule.Id)).ToArray());
    }

    [TestMethod]
    public async Task InvalidOrExpensiveRegexFailsClosedInsteadOfChoosingAPlayer()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.ParseSearchAsync(
            Filter(PropertyPool.Reserved, (int)ReservedProperty.Name, SearchOperation.NotMatches, "[")));
        var resource = await Resource(new string('a', 10000) + "!");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Evaluator.EvaluateAsync(new()
        {
            ResourceIds = [resource.Id],
            Profiles = [new() { Id = 1, SearchJson = Filter(PropertyPool.Reserved, (int)ReservedProperty.Name,
                SearchOperation.NotMatches, "^(a+)+$") }]
        }));
    }
}
