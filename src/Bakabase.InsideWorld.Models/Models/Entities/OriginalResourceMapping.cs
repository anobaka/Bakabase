using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class OriginalResourceMapping : IEquatable<OriginalResourceMapping>
    {
        [Key] public int Id { get; set; }
        public int OriginalId { get; set; }
        public int ResourceId { get; set; }

        public bool Equals(OriginalResourceMapping other)
        {
            return other != null && OriginalId == other.OriginalId && ResourceId == other.ResourceId;
        }

        public override int GetHashCode()
        {
            return $"{OriginalId}_{ResourceId}".GetHashCode();
        }
    }
}