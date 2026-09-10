namespace Bakabase.Abstractions.Models.Domain.Constants;

public enum ResourceSource
{
    PathMark = 1,
    Steam = 2,
    DLsite = 3,
    ExHentai = 4,
    Aigc = 5,

    /// <summary>
    /// A metadata authority: it identifies a work and describes it, but nobody holds files there.
    /// </summary>
    Bangumi = 6,

    Pixiv = 7,

    /// <summary>
    /// A metadata authority like <see cref="Bangumi"/>: the most reliable place a visual novel
    /// series is written down, and nowhere anybody holds files.
    /// </summary>
    Vndb = 8,
}
