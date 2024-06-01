using System;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class ResourceTagMapping : IEquatable<ResourceTagMapping>
    {
        public int Id { get; set; }
        public int TagId { get; set; }
        public int ResourceId { get; set; }

        public bool Equals(ResourceTagMapping m)
        {
            return m != null && m.TagId == TagId && m.ResourceId == ResourceId;
        }

        public override int GetHashCode()
        {
            return $"{ResourceId}-{TagId}".GetHashCode();
        }
    }
}