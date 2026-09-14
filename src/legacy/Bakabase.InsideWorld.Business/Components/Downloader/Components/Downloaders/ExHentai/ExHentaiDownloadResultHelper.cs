using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

public static class ExHentaiDownloadResultHelper
{
    /// <summary>Keep each automatic handoff's files independent while preserving legacy save-only paths.</summary>
    public static string GetWorkDirectory(string downloadPath, string url, int? workflowId) =>
        workflowId.HasValue
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
