using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record VolumeDto
    {
        public int Index { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }
        public int SerialId { get; set; }
        public int ResourceId { get; set; }

        private sealed class BizEqualityComparer : IEqualityComparer<VolumeDto>
        {
            public bool Equals(VolumeDto? x, VolumeDto? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Index == y.Index && x.Name == y.Name && x.Title == y.Title;
            }

            public int GetHashCode(VolumeDto obj)
            {
                return HashCode.Combine(obj.Index, obj.Name, obj.Title);
            }
        }

        public static IEqualityComparer<VolumeDto> BizComparer { get; } = new BizEqualityComparer();
    }
}