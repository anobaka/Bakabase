using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class ResourceSourceExtensions
{
    public static PropertyValueScope GetPropertyValueScope(this ResourceSource source) => source switch
    {
        ResourceSource.Steam => PropertyValueScope.Steam,
        ResourceSource.DLsite => PropertyValueScope.DLsite,
        ResourceSource.ExHentai => PropertyValueScope.ExHentai,
        _ => PropertyValueScope.Synchronization
    };

    /// <summary>
    /// Whether the user holds the resource on this platform — bought it, owns it, favourited it —
    /// so the platform both identifies the resource and can hand its files over. That is a
    /// different thing from a place where someone merely shared a link: a sharing channel never
    /// becomes a <see cref="ResourceSource"/> at all, because it says nothing about what the
    /// resource is.
    /// <para>
    /// The switch has no discard arm on purpose: a new source has to answer this question, and
    /// CS8509 is promoted to an error for this file so it cannot be left unanswered.
    /// </para>
    /// </summary>
    public static bool IsPlatformHolding(this ResourceSource source) => source switch
    {
        ResourceSource.Steam => true,
        ResourceSource.DLsite => true,
        ResourceSource.ExHentai => true,
        // Found on the user's own disk — there is nothing to fetch from anywhere.
        ResourceSource.PathMark => false,
        // Locally generated content; no platform holds it.
        ResourceSource.Aigc => false
    };

    public static DataOrigin? ToDataOrigin(this ResourceSource source) => source switch
    {
        ResourceSource.Steam => DataOrigin.Steam,
        ResourceSource.DLsite => DataOrigin.DLsite,
        ResourceSource.ExHentai => DataOrigin.ExHentai,
        _ => null
    };
}
