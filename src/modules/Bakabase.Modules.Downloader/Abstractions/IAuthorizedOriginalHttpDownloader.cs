using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>Provider-only transport. The private directory and the CDN/proxy-restricted client
/// are supplied by the registered provider, never by a public HTTP request.</summary>
public interface IAuthorizedOriginalHttpDownloader
{
    Task<AuthorizedOriginalCheckpoint> InspectAsync(AuthorizedOriginalIdentity identity,
        string checkpointDirectory, CancellationToken ct);

    /// <summary>Checks a fully transferred journal after the provider has independently verified
    /// its handoff receipt and final file. There must be no remaining private payload.</summary>
    Task<AuthorizedOriginalCheckpoint> InspectHandedOffAsync(AuthorizedOriginalIdentity identity,
        string checkpointDirectory, CancellationToken ct);

    /// <summary>Reads only a published local original. Verifies the entire journal and produces its
    /// whole-file digest in the same pass, without resolving a URL or advancing the checkpoint.</summary>
    Task<VerifiedOriginalPayload> VerifyCompletedAsync(AuthorizedOriginalIdentity identity,
        string checkpointDirectory, CancellationToken ct, Func<ValueTask>? onRead = null) =>
        throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_CHECKPOINT_INVALID");

    /// <summary>The resolver rechecks the original account, immutable file and length on every call.
    /// Its expiring address is used only in memory. Returns the private, verified payload for handoff.</summary>
    Task<string> DownloadAsync(AuthorizedOriginalIdentity identity, string checkpointDirectory,
        string httpClientName, Func<CancellationToken, Task<Uri>> resolveOriginal,
        Func<HttpTransferProgress, Task>? progress, CancellationToken ct);
}
