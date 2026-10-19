using Bakabase.Modules.Bittorrent.Models;

namespace Bakabase.Modules.Bittorrent.Abstractions;

public interface IBtDownloadManager
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BtDownloadJob>> ListAsync(CancellationToken ct = default);
    Task<BtDownloadJob?> GetAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task<BtDownloadJob> EnsureQueuedAsync(BtDownloadRequest request, CancellationToken ct = default);
    Task<BtDownloadJob> StopAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob> ResumeAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob> RecheckAsync(string id, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<BtDownloadJob> WaitForCompletionAsync(string id, Func<BtDownloadJob, Task>? progress = null,
        bool stopOnCancellation = false, CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
