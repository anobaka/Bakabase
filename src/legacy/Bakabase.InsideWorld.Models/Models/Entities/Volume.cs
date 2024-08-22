using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class Volume
    {
        [Key] public int ResourceId { get; set; }
        public int SerialId { get; set; }
        public int Index { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }

        public override bool Equals(object? obj)
        {
            return obj is Volume v && Equals(v);
        }

        protected bool Equals(Volume other)
        {
            return ResourceId == other.ResourceId && SerialId == other.SerialId && Index == other.Index &&
                   Name == other.Name && Title == other.Title;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ResourceId, SerialId, Index, Name, Title);
        }
    }
}