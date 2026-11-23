namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Opt-in proof that an idle, payload-free download may change its physical destination.
/// The implementation must reject external checkpoints or path-bound journals and inspect account/remote
/// ownership without downloading, transferring, modifying credentials, or rewriting tool state.</summary>
public interface ICloudDownloadDestinationRevisionProvider
{
    Task<CloudReadiness> InspectDestinationRevisionAsync(CloudDownloadRequest currentExecutionRequest,
        string newDirectory, CancellationToken ct);
}

public interface ICloudDownloadSourceReuseProvider
{
    /// <summary>Inspects a stopped, read-only source and its provider-bound layout. Does not hash, copy, or download payloads.</summary>
    Task<CloudDownloadReuseInventory> InspectReuseSourceAsync(CloudDownloadRequest executionRequest, CancellationToken ct);
}

public sealed record CloudDownloadReuseEstimate(int FileCount, long TotalBytes, int CandidateFileCount, long CandidateBytes)
{
    public long RequiredDownloadBytes => TotalBytes - CandidateBytes;
}

public sealed record CloudDownloadReuseInventory(CloudDownloadReuseEstimate Estimate, string InventoryFingerprint);
public sealed record CloudDownloadReuseSource(string OperationId, string OutputDirectory, string InventoryFingerprint)
{
    /// <summary>When set, a provider may continue only from an already prepared destination. It must never inspect the source.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DisableSourceRead { get; init; }
}

public sealed record CloudDownloadDestinationRevision(string RevisionId, string PreviousDirectory,
    string Directory, string RootDirectory, DateTime AppliedAt, string PreviousFingerprint)
{
    /// <summary>Persist only the source snapshot identity. Source directory and provider scope follow the validated revision chain.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ReuseInventoryFingerprint { get; init; }
}

/// <summary>One resolver for physical placement; never change the persisted semantic input or operation key.</summary>
public static class CloudDownloadDestinations
{
    private static readonly StringComparison Paths = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string ExecutionDirectory(CloudDownloadRequest request)
    {
        Validate(request);
        return request.DestinationRevisions is {Count: > 0} revisions ? revisions[^1].Directory : request.OutputDirectory;
    }

    public static string ExecutionRoot(CloudDownloadRequest request, string semanticRoot)
    {
        Validate(request);
        return request.DestinationRevisions is {Count: > 0} revisions ? revisions[^1].RootDirectory : semanticRoot;
    }

    public static CloudDownloadRequest ExecutionRequest(CloudDownloadRequest request)
    {
        var directory = ExecutionDirectory(request);
        if (request.DestinationRevisions is not {Count: > 0} revisions) return request;
        var layout = request.OperationId;
        CloudDownloadReuseSource? source = null;
        foreach (var revision in revisions)
        {
            source = revision.ReuseInventoryFingerprint is { } fingerprint
                ? new(layout, revision.PreviousDirectory, fingerprint) : null;
            if (source != null) layout = request.OperationId + ":destination:" + revision.RevisionId;
        }
        return request with {OutputDirectory = directory, DestinationRevisions = null, ProviderLayoutId = layout, ReuseSource = source};
    }

    public static IEnumerable<CloudDownloadDestinationRevision> RetainedSources(CloudDownloadRequest request)
    {
        Validate(request);
        return request.DestinationRevisions?.Where(r => r.ReuseInventoryFingerprint != null) ?? [];
    }

    public static void Validate(CloudDownloadRequest request)
    {
        if (request.DestinationRevisions == null) return;
        if (request.DestinationRevisions.Count is < 1 or > 32)
            throw new InvalidDataException("DOWNLOAD_DESTINATION_HISTORY_INVALID");
        var previous = Canonical(request.OutputDirectory);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var revision in request.DestinationRevisions)
        {
            if (revision == null || revision.ReuseInventoryFingerprint is { } snapshot &&
                (snapshot.Length != 64 || snapshot.Any(c => !Uri.IsHexDigit(c))) || !Guid.TryParseExact(revision.RevisionId, "N", out _) || !ids.Add(revision.RevisionId) ||
                revision.PreviousFingerprint is not {Length: 64} || revision.PreviousFingerprint.Any(c => !Uri.IsHexDigit(c)) ||
                revision.AppliedAt == default || !Canonical(revision.PreviousDirectory).Equals(previous, Paths))
                throw new InvalidDataException("DOWNLOAD_DESTINATION_HISTORY_INVALID");
            var root = Canonical(revision.RootDirectory);
            var directory = Canonical(revision.Directory);
            if (directory.Equals(previous, Paths) || !directory.StartsWith(Path.EndsInDirectorySeparator(root)
                    ? root : root + Path.DirectorySeparatorChar, Paths))
                throw new InvalidDataException("DOWNLOAD_DESTINATION_HISTORY_INVALID");
            previous = directory;
        }
    }

    private static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("DOWNLOAD_DESTINATION_HISTORY_INVALID");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
