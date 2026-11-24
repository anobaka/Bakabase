namespace Bakabase.Modules.Downloader.Models;

/// <summary>AccountVersion includes account ID and generation. AttemptScope is stable across
/// retries of the same authorized operation. Keys contain no link or credential.</summary>
public sealed record AuthorizedOriginalIdentity(string Provider, string OperationId, string AccountVersion,
    string FolderId, string MemberId, string ImmutableId, long Length, string AttemptScope);

/// <summary>Bytes acknowledged by the durable segment journal. Transfer rehashes them before reuse.</summary>
public sealed record AuthorizedOriginalCheckpoint(bool Exists, long VerifiedBytes, bool IsComplete);

/// <summary>Length and whole-file SHA-256 from the same verified read of this local payload.</summary>
public sealed record VerifiedOriginalPayload(string Path, long Length, string Sha256);

public sealed class AuthorizedOriginalDownloadException(string code) : IOException(code switch
{
    "ORIGINAL_HTTP_RESUME_UNSUPPORTED" or "ORIGINAL_HTTP_SOURCE_CHANGED" or "ORIGINAL_HTTP_RANGE_INVALID" or
    "ORIGINAL_HTTP_RATE_LIMITED" or "ORIGINAL_HTTP_CHECKPOINT_INVALID" or "ORIGINAL_HTTP_BUSY" or "ORIGINAL_HTTP_LOCATION_UNAVAILABLE" => code,
    _ => "ORIGINAL_HTTP_TRANSFER_FAILED"
})
{
    public string Code => Message;
}
