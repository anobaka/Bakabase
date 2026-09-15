using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

/// <summary>
/// Which downloader can fetch a link, and what it wants to be given.
/// </summary>
/// <param name="ThirdPartyId">The downloader's platform.</param>
/// <param name="TaskType">Which of that platform's task types — a single work, a listing, a creator.</param>
/// <param name="Key">What the task is keyed by. Usually the link itself; some downloaders want an id.</param>
public record DownloadTarget(ThirdPartyId ThirdPartyId, int TaskType, string Key);

/// <summary>
/// Reads a link and says which downloader can fetch it.
/// <para>
/// Enqueueing used to be an ExHentai-shaped action: one activity, one platform, the gallery URL
/// passed straight through. Once resources come from several platforms that stops being a step in a
/// recipe and becomes a special case. This is the table that replaces it — adding a platform is a
/// row here rather than an activity of its own.
/// </para>
/// <para>
/// It deliberately covers only what the downloaders in this build actually implement. The Fanbox,
/// Fantia, Ci-en and Patreon downloaders exist as stubs whose task bodies are still to be written;
/// listing their URL shapes here would produce tasks that sit in the queue doing nothing, which
/// reads to the user as a broken download rather than an absent one.
/// </para>
/// </summary>
public static partial class DownloadTargetResolver
{
    /// <summary>The platforms a link can currently be resolved to.</summary>
    public static IReadOnlyList<ThirdPartyId> SupportedPlatforms { get; } = [ThirdPartyId.ExHentai];

    /// <summary>
    /// The downloader for this link, if one of them can fetch it.
    /// </summary>
    public static bool TryResolve(string? url, [NotNullWhen(true)] out DownloadTarget? target)
    {
        target = null;

        if (string.IsNullOrWhiteSpace(url)) return false;

        var text = url.Trim();

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;

        // The host decides, and only the host: reading the shape out of the whole string would let
        // any link that merely quotes a gallery address in its query be sent to that downloader.
        if (!ExHentaiHost().IsMatch(uri.Host)) return false;

        // A gallery page is one work. Its URL is what the downloader is keyed by, token included —
        // the number alone is not enough to fetch it.
        var taskType = ExHentaiGalleryPath().IsMatch(uri.AbsolutePath)
            ? ExHentaiDownloadTaskType.SingleWork
            // Anything else on the site is a listing: a search, a favourites page, a tag. The list
            // downloader walks it page by page, so the link goes through as written — its query is
            // the whole of what it means.
            : ExHentaiDownloadTaskType.List;

        target = new DownloadTarget(ThirdPartyId.ExHentai, (int) taskType, text);

        return true;
    }

    [GeneratedRegex(@"^/g/\d+/[a-f0-9]+/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExHentaiGalleryPath();

    [GeneratedRegex(@"^(?:www\.)?(?:e-|ex)hentai\.org$", RegexOptions.IgnoreCase)]
    private static partial Regex ExHentaiHost();
}
