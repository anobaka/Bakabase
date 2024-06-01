namespace Bakabase.Abstractions.Models.Db
{
    public record TagResourceMapping
    {
        public int Id { get; set; }
        public int TagId { get; set; }
        public int ResourceId { get; set; }
        public int Scope { get; set; }

        private sealed class BizKeyEqualityComparer : IEqualityComparer<TagResourceMapping>
        {
            public bool Equals(TagResourceMapping? x, TagResourceMapping? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.TagId == y.TagId && x.ResourceId == y.ResourceId && x.Scope == y.Scope;
            }

            public int GetHashCode(TagResourceMapping obj)
            {
                return HashCode.Combine(obj.TagId, obj.ResourceId, obj.Scope);
            }
        }

        public static IEqualityComparer<TagResourceMapping> BizComparer { get; } =
            new BizKeyEqualityComparer();
    }
}