using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed partial class BaiduCloudDownloadProvider
{
    public async Task<CloudReadiness> InspectDestinationRevisionAsync(CloudDownloadRequest currentExecutionRequest,
        string newDirectory, CancellationToken ct)
    {
        var request = currentExecutionRequest;
        if (request.RemoteEntry is not {ProviderId: "baidu"} entry || entry.AccountId != request.AccountId ||
            string.IsNullOrWhiteSpace(entry.ManifestJson) || string.IsNullOrWhiteSpace(request.OperationId))
            return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
        try
        {
            // Account/profile read only. A directory revision does not authenticate again, inspect the
            // share over the network, rewrite a transfer journal or start the bridge/downloader.
            var profile = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
            using var manifest = JsonDocument.Parse(entry.ManifestJson);
            if (manifest.RootElement.ValueKind != JsonValueKind.Array || manifest.RootElement.GetArrayLength() is < 1 or > 10000)
                return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
            return InspectPathJournal(profile.Directory, request.OperationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN"); }
    }

    internal static CloudReadiness InspectPathJournal(string accountDirectory, string operationId)
    {
        // The bridge's path-map fingerprint includes its physical root. Keep every existing map;
        // absence is the only compatible empty-state proof for this provider's first revision.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationId))).ToLowerInvariant();
        var journal = Path.Combine(accountDirectory, "operations", key + ".paths-v1.json");
        ArtifactManifestFiles.RejectLinks(accountDirectory); ArtifactManifestFiles.RejectLinks(journal);
        if (!Directory.Exists(accountDirectory)) return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
        try
        {
            _ = File.GetAttributes(journal);
            return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT");
        }
        catch (FileNotFoundException) { return new(true); }
        catch (DirectoryNotFoundException) { return new(true); }
    }
}
