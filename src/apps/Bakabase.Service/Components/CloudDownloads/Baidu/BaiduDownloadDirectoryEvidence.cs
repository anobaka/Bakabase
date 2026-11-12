using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>Read the bridge's immutable, account/intent-bound path map; never create or repair it.</summary>
internal static class BaiduDownloadDirectoryEvidence
{
    internal static async Task<CloudDownloadDirectoryEvidence?> ReadAsync(string accountDirectory, string uid,
        CloudDownloadRequest request, string artifactRoot, CancellationToken ct)
    {
        if (request.RemoteEntry is not {OwnedContainer: not null} remote || string.IsNullOrWhiteSpace(uid) || uid == "0" ||
            string.IsNullOrWhiteSpace(request.OperationId)) return null;
        var owned = await BaiduOwnedDownloadRoot.ResolveAsync(accountDirectory, remote, artifactRoot, ct);
        if (owned == null) return null;
        try
        {
            var wrapper = Path.GetRelativePath(artifactRoot, owned).Replace('\\', '/');
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OperationId))).ToLowerInvariant();
            var path = Path.Combine(accountDirectory, "operations", key + ".paths-v1.json");
            ArtifactManifestFiles.RejectLinks(path);
            var info = new FileInfo(path);
            // No map means the exact original remote relative paths must match the captured local manifest.
            if (!info.Exists) return await BaiduCloudDownloadProvider.ReadDirectoryEvidenceAsync(accountDirectory, remote, artifactRoot, ct);
            if (info.Length > 4 * 1024 * 1024) return null;
            using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
            using var manifest = JsonDocument.Parse(remote.ManifestJson!);
            var root = journal.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("uid").GetString() != uid ||
                root.GetProperty("fingerprint").GetString() != Fingerprint(request.OperationId, uid,
                    CloudDownloadDestinations.ExecutionDirectory(request), remote.Path, manifest.RootElement)) return null;
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in root.GetProperty("paths").EnumerateArray())
            {
                var relative = pair.GetProperty("Relative").GetString();
                if (relative == null || !Safe(relative) || !(relative == wrapper || relative.StartsWith(wrapper + "/", StringComparison.Ordinal)) ||
                    !mapping.TryAdd(pair.GetProperty("Remote").GetString()!, relative)) return null;
            }
            var members = manifest.RootElement.EnumerateArray().ToArray();
            if (mapping.Count != members.Length || mapping.Values.Distinct(StringComparer.Ordinal).Count() != mapping.Count) return null;
            var expected = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                var remotePath = member.GetProperty("path").GetString()!;
                if (!mapping.TryGetValue(remotePath, out var relative) ||
                    !(remotePath == remote.Path || remotePath.StartsWith(remote.Path + "/", StringComparison.Ordinal))) return null;
                if (remotePath == remote.Path && relative != wrapper) return null;
                if (member.GetProperty("isDirectory").GetBoolean()) continue;
                var size = member.GetProperty("size").GetInt64();
                if (size < 0 || !expected.TryAdd(relative, size)) return null;
            }
            return expected.Count == 0 ? null : new(wrapper, expected);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                     or JsonException or KeyNotFoundException or NotSupportedException or System.Security.SecurityException)
        { return null; }
    }

    private static bool Safe(string path) => !path.Any(char.IsControl) && !path.Contains('\\') && !path.Contains(':') &&
        path.Split('/').All(p => p is not ("" or "." or ".."));

    // Go's encoding/json is the bridge's durable wire contract. Its UTF-8 ordering and escaping
    // differ from System.Text.Json, notably for non-ASCII names, HTML characters and surrogate pairs.
    internal static string Fingerprint(string operation, string uid, string directory, string remote, JsonElement manifest)
    {
        var text = new StringBuilder("{\"Operation\":").Append(Quote(operation)).Append(",\"UID\":").Append(Quote(uid))
            .Append(",\"Root\":").Append(Quote(directory)).Append(",\"Remote\":").Append(Quote(remote)).Append(",\"Manifest\":[");
        var members = manifest.EnumerateArray().OrderBy(e => e.GetProperty("path").GetString(), Utf8Order.Instance).ToArray();
        for (var index = 0; index < members.Length; index++)
        {
            if (index > 0) text.Append(',');
            var e = members[index];
            text.Append("{\"id\":").Append(e.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture))
                .Append(",\"path\":").Append(Quote(e.GetProperty("path").GetString()!))
                .Append(",\"size\":").Append(e.GetProperty("size").GetInt64().ToString(CultureInfo.InvariantCulture))
                .Append(",\"isDirectory\":").Append(e.GetProperty("isDirectory").GetBoolean() ? "true" : "false").Append('}');
        }
        text.Append("]}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (character < 0x20 || character is '<' or '>' or '&' or '\u2028' or '\u2029')
                        text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(character);
                    break;
            }
        }
        return text.Append('"').ToString();
    }
    private sealed class Utf8Order : IComparer<string?>
    {
        internal static readonly Utf8Order Instance = new();
        public int Compare(string? x, string? y) => Encoding.UTF8.GetBytes(x ?? "").AsSpan()
            .SequenceCompareTo(Encoding.UTF8.GetBytes(y ?? ""));
    }
}
