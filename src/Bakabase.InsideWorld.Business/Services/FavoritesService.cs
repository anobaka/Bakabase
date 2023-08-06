using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm.Infrastructures;

namespace Bakabase.InsideWorld.Business.Services
{
    public class FavoritesService : ResourceService<InsideWorldDbContext, Favorites, int>
    {
        public FavoritesService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }
    }
}
