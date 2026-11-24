using Bakabase.Modules.Bittorrent.Models;

namespace Bakabase.Modules.Bittorrent.Abstractions;

public interface IBtDownloadManager
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BtDownloadJob>> ListAsync(CancellationToken ct = default);
    /// <summary>Already loaded runtime state only; never initializes or resumes a queue.</summary>
    Task<IReadOnlyList<BtDownloadJob>> ReadSnapshotAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BtDownloadJob>>([]);
    /// <summary>Active transport states and children of active parents, without file inventories or queue initialization.</summary>
    async Task<IReadOnlyList<BtDownloadJob>> ReadActiveStatusAsync(IReadOnlySet<int> activeParents, CancellationToken ct) =>
        (await ReadSnapshotAsync(ct)).Where(j => j.Status is BtDownloadStatus.Running or BtDownloadStatus.Queued or
            BtDownloadStatus.Stopping || j.DownloadTaskId is { } parent && activeParents.Contains(parent))
            .Select(j => j with {Files = []}).ToArray();
    Task<BtDownloadJob?> GetAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task<BtDownloadJob> EnsureQueuedAsync(BtDownloadRequest request, CancellationToken ct = default);
    Task<BtDownloadJob> StopAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob> ResumeAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob?> InspectLegacyWorkflowMagnetAsync(BtLegacyWorkflowMagnet dispatch, CancellationToken ct = default)
        => Task.FromResult<BtDownloadJob?>(null);
    Task<BtDownloadJob?> ResumeLegacyWorkflowMagnetAsync(BtLegacyWorkflowMagnet dispatch, CancellationToken ct = default)
        => Task.FromResult<BtDownloadJob?>(null);
    Task<BtDownloadJob> RecheckAsync(string id, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob> WaitForCompletionAsync(string id, Func<BtDownloadJob, Task>? progress = null,
        bool stopOnCancellation = false, CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
