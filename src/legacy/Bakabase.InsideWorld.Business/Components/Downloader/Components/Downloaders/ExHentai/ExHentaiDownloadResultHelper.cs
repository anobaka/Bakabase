using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

public static class ExHentaiDownloadResultHelper
{
    /// <summary>Use the recorded user copy, never the managed metadata cache, for folder navigation and resume.</summary>
    public static string? GetTorrentDownloadPath(DownloadResultDbModel result)
    {
        if (result.Kind != DownloadResultKind.TorrentMetadata) return null;
        try
        {
            var recorded = GetRecordedTorrentDownloadPath(result);
            if (recorded != null) return recorded;
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(result.DownloadDirectory));
            // Older results recorded only their managed metadata. Their user copy used this name.
            return Path.Combine(directory, ExHentaiTorrentFileName.Limit(
                FileNameSanitizer.Sanitize($"{result.Name.RemoveInvalidFileNameChars()}.torrent"), result.SourceKey));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The exported torrent may be a sibling of the payload directory, or precede a later root change.</summary>
    internal static string? GetRecordedTorrentDownloadPath(DownloadResultDbModel result)
    {
        if (result.Kind != DownloadResultKind.TorrentMetadata) return null;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var file in JsonSerializer.Deserialize<string[]>(result.FilesJson) ?? [])
        {
            var path = Path.GetFullPath(file);
            if (!string.Equals(path, Path.GetFullPath(result.Path), comparison) &&
                Path.GetExtension(path).Equals(".torrent", StringComparison.OrdinalIgnoreCase)) return path;
        }
        return null;
    }

    /// <summary>Keep legacy workflow roots; BT resolves its gallery template after fetching gallery metadata.</summary>
    public static string GetWorkDirectory(string downloadPath, string url, int? workflowId, bool downloadContents = false) =>
        workflowId.HasValue && !downloadContents
            ? Path.Combine(downloadPath, "gallery-" + NormalizeSourceKey(url).Replace('/', '-'))
            : downloadPath;

    /// <summary>Shared by the source and acquisition adapter; host, query and trailing slash do not identify a work.</summary>
    public static string NormalizeSourceKey(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var match = Regex.Match(path, @"(?:^|/)g/(?<id>\d+)/(?<token>[a-zA-Z0-9]+)(?:/|$)");
        if (!match.Success) throw new ArgumentException("A gallery URL with its ID and token is required.", nameof(url));
        return long.Parse(match.Groups["id"].Value) + "/" + match.Groups["token"].Value.ToLowerInvariant();
    }
}
