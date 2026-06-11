using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Player.Tests.Helpers;

/// <summary>
/// In-memory stand-in for <see cref="IResourceService"/>. Each test seeds
/// resources and their playable file paths; only the members the batch-play
/// service touches are implemented, everything else throws so a future
/// caller change is loud.
/// </summary>
internal sealed class FakeResourceService : IResourceService
{
    private readonly Dictionary<int, Resource> _resources = [];
    private readonly Dictionary<int, List<string>> _playableFiles = [];

    public List<IReadOnlyDictionary<int, string>> MarkPlayedCalls { get; } = [];

    public void SetResource(Resource resource, params string[] playableFiles)
    {
        _resources[resource.Id] = resource;
        _playableFiles[resource.Id] = playableFiles.ToList();
    }

    public Task<List<Resource>> GetByKeys(int[] ids,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        => Task.FromResult(ids.Where(_resources.ContainsKey).Select(id => _resources[id]).ToList());

    public Task<List<PlayableItem>> DiscoverPlayableItems(int id, CancellationToken ct)
        => Task.FromResult((_playableFiles.GetValueOrDefault(id) ?? [])
            .Select(p => new PlayableItem { Origin = DataOrigin.FileSystem, Key = p })
            .ToList());

    public Task MarkPlayed(IReadOnlyDictionary<int, string> playedItemsByResourceId)
    {
        MarkPlayedCalls.Add(playedItemsByResourceId);
        return Task.CompletedTask;
    }

    // === Unused by batch play. ===
    public Task DeleteByKeys(int[] ids, bool deleteFiles = false) => throw new NotImplementedException();
    public Task<List<Resource>> GetAll(Expression<Func<ResourceDbModel, bool>>? selector = null,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None) => throw new NotImplementedException();
    public Task<SearchResponse<Resource>> Search(ResourceSearch model,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.All, bool asNoTracking = true)
        => throw new NotImplementedException();
    public Task<int[]> GetAllIds(ResourceSearch model) => throw new NotImplementedException();
    public Task<int[]> GetAllResourceIds() => throw new NotImplementedException();
    public Task<Resource?> Get(int id, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        => throw new NotImplementedException();
    public Task<List<ResourceDbModel>> GetAllDbModels(
        Expression<Func<ResourceDbModel, bool>>? selector = null, bool asNoTracking = true)
        => throw new NotImplementedException();
    public Task<List<DataChangeViewModel>> AddOrPutRange(List<Resource> resources)
        => throw new NotImplementedException();
    public Task RefreshParentTag() => throw new NotImplementedException();
    public Task<bool> Any(Func<ResourceDbModel, bool>? selector = null)
        => throw new NotImplementedException();
    public Task<List<ResourceDbModel>> AddAll(
        IEnumerable<ResourceDbModel> resources) => throw new NotImplementedException();
    public Task<BaseResponse> PutPropertyValue(int resourceId, ResourcePropertyValuePutInputModel model)
        => throw new NotImplementedException();
    public Task<BaseResponse> BulkPutPropertyValue(int[] resourceIds, ResourcePropertyValuePutInputModel model)
        => throw new NotImplementedException();
    public Task<BaseResponse> Play(int resourceId, string file) => throw new NotImplementedException();
    public Task<BaseResponse> PlayItem(int resourceId, DataOrigin origin, string key)
        => throw new NotImplementedException();
    public Task<BaseResponse> ChangeMediaLibrary(int[] ids, int mediaLibraryId,
        Dictionary<int, string>? newPaths = null) => throw new NotImplementedException();
    public Task<BaseResponse> ChangePath(int[] ids, Dictionary<int, string> newPaths)
        => throw new NotImplementedException();
    public Task Pin(int id, bool pin) => throw new NotImplementedException();
    public Task Transfer(ResourceTransferInputModel model) => throw new NotImplementedException();
    public Task SaveCover(int id, byte[] imageBytes, CoverSaveMode mode) => throw new NotImplementedException();
    public Task<CacheOverviewViewModel> GetCacheOverview() => throw new NotImplementedException();
    public Task<ResourceFileSystemCache?> GetResourceCache(int id) => throw new NotImplementedException();
    public Task InvalidateResourceCovers(int resourceId) => throw new NotImplementedException();
    public Task DeleteResourceCacheByResourceIdAndCacheType(int resourceId, ResourceCacheType type)
        => throw new NotImplementedException();
    public Task DeleteResourceCacheByMediaLibraryIdAndCacheType(int mediaLibraryId, ResourceCacheType type)
        => throw new NotImplementedException();
    public Task DeleteResourceCacheByResourceIdsAndCacheType(IEnumerable<int> resourceIds, ResourceCacheType type)
        => throw new NotImplementedException();
    public Task DeleteUnassociatedResourceCacheByCacheType(ResourceCacheType type)
        => throw new NotImplementedException();
    public Task<ResourceFileSystemCache?> RefreshResourceCache(int resourceId, CancellationToken ct)
        => throw new NotImplementedException();
    public Task RefreshResourcesCache(IReadOnlyCollection<int> resourceIds, Func<int, string?, Task>? onProgress,
        CancellationToken ct) => throw new NotImplementedException();
    public Task MarkAsNotPlayed(int id) => throw new NotImplementedException();
    public Task<Resource[]> GetAllGeneratedByMediaLibraryV2(int[]? ids = null,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None) => throw new NotImplementedException();
    public Task<List<Resource>> GetByMediaLibraryId(int mediaLibraryId,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None) => throw new NotImplementedException();
    public Task<BaseResponse> SetMediaLibraries(int[] resourceIds, int[] mediaLibraryIds)
        => throw new NotImplementedException();
    public Task<List<int>> GetConflictingResourceIds(int resourceId) => throw new NotImplementedException();
    public Task MergeResources(ResourceMergeInputModel model) => throw new NotImplementedException();
    public ResourceDisplayNameViewModel.Segment[] BuildDisplayNameSegmentsForResource(Resource resource,
        string template, (string Left, string Right)[] wrappers) => throw new NotImplementedException();
    public string BuildDisplayNameForResource(Resource resource, string template,
        (string Left, string Right)[] wrappers) => throw new NotImplementedException();
    public Task<BaseResponse> PlayRandomResource() => throw new NotImplementedException();
    public Task<(List<Resource> Ancestors, int ChildrenCount)> GetHierarchyContext(int resourceId)
        => throw new NotImplementedException();
}
