using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceSourceExtensions
{
    public static PropertyValueScope GetPropertyValueScope(this ResourceSource source) => source switch
    {
        ResourceSource.Steam => PropertyValueScope.Steam,
        ResourceSource.DLsite => PropertyValueScope.DLsite,
        ResourceSource.ExHentai => PropertyValueScope.ExHentai,
        // Pixiv has no scope of its own yet; it gets one when there is a Pixiv integration writing
        // values that need to be told apart from other synchronized ones.
        _ => PropertyValueScope.Synchronization
    };

    /// <summary>
    /// Whether this source names a platform that can hold resource content. This classifies the
    /// platform, not the user's ownership or the availability of a download connector. Metadata
    /// sites and sharing channels are represented separately from resource sources.
    /// <para>
    /// The switch has no discard arm on purpose: a new source has to answer this question, and
    /// CS8509 is promoted to an error for this file so it cannot be left unanswered.
    /// </para>
    /// </summary>
    public static bool IsPlatformHolding(this ResourceSource source)
    {
        if (!Enum.IsDefined(source)) return false;
        return source switch
        {
            ResourceSource.Steam => true,
            ResourceSource.DLsite => true,
            ResourceSource.ExHentai => true,
            ResourceSource.Pixiv => true,
            // Found on the user's own disk — there is nothing to fetch from anywhere.
            ResourceSource.PathMark => false,
            // Locally generated content; no platform holds it.
            ResourceSource.Aigc => false
        };
    }

    public static DataOrigin? ToDataOrigin(this ResourceSource source) => source switch
    {
        ResourceSource.Steam => DataOrigin.Steam,
        ResourceSource.DLsite => DataOrigin.DLsite,
        ResourceSource.ExHentai => DataOrigin.ExHentai,
        _ => null
    };
}
