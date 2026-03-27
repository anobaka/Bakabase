namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Identifies where resource data (covers, playable items, metadata) comes from.
/// Different from <see cref="ResourceSource"/> which identifies how a resource was discovered.
/// </summary>
public enum DataOrigin
{
    /// <summary>
    /// User manually set the data.
    /// </summary>
    Manual = 1,

    /// <summary>
    /// Data discovered from local filesystem (cover files, playable files).
    /// Every resource with a physical path inherently has this capability.
    /// </summary>
    FileSystem = 2,

    /// <summary>
    /// Data from Steam (cover images, steam:// launch URIs).
    /// </summary>
    Steam = 3,

    /// <summary>
    /// Data from DLsite (cover images, product page links).
    /// </summary>
    DLsite = 4,

    /// <summary>
    /// Data from ExHentai (cover images, gallery links).
    /// </summary>
    ExHentai = 5,
}
