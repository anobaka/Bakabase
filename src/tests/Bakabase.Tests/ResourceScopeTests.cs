using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceScopeTests
{
    private IServiceProvider _services = null!;
    private IResourceProfileService Profiles => _services.GetRequiredService<IResourceProfileService>();
    private IResourceScopeService Scopes => _services.GetRequiredService<IResourceScopeService>();

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();
    [TestCleanup]
    public async Task Cleanup()
    {
        if (_services is IAsyncDisposable disposable) await disposable.DisposeAsync();
        else if (_services is IDisposable synchronous) synchronous.Dispose();
    }
    private Task<ResourceProfile> Add(string name, int priority = 10, string? source = null, string? epoch = null) =>
        Profiles.Add(name, "{}", source == null ? "title" : null, null, null,
            new() { Players = [new() { ExecutablePath = name + ".exe" }] }, null, priority, source, epoch);
    private static string Named(string name) => JsonConvert.SerializeObject(new ResourceSearchDbModel {
        Group = new() { Combinator = SearchCombinator.And, Filters = [new() {
            PropertyPool = PropertyPool.Reserved, PropertyId = (int)ReservedProperty.Name,
            Operation = SearchOperation.Equals, Value = name }] }
    });
    private async Task<Resource> Resource(string name)
    {
        var id = (await _services.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(name)).ResourceId;
        return (await _services.GetRequiredService<IResourceService>().Get(id))!;
    }

    [TestMethod]
    public async Task SharedScopeEditsRebuildEveryReferencingRuleWithoutChangingPriorityOrLegacyRules()
    {
        var alpha = await Resource("alpha");
        var beta = await Resource("beta");
        var low = await Add("low", 10);
        var high = await Add("high", 20);
        var legacy = await Add("legacy", 1);
        var scope = await Scopes.Add("Shared", Named("alpha"), null, null);
        await Scopes.Bind(low.Id, scope.Id, low.UpdatedAt);
        await Scopes.Bind(high.Id, scope.Id, high.UpdatedAt);
        await _services.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, CancellationToken.None);
        Assert.AreEqual("high.exe", (await Profiles.GetEffectivePlayerOptions(alpha))!.Players![0].ExecutablePath);
        Assert.AreEqual("legacy.exe", (await Profiles.GetEffectivePlayerOptions(beta))!.Players![0].ExecutablePath);
        await Scopes.Update(scope.Id, scope.Revision, "Shared updated", Named("beta"));
        var tasks = _services.GetRequiredService<BTaskManager>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (tasks.GetTaskViewModel("ResourceProfileIndex") == null && DateTime.UtcNow < deadline) await Task.Delay(20);
        await tasks.Start("ResourceProfileIndex");
        while (tasks.GetTaskViewModel("ResourceProfileIndex")?.Status != BTaskStatus.Completed && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.AreEqual(BTaskStatus.Completed, tasks.GetTaskViewModel("ResourceProfileIndex")?.Status);
        Assert.AreEqual("legacy.exe", (await Profiles.GetEffectivePlayerOptions(alpha))!.Players![0].ExecutablePath);
        Assert.AreEqual("high.exe", (await Profiles.GetEffectivePlayerOptions(beta))!.Players![0].ExecutablePath);
        Assert.IsNull((await Profiles.Get(legacy.Id))!.ResourceScopeId);
        Assert.AreEqual(20, (await Profiles.Get(high.Id))!.Priority);
        Assert.AreEqual("title", (await Profiles.Get(high.Id))!.NameTemplate);
    }

    [TestMethod]
    public async Task RuleWritesPreserveOtherBlocksAndRejectStaleEditors()
    {
        var profile = await Add("Original");
        var scope = await Scopes.Add("All", null, null, null);
        profile = await Scopes.Bind(profile.Id, scope.Id, profile.UpdatedAt);
        var updated = await Scopes.UpdateRule(profile.Id, ResourceProfileRuleKind.Enhancers,
            "{\"enhancers\":[{\"enhancerId\":8,\"targetOptions\":[{\"target\":1,\"propertyPool\":4,\"propertyId\":99}]}]}", profile.UpdatedAt);
        Assert.IsNull(updated.PropertyOptions, "Saving enhancer targets must not silently link properties.");
        Assert.AreEqual(profile.ResourceScopeId, updated.ResourceScopeId);
        Assert.AreEqual(profile.NameTemplate, updated.NameTemplate);
        Assert.AreEqual(profile.Priority, updated.Priority);
        Assert.AreEqual("Original.exe", updated.PlayerOptions!.Players![0].ExecutablePath);
        await Assert.ThrowsExceptionAsync<ResourceScopeConflictException>(() => Scopes.UpdateRule(profile.Id,
            ResourceProfileRuleKind.Players, "{\"players\":[]}", profile.UpdatedAt));
        var current = (await Profiles.Get(profile.Id))!;
        Assert.AreEqual(8, current.EnhancerOptions!.Enhancers![0].EnhancerId);
        var cleared = await Scopes.UpdateRule(profile.Id, ResourceProfileRuleKind.Players,
            "{\"players\":[]}", current.UpdatedAt);
        Assert.AreEqual(0, cleared.PlayerOptions!.Players!.Count);
        Assert.AreEqual(8, cleared.EnhancerOptions!.Enhancers![0].EnhancerId);
    }

    [TestMethod]
    public async Task UnlinkSnapshotsEffectiveConditionsAndReferencedScopesCannotBeDeleted()
    {
        var profile = await Add("original");
        var scope = await Scopes.Add("Shared", Named("alpha"), null, null);
        profile = await Scopes.Bind(profile.Id, scope.Id, profile.UpdatedAt);
        var changed = await Scopes.Update(scope.Id, scope.Revision, "Shared", Named("beta"));
        await Assert.ThrowsExceptionAsync<ResourceScopeConflictException>(() => Scopes.Delete(scope.Id, changed.Revision));
        profile = await Scopes.Bind(profile.Id, null, profile.UpdatedAt);
        Assert.IsNull(profile.ResourceScopeId);
        Assert.AreEqual(Named("beta"), profile.SearchJson);
        await Scopes.Delete(scope.Id, changed.Revision);
        Assert.AreEqual(0, (await Scopes.GetAll()).Count);
        Assert.AreEqual(Named("beta"), (await Profiles.Get(profile.Id))!.SearchJson);
    }

    [TestMethod]
    public async Task StaleScopeRevisionCannotOverwriteNewConditionsOrDeleteScope()
    {
        var scope = await Scopes.Add("Shared", Named("alpha"), null, null);
        await Scopes.Update(scope.Id, scope.Revision, "New", Named("beta"));
        await Assert.ThrowsExceptionAsync<ResourceScopeConflictException>(() => Scopes.Update(scope.Id, scope.Revision, "Old", null));
        await Assert.ThrowsExceptionAsync<ResourceScopeConflictException>(() => Scopes.Delete(scope.Id, scope.Revision));
        var current = (await Scopes.GetAll()).Single();
        Assert.AreEqual(2, current.Revision);
        Assert.AreEqual(Named("beta"), current.SearchJson);
    }

    [TestMethod]
    public async Task ScopeSourceAndEpochMustMatchAndForeignRulesCannotModifyOwnerFileOrMetadataSettings()
    {
        var local = await Add("local");
        var foreign = await Add("remote", source: "source-a", epoch: "epoch-a");
        var wrongEpoch = await Add("old", source: "source-a", epoch: "old");
        var scope = await Scopes.Add("Foreign", "{}", "source-a", "epoch-a");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Scopes.Bind(local.Id, scope.Id, local.UpdatedAt));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Scopes.Bind(wrongEpoch.Id, scope.Id, wrongEpoch.UpdatedAt));
        foreign = await Scopes.Bind(foreign.Id, scope.Id, foreign.UpdatedAt);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Scopes.UpdateRule(foreign.Id,
            ResourceProfileRuleKind.PlayableFiles, "{\"extensions\":[\"mp4\"]}", foreign.UpdatedAt));
        var updated = await Scopes.UpdateRule(foreign.Id, ResourceProfileRuleKind.OpeningMode, "\"native\"", foreign.UpdatedAt);
        Assert.AreEqual("native", updated.OpeningMode);
        Assert.AreEqual("source-a", updated.SourceDeviceId);
        Assert.AreEqual("epoch-a", updated.SourceLibraryEpoch);
    }

    [TestMethod]
    public async Task LegacyFullProfileUpdatePreservesScopeAndCannotReplaceSharedConditions()
    {
        var profile = await Add("legacy");
        var scope = await Scopes.Add("Shared", Named("alpha"), null, null);
        profile = await Scopes.Bind(profile.Id, scope.Id, profile.UpdatedAt);
        await Profiles.Update(profile.Id, "Rename", Named("beta"), profile.NameTemplate, profile.EnhancerOptions,
            profile.PlayableFileOptions, profile.PlayerOptions, profile.PropertyOptions, 40);
        var current = (await Profiles.Get(profile.Id))!;
        Assert.AreEqual(scope.Id, current.ResourceScopeId);
        Assert.AreEqual(Named("alpha"), current.SearchJson);
        Assert.AreEqual("Rename", current.Name);
        Assert.AreEqual(40, current.Priority);
    }

    [TestMethod]
    public async Task MalformedOrUnknownRuleSettingsCannotReplaceSavedData()
    {
        var profile = await Add("unchanged");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Scopes.UpdateRule(profile.Id,
            ResourceProfileRuleKind.Players, "{\"unknown\":true}", profile.UpdatedAt));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Scopes.UpdateRule(profile.Id,
            ResourceProfileRuleKind.OpeningMode, "\"invalid-mode\"", profile.UpdatedAt));
        Assert.AreEqual("unchanged.exe", (await Profiles.Get(profile.Id))!.PlayerOptions!.Players![0].ExecutablePath);
    }
}
