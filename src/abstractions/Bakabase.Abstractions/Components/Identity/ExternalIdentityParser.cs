using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Identity;

/// <summary>
/// Turns whatever the user pasted — a work id, a store page URL, a gallery link — into the
/// <see cref="ResourceSource"/> and source key that identify a resource.
/// <para>
/// The same patterns used to live in four resolvers and services, each recognising a slightly
/// different subset. They are here so "paste a link or an id" means the same thing everywhere, and
/// so a new source only has to be taught once.
/// </para>
/// </summary>
public static class ExternalIdentityParser
{
    // RJ/BJ/VJ + digits. DLsite prints it on the page and puts it in the URL, and users type it on
    // its own more often than they paste a link.
    private static readonly Regex DLsiteWorkId =
        new(@"\b([RBV]J\d{6,10})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SteamAppUrl =
        new(@"store\.steampowered\.com/app/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A gallery is identified by both its number and its token; either alone is not enough to
    // fetch it.
    private static readonly Regex ExHentaiGallery =
        new(@"(?:e-|ex)hentai\.org/g/(\d+)/([a-f0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BangumiSubjectUrl =
        new(@"(?:bgm|bangumi)\.tv/subject/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PixivArtworkUrl =
        new(@"pixiv\.net/(?:[a-z]{2}/)?artworks/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Recognises the identity in <paramref name="input"/>, if there is one.
    /// </summary>
    /// <remarks>
    /// A bare number is deliberately not recognised as anything: it could be a Steam application, a
    /// Bangumi subject or a Pixiv artwork, and guessing wrong would attach a resource to the wrong
    /// platform. URL forms are matched first for the same reason — a Steam page URL says which
    /// platform it is, a naked id does not.
    /// </remarks>
    public static bool TryExtract(string input, out ResourceSource source,
        [NotNullWhen(true)] out string? sourceKey)
    {
        source = default;
        sourceKey = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        var exHentai = ExHentaiGallery.Match(text);
        if (exHentai.Success)
        {
            source = ResourceSource.ExHentai;
            sourceKey = $"{exHentai.Groups[1].Value}/{exHentai.Groups[2].Value.ToLowerInvariant()}";
            return true;
        }

        var steam = SteamAppUrl.Match(text);
        if (steam.Success)
        {
            source = ResourceSource.Steam;
            sourceKey = steam.Groups[1].Value;
            return true;
        }

        var bangumi = BangumiSubjectUrl.Match(text);
        if (bangumi.Success)
        {
            source = ResourceSource.Bangumi;
            sourceKey = bangumi.Groups[1].Value;
            return true;
        }

        var pixiv = PixivArtworkUrl.Match(text);
        if (pixiv.Success)
        {
            source = ResourceSource.Pixiv;
            sourceKey = pixiv.Groups[1].Value;
            return true;
        }

        var dlsite = DLsiteWorkId.Match(text);
        if (dlsite.Success)
        {
            source = ResourceSource.DLsite;
            sourceKey = dlsite.Groups[1].Value.ToUpperInvariant();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recognises an identity belonging to one specific source, so a caller who already knows which
    /// platform is meant can accept a bare id that would be ambiguous on its own.
    /// </summary>
    public static bool TryExtractFor(ResourceSource source, string input,
        [NotNullWhen(true)] out string? sourceKey)
    {
        sourceKey = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        if (TryExtract(text, out var detected, out var detectedKey) && detected == source)
        {
            sourceKey = detectedKey;
            return true;
        }

        // The source is known, so a bare number is no longer ambiguous.
        if (source is ResourceSource.Steam or ResourceSource.Bangumi or ResourceSource.Pixiv &&
            long.TryParse(text, out _))
        {
            sourceKey = text;
            return true;
        }

        return false;
    }
}
