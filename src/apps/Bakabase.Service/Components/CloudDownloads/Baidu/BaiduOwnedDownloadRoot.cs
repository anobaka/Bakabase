using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

/// <summary>Only the account's completed transfer journal can prove that a remote container belongs to Bakabase.</summary>
internal static class BaiduOwnedDownloadRoot
{
    public static async Task<string?> ResolveAsync(string accountDirectory, CloudRemoteEntry entry,
        string outputDirectory, CancellationToken ct)
    {
        var owner = entry.OwnedContainer;
        if (entry.ProviderId != "baidu" || string.IsNullOrWhiteSpace(entry.Path) || owner == null || string.IsNullOrWhiteSpace(owner.TransferOperationId) ||
            owner.TransferOperationId.Length > 4096 || owner.Path != entry.Path || string.IsNullOrWhiteSpace(entry.ManifestJson))
            return null;
        try
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.TransferOperationId))).ToLowerInvariant();
            var segments = entry.Path.Split('/');
            // This equality corroborates the journal's operation identity; matching a name alone is never enough.
            if (!entry.Path.StartsWith('/') || entry.Path.Contains('\\') ||
                segments.Skip(1).Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..") ||
                segments[^1] != "bakabase-" + key[..24]) return null;
            var journal = new FileInfo(Path.Combine(accountDirectory, "operations", key + ".json"));
            if (!journal.Exists || journal.Length > 32L * 1024 * 1024 || journal.LinkTarget != null ||
                journal.Directory?.LinkTarget != null) return null;
            ArtifactManifestFiles.RejectLinks(journal.FullName);
            using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(journal.FullName, ct));
            var receipt = parsed.RootElement;
            if (receipt.GetProperty("stage").GetString() != "completed" ||
                receipt.GetProperty("remotePath").GetString() != entry.Path) return null;
            var manifest = receipt.GetProperty("manifest");
            if (manifest.ValueKind != JsonValueKind.Array || manifest.GetArrayLength() == 0 ||
                !JsonNode.DeepEquals(JsonNode.Parse(manifest.GetRawText()), JsonNode.Parse(entry.ManifestJson))) return null;
            // The bridge validates the pinned remote manifest and its complete local result separately.
            // Preserve every author-created subdirectory; only this proven container is omitted from the logical root.
            return Path.Combine(outputDirectory, segments[^1]);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                                     or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException
                                     or NotSupportedException or System.Security.SecurityException)
        { return null; }
    }
}
