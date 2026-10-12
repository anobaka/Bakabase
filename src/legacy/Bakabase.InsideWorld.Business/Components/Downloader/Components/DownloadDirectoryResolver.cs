using System;
using System.IO;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

/// <summary>One precedence rule for manual, browser and automated download task creation.</summary>
public static class DownloadDirectoryResolver
{
    public static string? Resolve(string? taskPath, string? sourcePath, string? globalRoot, ThirdPartyId source)
    {
        if (!string.IsNullOrWhiteSpace(taskPath)) return taskPath;
        if (!string.IsNullOrWhiteSpace(sourcePath)) return sourcePath;
        if (string.IsNullOrWhiteSpace(globalRoot)) return null;

        // Enum identifiers are stable, language-independent folder names shared by all task types.
        var folder = Enum.GetName(source) ?? throw new ArgumentOutOfRangeException(nameof(source), source,
            "Unknown download source.");
        return Path.Combine(globalRoot, folder);
    }
}
