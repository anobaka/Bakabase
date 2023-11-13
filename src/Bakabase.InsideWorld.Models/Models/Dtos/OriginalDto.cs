using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record OriginalDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;

        private sealed class BizEqualityComparer : IEqualityComparer<OriginalDto>
        {
            public bool Equals(OriginalDto? x, OriginalDto? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(OriginalDto obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name);
            }
        }

        public static IEqualityComparer<OriginalDto> BizComparer { get; } = new BizEqualityComparer();
    }
}