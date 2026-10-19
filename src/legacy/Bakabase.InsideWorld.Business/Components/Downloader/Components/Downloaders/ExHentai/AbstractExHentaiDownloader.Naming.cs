using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

public abstract partial class AbstractExHentaiDownloader
{
    private static Dictionary<ExHentaiNamingFields, object?> GalleryNamingValues(ExHentaiResource detail, string sourceKey)
    {
        var sourceKeyParts = sourceKey.Split('/');
        return new Dictionary<ExHentaiNamingFields, object?>
        {
            [ExHentaiNamingFields.GalleryId] = sourceKeyParts[0],
            [ExHentaiNamingFields.GalleryToken] = sourceKeyParts[1],
            [ExHentaiNamingFields.RawName] = detail.RawName,
            [ExHentaiNamingFields.Name] = detail.Name,
            [ExHentaiNamingFields.Category] = detail.Category,
        };
    }

    // BT payloads and direct images share the same gallery directory template. A flat or
    // page-dependent template keeps the legacy gallery wrapper; custom names control uniqueness.
    private async Task<(string RelativeDirectory, string? TemplateDirectory)> ResolveGalleryNamingAsync(
        ExHentaiResource detail, string sourceKey)
    {
        var betterName = detail.RawName.IsNullOrEmpty() ? detail.Name : detail.RawName;
        var baseNameSegmentsValues = GalleryNamingValues(detail, sourceKey);
        var sourceKeyParts = sourceKey.Split('/');
        var namingConvention = GetEffectiveNamingConvention((await GetDownloaderOptionsAsync()).NamingConvention);
        var templateDirectory = Path.GetDirectoryName(namingConvention);
        var pageDependentDirectory = templateDirectory?.Contains("{PageTitle}", StringComparison.OrdinalIgnoreCase) == true ||
                                     templateDirectory?.Contains("{Extension}", StringComparison.OrdinalIgnoreCase) == true;
        var sampleValues = new Dictionary<ExHentaiNamingFields, object?>(baseNameSegmentsValues)
        {
            [ExHentaiNamingFields.PageTitle] = "bakabase-sample-page",
            [ExHentaiNamingFields.Extension] = ".img"
        };
        var samplePath = await BuildDownloadFilename(sampleValues);
        var renderedDirectory = Path.GetDirectoryName(samplePath);
        var useTemplateDirectory = !pageDependentDirectory && !string.IsNullOrWhiteSpace(renderedDirectory);
        var relativeDirectory = useTemplateDirectory
            ? renderedDirectory!
            : FileNameSanitizer.Sanitize($"[{detail.Category}] {betterName}");
        if (string.IsNullOrWhiteSpace(relativeDirectory))
            relativeDirectory = $"gallery-{sourceKeyParts[0]}";
        // Reserve the torrent suffix in the shared basename so long names remain identical
        // for the gallery folder and its exported seed on UTF-8 filesystems too.
        relativeDirectory = string.Join(Path.DirectorySeparatorChar,
            relativeDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Select(part => part.Length == 0 ? part :
                    ExHentaiTorrentFileName.Limit(part + ".torrent", sourceKey)[..^".torrent".Length]));
        return (relativeDirectory, useTemplateDirectory ? renderedDirectory : null);
    }

    private async Task<(string Directory, string? TemplateDirectory)> ResolveGalleryDirectoryAsync(
        string downloadRoot, ExHentaiResource detail, string sourceKey)
    {
        var (relativeDirectory, templateDirectory) = await ResolveGalleryNamingAsync(detail, sourceKey);
        return (ExHentaiGalleryOutputPath.Resolve(downloadRoot, relativeDirectory), templateDirectory);
    }

    private async Task<string> ResolveTorrentPathAsync(string downloadRoot, ExHentaiResource detail, string sourceKey)
    {
        var (relativeDirectory, _) = await ResolveGalleryNamingAsync(detail, sourceKey);
        var path = Path.GetFullPath(Path.Combine(downloadRoot, relativeDirectory + ".torrent"));
        ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(downloadRoot, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(downloadRoot, path);
        return path;
    }
}
