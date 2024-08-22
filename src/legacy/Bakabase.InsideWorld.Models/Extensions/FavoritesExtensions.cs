using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class FavoritesExtensions
    {
        public static FavoritesDto ToDto(this Favorites favorite)
        {
            if (favorite == null)
            {
                return null;
            }

            return new FavoritesDto
            {
                CreateDt = favorite.CreateDt,
                Description = favorite.Description,
                Id = favorite.Id,
                Name = favorite.Name
            };
        }
    }
}
