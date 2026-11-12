using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

public sealed class RclonePikPakCloudProvider(RcloneRemoteEngine engine, RcloneAccountStore accounts, RcloneDownloadSnapshotStore snapshots)
    : RcloneAccountCloudProvider("pikpak", "pikpak.rclone", engine, accounts, snapshots);

public sealed class RcloneOneDriveCloudProvider(RcloneRemoteEngine engine, RcloneAccountStore accounts, RcloneDownloadSnapshotStore snapshots)
    : RcloneAccountCloudProvider("oneDrive", "oneDrive.rclone", engine, accounts, snapshots);

public sealed class RclonePixeldrainAccountCloudProvider(RcloneRemoteEngine engine, RcloneAccountStore accounts, RcloneDownloadSnapshotStore snapshots)
    : RcloneAccountCloudProvider("pixeldrain", "pixeldrain.rcloneRemote", engine, accounts, snapshots);

/// <summary>A site-independent account-path adapter. rclone owns backend protocols and OAuth refresh.</summary>
public abstract class RcloneAccountCloudProvider(string providerId, string methodId, RcloneRemoteEngine engine,
    RcloneAccountStore accounts, RcloneDownloadSnapshotStore snapshots) : ICloudDownloadProvider
{
    public string ProviderId => providerId;
    public string MethodId => methodId;

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        try
        {
            var id = await accounts.ResolveAccountIdAsync(ProviderId, accountId, ct);
            _ = await accounts.GetConfiguredProfileVersionAsync(ProviderId, id, ct);
            return new(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { return new(false, error is RcloneRemoteException ? error.Message : "Choose a configured account for this source."); }
    }

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        var source = request.Source;
        if (source.ProviderId != ProviderId || source.Kind != "remote" ||
            source.MethodId != MethodId || string.IsNullOrWhiteSpace(source.AccountVersion) ||
            source.AccountId != request.AccountId || string.IsNullOrWhiteSpace(request.OperationId) ||
            !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new ArgumentException("Choose this method with a configured account and an account-internal path.");
        DownloadSourceResolver.Normalize(new() {Id = source.Id, Url = source.Url, Kind = source.Kind,
            ProviderId = ProviderId, MethodId = MethodId, AccountId = source.AccountId});
        var id = await accounts.ResolveAccountIdAsync(ProviderId, request.AccountId, ct);
        var profile = new RcloneProfileRef(ProviderId, id);
        if (await accounts.GetConfiguredProfileVersionAsync(ProviderId, id, ct) != source.AccountVersion)
            throw new CloudProviderException("The account configuration changed. Start a new download using the configured account.");
        if (request.RemoteEntry != null && (request.RemoteEntry.ProviderId != ProviderId ||
            request.RemoteEntry.AccountId != id || request.RemoteEntry.Path != source.Url))
            throw new ArgumentException("The remote entry does not match the selected account path.");
        try
        {
            var destination = Path.GetFullPath(request.OutputDirectory);
            var identity = CloudOperationService.Key(CloudSourceIdentity.Key(source, id), engine.IdentityPath(destination));
            var snapshot = await snapshots.GetOrCreateAsync(ProviderId, MethodId, request.OperationId, identity,
                source.AccountVersion, async token =>
            {
                await progress(new(null, Stage: "Listing account files with rclone"));
                var files = await engine.ListAsync(profile, source.Url, token, expectedProfileVersion: source.AccountVersion);
                if (files.Count == 0) throw new CloudProviderException("This account path has no downloadable files. Empty folders are not downloaded.");
                return files;
            }, ct);
            // Reconfiguration invalidates both listing and transport identity. The engine also
            // checks the exact remote manifest before and after transfer and never overwrites a conflict.
            if (await engine.GetProfileVersionAsync(profile, ct) != source.AccountVersion)
                throw new CloudProviderException("The account configuration changed during discovery. Start a new run.");
            var result = await engine.CopyAsync(profile, source.Url, request.OutputDirectory, snapshot.Files,
                p => progress(new(p.TotalBytes > 0 ? 100d * p.Bytes / p.TotalBytes : null,
                    p.Bytes, p.TotalBytes, "Downloading original files with rclone")).GetAwaiter().GetResult(), ct,
                expectedProfileVersion: source.AccountVersion, workspaceDirectory: request.WorkspaceDirectory);
            CloudDownloadManifest.Validate(request.OutputDirectory, result.Files.Select(f => f.Path).ToList());
            await progress(new(100, Stage: result.Reused ? "Reused verified rclone files" : "rclone download complete"));
            return new(result.Files.Select(f => f.Path).ToList());
        }
        catch (RcloneRemoteException error) {throw new CloudProviderException(error.Message);}
    }
}
