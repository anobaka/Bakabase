using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete($"Use new version of CustomProperty instead")]
    public record CustomResourceProperty
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        [Required] public string Key { get; set; } = null!;
        public int? Index { get; set; }
        [Required] public string Value { get; set; } = null!;
        public CustomDataType ValueType { get; set; }

        private sealed class CustomResourcePropertyEqualityComparer : IEqualityComparer<CustomResourceProperty>
        {
            public bool Equals(CustomResourceProperty? x, CustomResourceProperty? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Key == y.Key && x.Index == y.Index && x.Value == y.Value && x.ValueType == y.ValueType;
            }

            public int GetHashCode(CustomResourceProperty obj)
            {
                return HashCode.Combine(obj.Key, obj.Index, obj.Value, (int) obj.ValueType);
            }
        }

        public static IEqualityComparer<CustomResourceProperty> CustomResourcePropertyComparer { get; } =
            new CustomResourcePropertyEqualityComparer();
    }
}