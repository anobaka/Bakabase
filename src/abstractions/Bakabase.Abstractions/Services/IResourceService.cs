using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Dto;
using Bootstrap.Models.ResponseModels;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Abstractions.Services;

public interface IResourceService
{
    // Task RemoveByMediaLibraryIdsNotIn(int[] ids);
    Task DeleteByKeys(int[] ids);

    // Task LogicallyRemoveByCategoryId(int categoryId);
    Task<List<Resource>> GetAll(Expression<Func<Models.Db.Resource, bool>>? selector = null,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    //
    // Task<List<Abstractions.Models.Db.Resource>> GetAll(Expression<Func<Abstractions.Models.Db.Resource, bool>> selector = null,
    //     bool asNoTracking = true);
    //
    Task<SearchResponse<Resource>> Search(ResourceSearchDto model, bool saveSearchCriteria, bool asNoTracking);

    // Task<Abstractions.Models.Db.Resource?> GetByKey(int id, bool asNoTracking);
    Task<Resource?> Get(int id, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    Task<List<Resource>> GetByKeys(int[] ids, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    //
    // Task<Resource> ToDomainModel(Abstractions.Models.Db.Resource resource,
    //     ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);
    //
    // Task<List<Resource>> ToDomainModel(Abstractions.Models.Db.Resource[] resources,
    //     ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);
    //
    Task<List<Abstractions.Models.Db.Resource>> GetAllDbModels(
        Expression<Func<Abstractions.Models.Db.Resource, bool>>? selector = null,
        bool asNoTracking = true);

    //
    /// <summary>
    /// <para>All properties of resources will be saved, including null values.</para>
    /// <para>Parents will be saved too, so be sure the properties of parent are fulfilled also.</para>
    /// </summary>
    /// <param name="resources"></param>
    /// <returns></returns>
    Task<List<DataChangeViewModel>> AddOrPutRange(List<Resource> resources);

    //
    // Task<(string Ext, Stream Stream)?> DiscoverAndPopulateCoverStream(int id, CancellationToken ct);
    Task<string[]> GetPlayableFiles(int id, CancellationToken ct);

    Task<bool> Any(Func<Models.Db.Resource, bool>? selector = null);

    Task<List<Models.Db.Resource>> AddAll(IEnumerable<Models.Db.Resource> resources);

    Task<BaseResponse> PutPropertyValue(int resourceId, ResourcePropertyValuePutInputModel model);

    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="path"></param>
    // /// <param name="ct"></param>
    // /// <param name="order"></param>
    // /// <param name="additionalSources"></param>
    // /// <returns>If ShouldSave is true, it usually means the cost of discovering cover is high, and we should save the result for better performance.</returns>
    // /// <exception cref="ArgumentOutOfRangeException"></exception>
    // Task<(Stream Stream, string Ext, bool ShouldSave)?> DiscoverCover(string path,
    //     CancellationToken ct, CoverSelectOrder order,
    //     AdditionalCoverDiscoveringSource[] additionalSources);
    //
    // Task<List<Resource>> GetNfoGenerationNeededResources(int[] resourceIds);
    // Task SaveNfo(Resource resource, bool overwrite, CancellationToken ct = new());
    // Task TryToGenerateNfoInBackground();
    // Task RunBatchSaveNfoBackgroundTask(int[] resourceIds, string backgroundTaskName, bool overwrite);
    // Task<BaseResponse> StartGeneratingNfo(BackgroundTask task);
    // Task PopulateStatistics(DashboardStatistics statistics);
    //
    Task<SingletonResponse<(string Path, byte[] Data)>> SaveCover(int id, bool overwrite, Func<byte[]> getImageData,
        CancellationToken ct);

    Task<(string Ext, Stream Stream)?> DiscoverCover(int id, CancellationToken ct);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="thumbnail"></param>
    /// <returns>File path</returns>
    Task<string?> GetCover(int id, bool thumbnail);

    Task<BaseResponse> Play(int resourceId, string file);
}