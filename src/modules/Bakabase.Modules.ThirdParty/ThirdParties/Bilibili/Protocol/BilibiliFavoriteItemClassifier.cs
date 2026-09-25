using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

public enum BilibiliFavoriteItemKind
{
    Video = 1,
    OgvEpisode = 2,
    Audio = 3,
    UgcSeason = 4,
    Unknown = 5,
}

/// <param name="Skip">Null when the item should be downloaded.</param>
public sealed record BilibiliFavoriteItemClassification(BilibiliFavoriteItemKind Kind, BilibiliSkip? Skip);

/// <summary>
/// Decides what a favorites entry is from the list fields alone: never a request, and the title is never
/// read (invalid items keep their real title as often as they show 已失效视频).
/// </summary>
public static class BilibiliFavoriteItemClassifier
{
    public const int TypeVideo = 2;
    public const int TypeAudio = 12;
    public const int TypeUgcSeason = 21;
    public const int TypeOgvEpisode = 24;

    public const int AttrInvalid = 1;
    public const int AttrPgcArchive = 2;
    public const int AttrLoginOnly = 4;
    public const int AttrInteractive = 16;

    public static BilibiliFavoriteItemClassification Classify(FavoriteItem item)
    {
        var kind = item.Type switch
        {
            // The field missing (0): keep today's behaviour instead of skipping — and checkpointing past — a
            // whole library if Bilibili ever drops it.
            TypeVideo or 0 => BilibiliFavoriteItemKind.Video,
            TypeOgvEpisode => BilibiliFavoriteItemKind.OgvEpisode,
            TypeAudio => BilibiliFavoriteItemKind.Audio,
            TypeUgcSeason => BilibiliFavoriteItemKind.UgcSeason,
            _ => BilibiliFavoriteItemKind.Unknown,
        };

        if ((item.Attr & AttrInvalid) != 0)
        {
            return new(kind, new BilibiliSkip(BilibiliSkipReason.InvalidItem));
        }

        return kind switch
        {
            // id is an ep_id, not an aid.
            BilibiliFavoriteItemKind.OgvEpisode => new(kind, new BilibiliSkip(BilibiliSkipReason.UnsupportedOgvEpisode)),
            // id is an audio id.
            BilibiliFavoriteItemKind.Audio => new(kind, new BilibiliSkip(BilibiliSkipReason.UnsupportedAudio)),
            BilibiliFavoriteItemKind.UgcSeason => new(kind, new BilibiliSkip(BilibiliSkipReason.UnsupportedCollection)),
            BilibiliFavoriteItemKind.Unknown => new(kind,
                new BilibiliSkip(BilibiliSkipReason.UnsupportedItemType, item.Type)),
            // Only the first few seconds (the first node) of an interactive video could be downloaded.
            _ when (item.Attr & AttrInteractive) != 0 => new(kind,
                new BilibiliSkip(BilibiliSkipReason.InteractiveVideo)),
            // attr 2 (PGC archive) and 4 (login-only legacy archive) are valid videos.
            _ => new(kind, null),
        };
    }
}
