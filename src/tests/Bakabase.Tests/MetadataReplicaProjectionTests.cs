using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

public sealed partial class ResourceSearchIndexIncrementalTests
{
    private MetadataReplicaProjection Projection(IResourceService? resourceService = null,
        IMediaLibraryV2Service? libraries = null, ICollectionService? collections = null) => new(
        resourceService ?? _sp.GetRequiredService<IResourceService>(),
        _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>(),
        IndexExporter(), _sp.GetRequiredService<IPropertyService>(),
        _sp.GetRequiredService<ICustomPropertyService>(), libraries ?? _sp.GetRequiredService<IMediaLibraryV2Service>(),
        collections ?? _sp.GetRequiredService<ICollectionService>(), _sp.GetRequiredService<IExtensionGroupService>());

    [TestMethod]
    public async Task ExportProjection_ReturnsOrdinaryResourceMetadataWithSortAndTypedPostingsWithoutPlayableDiscovery()
    {
        await Seed("MiXeD");
        var id = await ResourceId("MiXeD");
        var number = await AddMemoryTestProperty(PropertyType.Number);
        await _sp.GetRequiredService<ICustomPropertyValueService>().AddRange([MemoryTestValue(id, number.Id, 3.75m)]);
        var links = _sp.GetRequiredService<IResourceSourceLinkService>();
        var link = (await links.GetByResourceId(id)).First();
        link.MetadataJson = "{\"authorization\":\"source-secret-canary\"}";
        await links.Update(link);
        await BuildIndex();
        var resourceService = DispatchProxy.Create<IResourceService, ProjectionResourceSpy>();
        var spy = (ProjectionResourceSpy)(object)resourceService;
        spy.Inner = _sp.GetRequiredService<IResourceService>();

        var exported = (await Projection(resourceService).ReadResourcesAsync([id], 17)).Single();
        var json = JObject.Parse(exported.ResourceJson);
        Assert.AreEqual(id, (int)json["id"]!);
        Assert.AreEqual(17L, exported.Revision);
        Assert.AreEqual("MiXeD", exported.Sort.FileName);
        Assert.AreEqual("MiXeD", (string)json["fileName"]!);
        Assert.IsNotNull(json["properties"]);
        Assert.IsTrue(exported.Postings.Any(entry => entry.Pool == PropertyPool.Custom &&
            entry.PropertyId == number.Id && Equals(entry.RangeValue?.ToComparable(), 3.75m) && entry.ValueKey == null));
        Assert.IsTrue(exported.Postings.Any(entry => entry.PropertyId == (int)InternalProperty.Filename &&
            entry.Pool == PropertyPool.Internal && entry.ValueKey == "mixed" && entry.RangeValue == null));
        Assert.AreEqual(ResourceAdditionalItem.All & ~ResourceAdditionalItem.PlayableItem, spy.ReadItems);
        Assert.IsFalse(exported.ResourceJson.Contains("source-secret-canary", StringComparison.Ordinal));
        Assert.AreEqual(link.MetadataJson, (await links.GetByResourceId(id)).First().MetadataJson,
            "Projection must not scrub the source's own stored metadata.");
    }

    [TestMethod]
    public async Task ExportProjection_CatalogContainsOnlyExplicitMetadataAndKeepsOwnerLocalKeys()
    {
        var property = await AddMemoryTestProperty(PropertyType.SingleLineText);
        var libraryService = DispatchProxy.Create<IMediaLibraryV2Service, ProjectionCatalogStub>();
        ((ProjectionCatalogStub)(object)libraryService).Result = Task.FromResult(new List<MediaLibraryV2>
        {
            new() { Id = 71, Name = "Catalog library", Color = "red", ResourceCount = 3,
                Players = [new() { ExecutablePath = "player-secret-canary.exe", Command = "secret-command" }],
                Paths = ["mount-secret-canary"], SyncVersion = "sync-secret-canary" }
        });
        var collectionService = DispatchProxy.Create<ICollectionService, ProjectionCatalogStub>();
        ((ProjectionCatalogStub)(object)collectionService).Result = Task.FromResult(new List<ResourceCollection>
        {
            new() { Id = 72, Name = "Catalog collection", RuleSearchJson = "{\"keyword\":\"book\"}",
                AutoAcquire = true, AcquisitionSettingsJson = "acquisition-secret-canary" }
        });
        var catalog = new List<ReplicaCatalog>();
        await foreach (var item in Projection(libraries: libraryService, collections: collectionService).ReadCatalogAsync())
            catalog.Add(item);

        Assert.IsTrue(catalog.Any(item => item.Kind == "properties" && item.Key == $"{(int)PropertyPool.Custom}:{property.Id}"));
        Assert.IsTrue(catalog.Any(item => item.Kind == "customProperties" && item.Key == property.Id.ToString()));
        var library = JObject.Parse(catalog.Single(item => item.Kind == "mediaLibraries" && item.Key == "71").Json);
        Assert.AreEqual("Catalog library", (string)library["name"]!);
        Assert.AreEqual(3, (int)library["resourceCount"]!);
        Assert.IsNull(library["players"]);
        Assert.IsNull(library["paths"]);
        var collection = JObject.Parse(catalog.Single(item => item.Kind == "collections" && item.Key == "72").Json);
        Assert.IsNotNull(collection["ruleSearchJson"]);
        Assert.IsNull(collection["acquisitionSettingsJson"]);
        Assert.IsNull(collection["autoAcquire"]);
        Assert.IsFalse(string.Join("\n", catalog.Select(item => item.Json)).Contains("secret-canary", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExportProjection_RejectsOversizedCatalogAndCancelledReads()
    {
        var libraryService = DispatchProxy.Create<IMediaLibraryV2Service, ProjectionCatalogStub>();
        ((ProjectionCatalogStub)(object)libraryService).Result = Task.FromResult(new List<MediaLibraryV2>
        {
            new() { Id = 1, Name = new string('x', MetadataReplicaProjection.MaximumBatchBytes + 1) }
        });
        await Assert.ThrowsExceptionAsync<ReplicaBudgetExceededException>(async () =>
        {
            await foreach (var _ in Projection(libraries: libraryService).ReadCatalogAsync()) { }
        });
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            Projection().ReadResourcesAsync([1], 0, new CancellationToken(true)));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Projection().ReadCatalogAsync(new CancellationToken(true))) { }
        });
    }

    [TestMethod]
    public async Task ExportProjection_RebuildsDetachedPostingsWhenTheGlobalIndexHasNotObservedTheResource()
    {
        await Seed("existing");
        var id = await ResourceId("existing");
        await BuildIndex();
        Index().RemoveResource(id);
        await Index().WaitForPendingUpdatesAsync(default);
        var projected = (await Projection().ReadResourcesAsync([id], 1)).Single();
        Assert.IsTrue(projected.Postings.Any(p => p.Pool == PropertyPool.Internal &&
            p.PropertyId == (int)InternalProperty.Filename && p.ValueKey == "existing"));
        Assert.AreEqual(0, (await IndexExporter().ReadIndexEntriesAsync([id])).Count,
            "A detached export must not publish into the source's asynchronous index.");
    }

    public class ProjectionResourceSpy : DispatchProxy
    {
        public IResourceService Inner = null!;
        public ResourceAdditionalItem? ReadItems;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IResourceService.GetByKeys)) ReadItems = (ResourceAdditionalItem)args![1]!;
            return method.Invoke(Inner, args);
        }
    }

    public class ProjectionCatalogStub : DispatchProxy
    {
        public object Result = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "GetAll" ? Result : throw new InvalidOperationException("Unexpected catalog method: " + method.Name);
    }
}
