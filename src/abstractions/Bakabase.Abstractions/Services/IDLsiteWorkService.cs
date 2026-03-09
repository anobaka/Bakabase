using Bakabase.Abstractions.Models.Db;

namespace Bakabase.Abstractions.Services;

public interface IDLsiteWorkService
{
    Task<List<DLsiteWorkDbModel>> GetAll();
    Task<DLsiteWorkDbModel?> GetByWorkId(string workId);
    Task<List<DLsiteWorkDbModel>> GetByWorkIds(IEnumerable<string> workIds);
    Task AddOrUpdate(DLsiteWorkDbModel work);
    Task AddOrUpdateRange(IEnumerable<DLsiteWorkDbModel> works);
    Task DeleteByWorkId(string workId);
    Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task DownloadWork(string workId, Func<int, string, Task>? onProgress = null, CancellationToken ct = default);
    Task<string> PrepareDownloadDirectory(string workId);
    Task LaunchWork(string workId, CancellationToken ct = default);
    List<string> FindPlayableFiles(string localPath, string? workType);
    Task<int> ScanFolder(string folderPath, Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task<int> ScanConfiguredFolders(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task SetHidden(string workId, bool isHidden);
    Task<string?> FetchDrmKey(string workId, CancellationToken ct = default);
    Task DeleteLocalFiles(string workId);
}
