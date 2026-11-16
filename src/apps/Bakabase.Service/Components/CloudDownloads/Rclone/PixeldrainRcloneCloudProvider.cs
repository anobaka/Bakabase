using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>
/// Public Pixeldrain filesystem /d locators only. The free /u and /l sharing API is
/// a different product and remains handled by its separate reviewed method.
/// </summary>
public sealed class PixeldrainRcloneCloudProvider(RcloneRemoteEngine engine, RcloneService dependency,
    RcloneDownloadSnapshotStore snapshots) : ICloudDownloadProvider
{
    public string ProviderId => "pixeldrain";
    public string MethodId => "pixeldrain.rclone";

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(accountId)) return new(false, "PIXELDRAIN_PUBLIC_DIRECTORY_ACCOUNT_NOT_REQUIRED");
        try { await dependency.EnsureReadyAsync(ct); return new(true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(false, "RCLONE_DEPENDENCY_REQUIRED"); }
    }

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        if (request.Source.ProviderId != ProviderId || request.Source.MethodId != MethodId || request.Source.Kind != "share" ||
            string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory) ||
            !string.IsNullOrEmpty(request.AccountId) || !string.IsNullOrEmpty(request.Source.AccountId) || request.RemoteEntry != null)
            throw new CloudProviderException("PIXELDRAIN_PUBLIC_DIRECTORY_REQUIRED");
        var locator = ParseLocator(request.Source.Url);
        try
        {
            var profile = await engine.EnsurePixeldrainPublicProfileAsync(locator.DirectoryId, ct);
            var version = await engine.GetProfileVersionAsync(profile, ct)
                ?? throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
            var destination = Path.GetFullPath(request.OutputDirectory);
            var identity = CloudOperationService.Key(CloudSourceIdentity.Key(request.Source), engine.IdentityPath(destination));
            var snapshot = await snapshots.GetOrCreateAsync(ProviderId, MethodId, request.OperationId, identity, version,
                token => engine.ListAsync(profile, locator.RemotePath, token, version), ct);
            var copied = await engine.CopyAsync(profile, locator.RemotePath, request.OutputDirectory, snapshot.Files,
                p => progress(new(p.TotalBytes == 0 ? null : Math.Min(100d, p.Bytes * 100d / p.TotalBytes),
                    p.Bytes, p.TotalBytes, "Downloading")).GetAwaiter().GetResult(), ct, version, request.WorkspaceDirectory);
            return new(copied.Files.Select(f => f.Path).ToArray());
        }
        catch (RcloneRemoteException error) { throw new CloudProviderException(error.Message); }
    }

    public sealed record Locator(string DirectoryId, string RemotePath);

    public static bool CanHandle(string url)
    {
        try { ParseLocator(url); return true; }
        catch (CloudProviderException) { return false; }
    }

    public static Locator ParseLocator(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Port != 443 || !CloudSourceClassifier.IsPixeldrainHost(uri.IdnHost) ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || url.Contains('\\'))
            throw new CloudProviderException("PIXELDRAIN_PUBLIC_DIRECTORY_REQUIRED");
        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
        if (parts.Length < 3 || parts[0] != "" || parts[1] != "d" ||
            !Regex.IsMatch(parts[2], @"\A[A-Za-z0-9_-]{1,128}\z") || parts[2] == "me")
            throw new CloudProviderException("PIXELDRAIN_PUBLIC_DIRECTORY_REQUIRED");
        try
        {
            // Uri normalizes dot segments. Reject them in the original locator before that
            // normalization can silently select a different directory/file than the user gave.
            var pathStart = url.IndexOf('/', url.IndexOf("://", StringComparison.Ordinal) + 3);
            if (pathStart < 0 || url[pathStart..].Split('/').Select(Uri.UnescapeDataString).Any(p => p is "." or ".."))
                throw new RcloneRemoteException("RCLONE_PATH_INVALID");
            var path = string.Join('/', parts.Skip(3).Select(Uri.UnescapeDataString));
            RcloneRemoteEngine.Remote(path);
            // Encoded slashes must not silently turn one URL segment into another path.
            if (parts.Skip(3).Any(p => Uri.UnescapeDataString(p).Contains('/')))
                throw new RcloneRemoteException("RCLONE_PATH_INVALID");
            return new(parts[2], path);
        }
        catch (Exception error) when (error is RcloneRemoteException or UriFormatException)
        { throw new CloudProviderException("PIXELDRAIN_PUBLIC_DIRECTORY_PATH_INVALID"); }
    }
}
