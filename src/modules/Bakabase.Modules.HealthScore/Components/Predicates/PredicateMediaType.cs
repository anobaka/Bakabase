using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.HealthScore.Components.Predicates;

/// <summary>
/// Media-type vocabulary for scoring predicates. Kept independent from
/// <see cref="MediaType"/> so the two can evolve independently — the
/// global enum is pinned by inverse-index keys and other consumers, while
/// here we want a stable set of buckets that include <see cref="All"/>
/// (any file regardless of extension).
/// </summary>
public enum PredicateMediaType
{
    /// <summary>Match any file regardless of extension.</summary>
    All = 0,
    Video = 1,
    Image = 2,
    Audio = 3,
    Text = 4,
    Application = 5,
}

internal static class PredicateMediaTypeExtensions
{
    /// <summary>Maps to the internal <see cref="MediaType"/> bucket; null when <c>All</c>.</summary>
    public static MediaType? ToInternalMediaType(this PredicateMediaType t) => t switch
    {
        PredicateMediaType.All => null,
        PredicateMediaType.Video => MediaType.Video,
        PredicateMediaType.Image => MediaType.Image,
        PredicateMediaType.Audio => MediaType.Audio,
        PredicateMediaType.Text => MediaType.Text,
        PredicateMediaType.Application => MediaType.Application,
        _ => null,
    };
}
