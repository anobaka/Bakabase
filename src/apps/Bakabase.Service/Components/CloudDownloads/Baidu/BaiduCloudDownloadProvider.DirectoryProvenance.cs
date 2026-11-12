using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

internal sealed class BaiduDownloadDirectoryProvenanceProvider(BaiduPcsAccountStore accounts)
    : ICloudDownloadDirectoryProvenanceProvider
{
    public async Task<CloudDownloadDirectoryEvidence?> InspectDirectoryEvidenceAsync(CloudDownloadRequest request,
        string artifactRoot, CancellationToken ct)
    {
        if (request.Source.ProviderId != "baidu" || request.RemoteEntry is not {OwnedContainer: not null} entry ||
            entry.AccountId != request.AccountId) return null;
        try
        {
            var profile = await accounts.ReadInspectionProfileAsync(entry.AccountId, ct);
            return await BaiduDownloadDirectoryEvidence.ReadAsync(profile.Directory, profile.Account.Uid!, request, artifactRoot, ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return null; }
    }

}

public sealed partial class BaiduCloudDownloadProvider
{
    internal static async Task<CloudDownloadDirectoryEvidence?> ReadDirectoryEvidenceAsync(string accountDirectory,
        CloudRemoteEntry entry, string artifactRoot, CancellationToken ct)
    {
        var owned = await BaiduOwnedDownloadRoot.ResolveAsync(accountDirectory, entry, artifactRoot, ct);
        if (owned == null) return null;
        try
        {
            var relative = Path.GetRelativePath(artifactRoot, owned).Replace('\\', '/');
            var parent = entry.Path[..(entry.Path.LastIndexOf('/') + 1)];
            using var document = JsonDocument.Parse(entry.ManifestJson!);
            var files = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var member in document.RootElement.EnumerateArray())
            {
                var path = member.GetProperty("path").GetString();
                if (path == null || !path.StartsWith(parent, StringComparison.Ordinal)) return null;
                if (member.GetProperty("isDirectory").GetBoolean()) continue;
                var size = member.GetProperty("size").GetInt64();
                if (size < 0 || !files.TryAdd(path[parent.Length..], size)) return null;
            }
            return files.Count == 0 ? null : new(relative, files);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        { return null; }
    }
}
