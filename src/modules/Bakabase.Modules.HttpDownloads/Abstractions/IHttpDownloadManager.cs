using Bakabase.Modules.HttpDownloads.Models;

namespace Bakabase.Modules.HttpDownloads.Abstractions;

public interface IHttpDownloadManager
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HttpDownloadJob>> ListAsync(CancellationToken ct = default);
    Task<HttpDownloadJob?> GetAsync(string id, CancellationToken ct = default);
    Task<HttpDownloadJob?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task<HttpDownloadJob> EnsureQueuedAsync(HttpDownloadRequest request, CancellationToken ct = default);
    Task<HttpDownloadJob> StopAsync(string id, CancellationToken ct = default);
    Task<HttpDownloadJob> ResumeAsync(string id, CancellationToken ct = default);
    Task<HttpDownloadJob> RecheckAsync(string id, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<HttpDownloadJob> WaitForCompletionAsync(string id, Func<HttpDownloadJob, Task>? progress = null,
        bool stopOnCancellation = false, CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
