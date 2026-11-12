namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Resolve local account references without exposing credentials or authorizing remotely.
/// A configuration generation pins execution and artifact reuse to the account the user reviewed.</summary>
public interface ICloudDownloadAccountResolver
{
    string AccountProvider { get; }
    Task<CloudAccountReference> ResolveAccountAsync(string? accountId, string? accountVersion, CancellationToken ct);
}

public sealed record CloudAccountReference(string AccountId, string AccountVersion);
