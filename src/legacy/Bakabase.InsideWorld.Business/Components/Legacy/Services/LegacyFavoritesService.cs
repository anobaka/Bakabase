using System;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyFavoritesService(IServiceProvider serviceProvider)
        : ResourceService<InsideWorldDbContext, Favorites, int>(serviceProvider);
}
