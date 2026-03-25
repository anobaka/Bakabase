using Bakabase.Abstractions.Models.Db;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

public interface IExHentaiGalleryService
{
    Task<List<ExHentaiGalleryDbModel>> GetAll();
    Task<SearchResponse<ExHentaiGalleryDbModel>> Search(string? keyword, int pageIndex, int pageSize);
    Task<ExHentaiGalleryDbModel?> GetByGalleryId(long galleryId, string galleryToken);
    Task AddOrUpdate(ExHentaiGalleryDbModel gallery);
    Task AddOrUpdateRange(IEnumerable<ExHentaiGalleryDbModel> galleries);
    Task DeleteById(int id);
    Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task DeleteLocalFiles(long galleryId, string galleryToken);
    Task SetHidden(long galleryId, string galleryToken, bool isHidden);

    /// <summary>
    /// Clear MetadataJson and MetadataFetchedAt for all items, triggering re-fetch on next metadata task run.
    /// </summary>
    Task ClearAllMetadata();
}
