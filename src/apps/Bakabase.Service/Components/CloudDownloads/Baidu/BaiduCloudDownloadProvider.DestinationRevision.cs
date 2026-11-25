using System;
using System.IO;
using System.Linq;
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
            return InspectPathJournal(profile.Directory, request.LayoutOperationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN"); }
    }

    public async Task<CloudDownloadReuseInventory> InspectReuseSourceAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        if (request.RemoteEntry is not {ProviderId: "baidu", ManifestJson: not null} entry ||
            entry.AccountId != request.AccountId || string.IsNullOrEmpty(request.OperationId))
            throw new CloudProviderException("DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
        var profile = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        JsonElement result;
        try
        {
            result = await bridge.ExecuteAsync(profile.Directory, new
            {
                command = "inspect-reuse-source", operationId = request.LayoutOperationId, expectedUid = profile.Account.Uid,
                request.OutputDirectory, remotePath = entry.Path, expectedManifest = JsonSerializer.Deserialize<JsonElement>(entry.ManifestJson)
            }, null, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new CloudProviderException("DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE"); }
        var latest = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
        if (latest.Account.Uid != profile.Account.Uid || result.GetProperty("uid").GetString() != profile.Account.Uid)
            throw new CloudProviderException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
        using var expected = JsonDocument.Parse(entry.ManifestJson);
        var files = expected.RootElement.EnumerateArray().Where(e => !e.GetProperty("isDirectory").GetBoolean()).ToArray();
        var estimate = result.Deserialize<CloudDownloadReuseEstimate>(BaiduPcsBridgeClient.Json);
        var fingerprint = result.GetProperty("inventoryFingerprint").GetString();
        if (estimate == null || files.Length == 0 || files.Length > 10000 || estimate.FileCount != files.Length ||
            estimate.TotalBytes < 0 || estimate.TotalBytes != files.Sum(e => e.GetProperty("size").GetInt64()) ||
            estimate.CandidateFileCount < 0 || estimate.CandidateFileCount > files.Length ||
            estimate.CandidateBytes < 0 || estimate.CandidateBytes > estimate.TotalBytes ||
            fingerprint is not {Length: 64} || fingerprint.Any(c => !Uri.IsHexDigit(c)))
            throw new CloudProviderException("DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE");
        return new(estimate, fingerprint);
    }

    internal static CloudReadiness InspectPathJournal(string accountDirectory, string operationId)
    {
        // The bridge may persist a path map, reuse receipt or its operation lock before writing payload files.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationId))).ToLowerInvariant();
        ArtifactManifestFiles.RejectLinks(accountDirectory);
        if (!Directory.Exists(accountDirectory)) return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
        foreach (var suffix in new[] {".paths-v1.json", ".reuse-v1.json", ".reuse-v1.json.lock"})
        {
            var journal = Path.Combine(accountDirectory, "operations", key + suffix);
            ArtifactManifestFiles.RejectLinks(journal);
            try
            {
                _ = File.GetAttributes(journal);
                return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return new(true);
    }
}
