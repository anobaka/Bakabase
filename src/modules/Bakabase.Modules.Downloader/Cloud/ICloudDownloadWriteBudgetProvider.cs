namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>A conservative upper bound for new writes, never a count of remaining network bytes.</summary>
public sealed record CloudDownloadWriteBudget(string Destination, long MaximumAdditionalBytes);

/// <summary>The exact per-invocation request whose pinned manifest establishes these write budgets.</summary>
public sealed record CloudDownloadWritePlan(CloudDownloadRequest Request, IReadOnlyList<CloudDownloadWriteBudget> Budgets);

/// <summary>
/// Optional read-only preflight. A known budget must cover all payload and staging writes to each
/// destination, including a possible rewrite of partial files. Null means the size is unknown;
/// it must not be interpreted as an empty reservation or derived from progress telemetry.
/// </summary>
public interface ICloudDownloadWriteBudgetProvider
{
    Task<CloudDownloadWritePlan?> PrepareWriteAsync(
        CloudDownloadRequest request, CancellationToken ct);

    /// <summary>When the full budget cannot fit, optionally prove existing immutable members and
    /// prevent their rewrite during this invocation. The queued request stays unchanged.</summary>
    Task<CloudDownloadWritePlan?> TryPrepareRetainedWriteAsync(CloudDownloadRequest pinnedRequest,
        Func<Task> verificationActivity, CancellationToken ct) => Task.FromResult<CloudDownloadWritePlan?>(null);
}
