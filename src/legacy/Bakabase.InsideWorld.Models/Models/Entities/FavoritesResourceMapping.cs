using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class FavoritesResourceMapping : IEquatable<FavoritesResourceMapping>
    {
        [Key] public int Id { get; set; }
        public int FavoritesId { get; set; }
        public int ResourceId { get; set; }

        public bool Equals(FavoritesResourceMapping m)
        {
            return m != null && m.FavoritesId == FavoritesId && m.ResourceId == ResourceId;
        }

        public override int GetHashCode()
        {
            return $"{FavoritesId}_{ResourceId}".GetHashCode();
        }
    }
}