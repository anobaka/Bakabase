namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi.Models;

/// <summary>
/// Bangumi subject types for filtering search results.
/// </summary>
public enum BangumiSubjectType
{
    /// <summary>
    /// All types (default)
    /// </summary>
    All = 0,

    /// <summary>
    /// Anime (动画)
    /// </summary>
    Anime = 1,

    /// <summary>
    /// Books/Manga (书籍/漫画)
    /// </summary>
    Book = 2,

    /// <summary>
    /// Music (音乐)
    /// </summary>
    Music = 3,

    /// <summary>
    /// Games (游戏)
    /// </summary>
    Game = 4,

    /// <summary>
    /// Real-world subjects like live-action, documentaries (三次元)
    /// </summary>
    Real = 5
}
