namespace Bakabase.Modules.Downloader.Cloud;

public static class SourceAvailabilityStatuses
{
    public const string Available = "available";
    public const string ConfirmedUnavailable = "confirmedUnavailable";
    public const string AccessRequired = "accessRequired";
    public const string TemporarilyUnavailable = "temporarilyUnavailable";
    public const string Unknown = "unknown";
    public const string Unsupported = "unsupported";
}

/// <summary>A time-bounded, scoped observation, never a download success or permission to delete files.</summary>
public sealed record SourceAvailabilityResult
{
    public required string Status { get; init; }
    public required string ReasonCode { get; init; }
    public required string ProbeId { get; init; }
    public int ProbeVersion { get; init; } = 1;
    /// <summary>Allowlisted status codes/counts only. No provider HTML, URLs, credentials or exception messages.</summary>
    public Dictionary<string, string> Evidence { get; init; } = [];
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Metadata only: implementations must never purchase, transfer, create registered user accounts or
/// download payloads. Provider adapters may separately prepare a reusable anonymous transport session when
/// the public website requires one; it must not use or change the user's signed-in account.</summary>
public interface ISourceAvailabilityProbe
{
    /// <summary>Bump when classification or evidence semantics change; observations must not outlive that change.</summary>
    int CacheVersion => 1;
    /// <summary>Authenticated probes must opt in so anonymous observations cannot authorize or block another account scope.</summary>
    bool UsesAccountScope => false;
    bool CanHandle(CloudSource source);
    Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct);
}
