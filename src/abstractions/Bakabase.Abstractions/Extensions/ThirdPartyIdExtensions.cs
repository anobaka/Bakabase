using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class ThirdPartyIdExtensions
{
    /// <summary>Content platforms have resource sources; metadata and sharing sites do not.</summary>
    public static ResourceSource? ToResourceSource(this ThirdPartyId thirdPartyId) => thirdPartyId switch
    {
        ThirdPartyId.Steam => ResourceSource.Steam,
        ThirdPartyId.DLsite => ResourceSource.DLsite,
        ThirdPartyId.ExHentai => ResourceSource.ExHentai,
        ThirdPartyId.Pixiv => ResourceSource.Pixiv,
        _ => null
    };

    public static PropertyValueScope GetPropertyValueScope(this ThirdPartyId thirdPartyId) => thirdPartyId switch
    {
        ThirdPartyId.Bangumi => PropertyValueScope.Bangumi,
        _ => thirdPartyId.ToResourceSource()?.GetPropertyValueScope() ?? PropertyValueScope.Synchronization
    };
}
