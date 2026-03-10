using Bakabase.Abstractions.Models.Db;

namespace Bakabase.Abstractions.Services;

public interface IExHentaiGalleryService
{
    Task<List<ExHentaiGalleryDbModel>> GetAll();
    Task<ExHentaiGalleryDbModel?> GetByGalleryId(long galleryId, string galleryToken);
    Task AddOrUpdate(ExHentaiGalleryDbModel gallery);
    Task AddOrUpdateRange(IEnumerable<ExHentaiGalleryDbModel> galleries);
    Task DeleteById(int id);
    Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task DeleteLocalFiles(long galleryId, string galleryToken);
    Task SetHidden(long galleryId, string galleryToken, bool isHidden);
}
