namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;

/// <summary>
/// How strongly a magnet's name hints at a Chinese-subtitled release.
/// The numeric order is the preference order used by <see cref="JavbusMagnetSelector"/>.
/// </summary>
public enum JavbusMagnetTag
{
    /// <summary>Plain ASCII name — no Chinese, no subtitle marker.</summary>
    Plain = 1,

    /// <summary>Contains Chinese characters; not necessarily subtitled, but usually better seeded.</summary>
    Chinese = 2,

    /// <summary>Carries a <c>-C</c> / <c>-UC</c> suffix, the conventional subtitled-release marker.</summary>
    SubtitleSuffix = 3,

    /// <summary>Explicitly says 字幕 / 中字 / 中文 — the strongest signal.</summary>
    SubtitleKeyword = 4
}
