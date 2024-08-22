using System;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class PublisherTagMapping: IEquatable<PublisherTagMapping>
    {
        public int Id { get; set; }
        public int PublisherId { get; set; }
        public int TagId { get; set; }

        public bool Equals(PublisherTagMapping m)
        {
            return m != null && m.TagId == TagId && m.PublisherId == PublisherId;
        }

        public override int GetHashCode()
        {
            return $"{PublisherId}_{TagId}".GetHashCode();
        }
    }
}