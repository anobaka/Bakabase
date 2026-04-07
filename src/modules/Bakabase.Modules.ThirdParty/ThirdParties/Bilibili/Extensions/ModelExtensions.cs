using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Extensions;

public static class ModelExtensions
{
    public static Favorites ToDomain(this ApiFavorites favorites)
    {
        return new Favorites
        {
            Id = favorites.Id,
            MediaCount = favorites.MediaCount,
            Title = favorites.Title
        };
    }
}