using System;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyFavoritesService(IServiceProvider serviceProvider)
        : ResourceService<BakabaseDbContext, Favorites, int>(serviceProvider);
}
