using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

public static class BilibiliQualityNaming
{
    /// <summary>
    /// The QualityName part of downloaded file names. Apply it ONLY to the legacy naming answer
    /// (<c>BiliBiliApiUrls.LegacyNamingPlayUrl</c>, fnval=16): it reproduces what the downloader has always
    /// used, so existing libraries are recognised and not downloaded again.
    /// </summary>
    public static string? LegacyQualityName(IEnumerable<VideoQuality>? supportFormats) =>
        supportFormats?.MaxBy(f => f.Quality)?.Description;

    /// <summary>For progress text and logs only (never for file names): the id's description, or "Q{id}".</summary>
    public static string DescribeQuality(int id, IEnumerable<VideoQuality>? supportFormats) =>
        supportFormats?.FirstOrDefault(f => f.Quality == id)?.Description is { Length: > 0 } description
            ? description
            : $"Q{id}";
}
