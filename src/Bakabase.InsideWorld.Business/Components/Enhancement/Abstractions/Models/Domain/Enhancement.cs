using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain
{
    public record Enhancement
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public StandardValueType ValueType { get; set; }

        /// <summary>
        /// <see cref="EnhancementRawValue.Target"/>
        /// </summary>
        public int Target { get; set; }

        /// <summary>
        /// <see cref="EnhancementRawValue.Value"/>
        /// </summary>
        public object? Value { get; set; }

        public DateTime CreatedAt { get; set; }

        private sealed class EnhancementBizKeyEqualityComparer : IEqualityComparer<Enhancement>
        {
            public bool Equals(Enhancement? x, Enhancement? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return (x.Id > 0 && x.Id == y.Id) || (x.ResourceId > 0 && x.ResourceId == y.ResourceId &&
                                                      x.EnhancerId > 0 && x.EnhancerId == y.EnhancerId &&
                                                      x.Target > 0 && x.Target == y.Target);
            }

            public int GetHashCode(Enhancement obj)
            {
                return obj.Id > 0
                    ? HashCode.Combine(obj.Id)
                    : HashCode.Combine(obj.ResourceId, (int) obj.EnhancerId, obj.Target);
            }
        }

        public static IEqualityComparer<Enhancement> BizKeyComparer { get; } = new EnhancementBizKeyEqualityComparer();
    }
}