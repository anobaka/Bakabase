using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for <see cref="IResourceMaterializationService"/> — the one entry point for a resource
/// gaining or losing its local files.
/// <para>
/// Writing <c>Resource.Path</c> is the easy half. The half that used to be forgotten at every call
/// site is everything derived from it: whether the path is a file, its timestamps, which resource
/// is its parent, the path marks that now describe it, the caches that no longer do, and the
/// resource that was already sitting on that path. These tests pin that whole transition.
/// </para>
/// </summary>
[TestClass]
public sealed class ResourceMaterializationTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;
    private readonly FakeDLsiteResolver _resolver = new();

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            // The resolvers are registered by assembly scan; swap the real DLsite one out so the
            // test drives what the source reports.
            var registered = services
                .Where(s => s.ServiceType == typeof(IResourceResolver) &&
                            s.ImplementationType == typeof(DLsiteResolver))
                .ToList();
            foreach (var d in registered)
            {
                services.Remove(d);
            }

            services.AddSingleton<IResourceResolver>(_resolver);
        });

        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceMaterializationTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private IResourceService ResourceService => _sp.GetRequiredService<IResourceService>();
    private IResourceMaterializationService Materialization =>
        _sp.GetRequiredService<IResourceMaterializationService>();

    /// <summary>
    /// Persists the shape <see cref="ResourceSyncService"/> already creates for a source entry whose
    /// files are not downloaded yet: an identity, no path.
    /// </summary>
    private async Task<Resource> AddUnmaterializedResource(string sourceKey = "RJ00000001")
    {
        await ResourceService.AddOrPutRange([
            new Resource
            {
                Path = null,
                IsFile = false,
                Status = ResourceStatus.Active,
                DisplayName = "unmaterialized-work",
                FileCreatedAt = DateTime.Now,
                FileModifiedAt = DateTime.Now,
                SourceLinks =
                [
                    new ResourceSourceLink { Source = ResourceSource.DLsite, SourceKey = sourceKey }
                ]
            }
        ]);

        var all = await ResourceService.GetAll();
        return all.First(r => !r.HasLocalPath);
    }

    /// <summary>
    /// Adds a synced property mark covering <paramref name="path"/>. Only synced marks can be
    /// pushed back to pending, which is what proves materialization asked for a property re-sync.
    /// </summary>
    private async Task<PathMark> AddSyncedPropertyMark(string path)
    {
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var mark = await pathMarkService.Add(new PathMark
        {
            Path = path,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Reserved,
                PropertyId = (int)ReservedProperty.Rating,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "5",
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 50
        });
        await pathMarkService.MarkAsSyncedBatch([mark.Id]);
        return mark;
    }

    /// <summary>
    /// Creates on-disk directories and turns them into resources through a real path-mark sync, so
    /// the resources under test carry the source links a path-mark resource actually has.
    /// </summary>
    private async Task SyncPathMarkResources(params string[] directoryNames)
    {
        foreach (var n in directoryNames)
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

    private async Task<List<ReservedPropertyValue>> GetReservedValues(int resourceId) =>
        await _sp.GetRequiredService<IReservedPropertyValueService>()
            .GetAll(v => v.ResourceId == resourceId);

    [TestMethod]
    public async Task Materialize_WritesDerivedStateAndReSyncsCoveringMarks()
    {
        // A resource already sits on the root, so the newly materialized one has a parent to find.
        var workPath = Path.Combine(_testRoot, "work");
        Directory.CreateDirectory(workPath);
        await ResourceService.AddOrPutRange([
            new Resource
            {
                Path = _testRoot,
                IsFile = false,
                Status = ResourceStatus.Active,
                FileCreatedAt = DateTime.Now,
                FileModifiedAt = DateTime.Now
            }
        ]);
        var rootResource = (await ResourceService.GetAll()).First(r => r.Path == _testRoot);

        var mark = await AddSyncedPropertyMark(_testRoot);
        var resource = await AddUnmaterializedResource();

        var result = await Materialization.MaterializeAsync(resource.Id, workPath,
            new MaterializationOptions(EnqueuePathMarkSync: false), CancellationToken.None);

        Assert.IsFalse(result.Merged, "nothing occupied the path");

        var materialized = (await ResourceService.Get(resource.Id))!;
        Assert.IsTrue(materialized.HasLocalPath);
        Assert.IsFalse(materialized.IsFile, "a directory was materialized");
        Assert.AreEqual(rootResource.Id, materialized.ParentId,
            "the parent-child tree must be rebuilt around the new path");

        var di = new DirectoryInfo(workPath);
        Assert.AreEqual(di.CreationTime.TruncateToMilliseconds(), materialized.FileCreatedAt);
        Assert.AreEqual(di.LastWriteTime.TruncateToMilliseconds(), materialized.FileModifiedAt);

        var reloadedMark = (await _sp.GetRequiredService<IPathMarkService>().Get(mark.Id))!;
        Assert.AreEqual(PathMarkSyncStatus.Pending, reloadedMark.SyncStatus,
            "the property mark covering the new path has to run again");
    }

    [TestMethod]
    public async Task Materialize_AbsorbsTheResourceAlreadyOwningThePath()
    {
        await SyncPathMarkResources("alpha");
        var alphaPath = Path.Combine(_testRoot, "alpha");
        var occupant = (await ResourceService.GetAll())
            .First(r => string.Equals(r.Path, alphaPath, StringComparison.OrdinalIgnoreCase));

        // Give the occupant property values in both pools — deleting it must not take them along.
        var customProperty = await _sp.GetRequiredService<ICustomPropertyService>().Add(
            new CustomPropertyAddOrPutDto { Name = "Studio", Type = PropertyType.SingleLineText });
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange([
            new CustomPropertyValueDbModel
            {
                ResourceId = occupant.Id,
                PropertyId = customProperty.Id,
                Scope = (int)PropertyValueScope.Manual,
                Value = "Studio A"
            }
        ]);
        var occupantNames = await GetReservedValues(occupant.Id);
        Assert.IsTrue(occupantNames.Any(v => !string.IsNullOrEmpty(v.Name)),
            "the path-mark sync should have named the occupant after its folder");
        var occupantName = occupantNames.First(v => !string.IsNullOrEmpty(v.Name)).Name;

        var resource = await AddUnmaterializedResource();

        var result = await Materialization.MaterializeAsync(resource.Id, alphaPath,
            new MaterializationOptions(EnqueuePathMarkSync: false), CancellationToken.None);

        Assert.AreEqual(occupant.Id, result.MergedResourceId);
        Assert.IsNull(await ResourceService.Get(occupant.Id), "the absorbed resource is deleted");

        var survivor = (await ResourceService.Get(resource.Id))!;
        Assert.AreEqual(alphaPath.StandardizePath(), survivor.Path);

        var survivorCustomValues = await _sp.GetRequiredService<ICustomPropertyValueService>()
            .GetAllDbModels(v => v.ResourceId == resource.Id);
        Assert.AreEqual(1, survivorCustomValues.Count, "custom values move to the survivor");
        Assert.AreEqual("Studio A", survivorCustomValues[0].Value);

        var survivorReserved = await GetReservedValues(resource.Id);
        Assert.IsTrue(survivorReserved.Any(v => v.Name == occupantName),
            "reserved values move to the survivor");

        var survivorLinks = await _sp.GetRequiredService<IResourceSourceLinkService>()
            .GetByResourceIds([resource.Id]);
        CollectionAssert.AreEquivalent(
            new[] { ResourceSource.DLsite, ResourceSource.PathMark },
            survivorLinks.Select(l => l.Source).Distinct().ToArray(),
            "the survivor keeps its own identity and inherits the path-mark one");
    }

    [TestMethod]
    public async Task Materialize_RefusesAnOccupiedPathWhenMergingIsDisabled()
    {
        await SyncPathMarkResources("alpha");
        var alphaPath = Path.Combine(_testRoot, "alpha");
        var resource = await AddUnmaterializedResource();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await Materialization.MaterializeAsync(resource.Id, alphaPath,
                new MaterializationOptions(MergeIfPathOwnedByAnotherResource: false), CancellationToken.None));

        Assert.IsFalse((await ResourceService.Get(resource.Id))!.HasLocalPath,
            "a refused materialization must leave the resource alone");
    }

    [TestMethod]
    public async Task DematerializeThenMaterialize_LeavesNoStaleCache()
    {
        var firstPath = Path.Combine(_testRoot, "first");
        var secondPath = Path.Combine(_testRoot, "second");
        Directory.CreateDirectory(firstPath);
        Directory.CreateDirectory(secondPath);
        await File.WriteAllTextAsync(Path.Combine(firstPath, "cover.jpg"), "not really an image");

        var resource = await AddUnmaterializedResource();
        var noMarkSync = new MaterializationOptions(EnqueuePathMarkSync: false);

        await Materialization.MaterializeAsync(resource.Id, firstPath, noMarkSync, CancellationToken.None);
        // Populate the filesystem cache so there is something stale to leave behind.
        await ResourceService.RefreshResourceCache(resource.Id, CancellationToken.None);

        await Materialization.DematerializeAsync(resource.Id, CancellationToken.None);

        var dematerialized = (await ResourceService.Get(resource.Id))!;
        Assert.IsFalse(dematerialized.HasLocalPath);
        Assert.IsNull(dematerialized.ParentId);
        var identityLinks = await _sp.GetRequiredService<IResourceSourceLinkService>()
            .GetByResourceIds([resource.Id]);
        Assert.IsTrue(identityLinks.Any(l => l.Source == ResourceSource.DLsite),
            "dematerialization keeps the resource's identity");

        var cacheAfterDematerialize = await ResourceService.GetResourceCache(resource.Id);
        AssertCacheIsCold(cacheAfterDematerialize, "after dematerialization");

        await Materialization.MaterializeAsync(resource.Id, secondPath, noMarkSync, CancellationToken.None);

        var cacheAfterRematerialize = await ResourceService.GetResourceCache(resource.Id);
        AssertCacheIsCold(cacheAfterRematerialize, "after re-materialization at a different path");
    }

    private static void AssertCacheIsCold(ResourceFileSystemCache? cache, string when)
    {
        if (cache == null)
        {
            return;
        }

        Assert.IsFalse(cache.CachedTypes.Contains(ResourceCacheType.Covers),
            $"covers must be re-discovered {when}");
        Assert.IsFalse(cache.CachedTypes.Contains(ResourceCacheType.PlayableFiles),
            $"playable files must be re-discovered {when}");
        Assert.IsTrue(cache.CoverPaths is null or { Count: 0 },
            $"cover paths from the old location must be dropped {when}");
        Assert.IsTrue(cache.PlayableFilePaths is null or { Count: 0 },
            $"playable file paths from the old location must be dropped {when}");
    }

    [TestMethod]
    public async Task SourceSync_NamesUndownloadedWorks_AndMaterializesThemWhenFilesAppear()
    {
        _resolver.Resources =
        [
            new ResolvedResource
            {
                Source = ResourceSource.DLsite,
                SourceKey = "RJ00000042",
                DisplayName = "A work nobody downloaded yet"
            }
        ];

        var syncService = _sp.GetRequiredService<ResourceSyncService>();
        await syncService.SyncResources(ResourceSource.DLsite, null, null, new PauseToken(),
            CancellationToken.None);

        var created = (await ResourceService.GetAll()).Single();
        Assert.IsFalse(created.HasLocalPath, "an undownloaded work has no local files");
        var names = await GetReservedValues(created.Id);
        Assert.AreEqual("A work nobody downloaded yet", names.FirstOrDefault(v => v.Name != null)?.Name,
            "a resource without a path still has to get its name — its source link is the only handle");

        // The user downloads it.
        var workPath = Path.Combine(_testRoot, "RJ00000042");
        Directory.CreateDirectory(workPath);
        _resolver.Resources[0].Path = workPath;

        var result = await syncService.SyncResources(ResourceSource.DLsite, null, null, new PauseToken(),
            CancellationToken.None);

        Assert.AreEqual(1, result.ResourcesMaterialized);
        Assert.AreEqual(0, result.ResourcesCreated, "the existing resource is materialized, not duplicated");

        var materialized = (await ResourceService.Get(created.Id))!;
        Assert.AreEqual(workPath.StandardizePath(), materialized.Path);
        Assert.IsFalse(materialized.IsFile);
        Assert.AreNotEqual(default, materialized.FileCreatedAt);
        Assert.AreEqual(new DirectoryInfo(workPath).LastWriteTime.TruncateToMilliseconds(),
            materialized.FileModifiedAt,
            "file times have to come from disk, not from the moment the row was written");
    }

    /// <summary>
    /// Nested and therefore not picked up by the resolver assembly scan, which only registers
    /// top-level public types.
    /// </summary>
    private class FakeDLsiteResolver : IResourceResolver
    {
        public List<ResolvedResource> Resources { get; set; } = [];

        public ResourceSource Source => ResourceSource.DLsite;

        public Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct) =>
            Task.FromResult(Resources);

        public ResolverConfigurationSchema GetConfigurationSchema() => new();

        public Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
            List<Resource> pathMarkResources, CancellationToken ct) =>
            Task.FromResult(new List<MigrationCandidate>());

        public Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys) =>
            Task.FromResult(Resources
                .Where(r => sourceKeys.Contains(r.SourceKey))
                .ToDictionary(r => r.SourceKey, r => r.DisplayName));
    }
}
