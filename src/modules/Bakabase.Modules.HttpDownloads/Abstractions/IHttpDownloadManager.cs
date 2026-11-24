using Bakabase.Modules.HttpDownloads.Models;

namespace Bakabase.Modules.HttpDownloads.Abstractions;

public interface IHttpDownloadManager
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HttpDownloadJob>> ListAsync(CancellationToken ct = default);
    /// <summary>Already loaded runtime state only; never initializes or resumes a queue.</summary>
    Task<IReadOnlyList<HttpDownloadJob>> ReadSnapshotAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HttpDownloadJob>>([]);
    /// <summary>Active transport states and children of active parents, without file inventories or queue initialization.</summary>
    async Task<IReadOnlyList<HttpDownloadJob>> ReadActiveStatusAsync(IReadOnlySet<int> activeParents, CancellationToken ct) =>
        (await ReadSnapshotAsync(ct)).Where(j => j.Status is HttpDownloadStatus.Running or HttpDownloadStatus.Queued or
            HttpDownloadStatus.Stopping || j.DownloadTaskId is { } parent && activeParents.Contains(parent))
            .Select(j => j with {Files = []}).ToArray();
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
