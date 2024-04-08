using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class PublisherResourceMapping : IEquatable<PublisherResourceMapping>
    {
        [Key] public int Id { get; set; }
        public int PublisherId { get; set; }
        public int? ParentPublisherId { get; set; }
        public int ResourceId { get; set; }

        public bool Equals(PublisherResourceMapping? m)
        {
            return m != null && m.ParentPublisherId == ParentPublisherId && m.PublisherId == PublisherId &&
                   m.ResourceId == ResourceId;
        }

        public override int GetHashCode()
        {
            return $"{ParentPublisherId}_{PublisherId}_{ResourceId}".GetHashCode();
        }
    }
}