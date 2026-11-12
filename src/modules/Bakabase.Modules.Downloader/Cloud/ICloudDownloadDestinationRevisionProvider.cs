namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Opt-in proof that an idle, payload-free download may change its physical destination.
/// The implementation must reject external checkpoints or path-bound journals and inspect account/remote
/// ownership without downloading, transferring, modifying credentials, or rewriting tool state.</summary>
public interface ICloudDownloadDestinationRevisionProvider
{
    Task<CloudReadiness> InspectDestinationRevisionAsync(CloudDownloadRequest currentExecutionRequest,
        string newDirectory, CancellationToken ct);
}

public sealed record CloudDownloadDestinationRevision(string RevisionId, string PreviousDirectory,
    string Directory, string RootDirectory, DateTime AppliedAt, string PreviousFingerprint);

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

    public static CloudDownloadRequest ExecutionRequest(CloudDownloadRequest request) =>
        request with {OutputDirectory = ExecutionDirectory(request), DestinationRevisions = null};

    public static void Validate(CloudDownloadRequest request)
    {
        if (request.DestinationRevisions == null) return;
        if (request.DestinationRevisions.Count is < 1 or > 32)
            throw new InvalidDataException("DOWNLOAD_DESTINATION_HISTORY_INVALID");
        var previous = Canonical(request.OutputDirectory);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var revision in request.DestinationRevisions)
        {
            if (!Guid.TryParseExact(revision.RevisionId, "N", out _) || !ids.Add(revision.RevisionId) ||
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
