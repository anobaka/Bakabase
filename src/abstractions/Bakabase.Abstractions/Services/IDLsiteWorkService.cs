using Bakabase.Abstractions.Models.Db;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

public interface IDLsiteWorkService
{
    Task<List<DLsiteWorkDbModel>> GetAll();
    Task<SearchResponse<DLsiteWorkDbModel>> Search(string? keyword, bool showHidden, int pageIndex, int pageSize);
    Task<DLsiteWorkDbModel?> GetByWorkId(string workId);
    Task<List<DLsiteWorkDbModel>> GetByWorkIds(IEnumerable<string> workIds);
    Task AddOrUpdate(DLsiteWorkDbModel work);
    Task AddOrUpdateRange(IEnumerable<DLsiteWorkDbModel> works);
    Task DeleteByWorkId(string workId);
    Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task DownloadWork(string workId, Func<int, string, Task>? onProgress = null, CancellationToken ct = default);
    Task ExtractWork(string workId, Func<int, string, Task>? onProgress = null, CancellationToken ct = default);
    Task<string> PrepareDownloadDirectory(string workId);
    Task LaunchWork(string workId, CancellationToken ct = default);
    List<string> FindPlayableFiles(string localPath, string? workType);
    Task<int> ScanFolder(string folderPath, Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task<int> ScanConfiguredFolders(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task SetHidden(string workId, bool isHidden);
    Task SetUseLocaleEmulator(string workId, bool useLocaleEmulator);
    Task<string?> FetchDrmKey(string workId, CancellationToken ct = default);
    Task DeleteLocalFiles(string workId);

    /// <summary>
    /// Clear MetadataJson and MetadataFetchedAt for all items, triggering re-fetch on next metadata task run.
    /// </summary>
    Task ClearAllMetadata();
}
