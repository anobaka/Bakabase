namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>Where resource content is discovered or available. Metadata sites use ThirdPartyId identities.</summary>
public enum ResourceSource
{
    PathMark = 1,
    Steam = 2,
    DLsite = 3,
    ExHentai = 4,
    Aigc = 5,
    Pixiv = 7,
}
