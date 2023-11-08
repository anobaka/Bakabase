using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class SeriesDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;

        private sealed class BizEqualityComparer : IEqualityComparer<SeriesDto>
        {
            public bool Equals(SeriesDto x, SeriesDto y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Name == y.Name;
            }

            public int GetHashCode(SeriesDto obj)
            {
                return obj.Name.GetHashCode();
            }
        }

        public static IEqualityComparer<SeriesDto> BizComparer { get; } = new BizEqualityComparer();
    }
}