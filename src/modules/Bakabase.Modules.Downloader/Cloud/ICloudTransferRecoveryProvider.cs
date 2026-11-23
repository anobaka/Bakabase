namespace Bakabase.Modules.Downloader.Cloud;

public enum CloudTransferRecoveryDisposition
{
    Unavailable,
    Candidate,
    Reconcile,
    Retry,
    RequiresConfirmation,
    Authorized,
    FirstAttempt
}

public sealed record CloudTransferRecoveryCheck(CloudTransferRecoveryDisposition Disposition,
    string? Problem = null, CloudTransferReceipt? Receipt = null)
{
    /// <summary>The provider verified the previously issued operation's own journal, account and empty remote target.</summary>
    public bool VerifiedEmptyTarget { get; init; }
    /// <summary>Fresh isolation is bound to this exact next attempt in the recovery preview.</summary>
    public string? NextIsolatedOperationId { get; init; }
}

/// <summary>Read-only evidence for the original transfer request. Retry requires provider proof
/// that no transfer was attempted. RequiresConfirmation permits an isolated follow-up attempt
/// after approval without replaying or discarding the original operation.</summary>
public interface ICloudTransferRecoveryProvider
{
    bool IsTerminalTransferProblem(string problem) => false;

    Task<CloudTransferRecoveryCheck> InspectTransferRecoveryAsync(CloudTransferRequest request,
        bool verifyRemote, CancellationToken ct);
}
