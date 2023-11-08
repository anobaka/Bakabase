using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record PublisherDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public List<PublisherDto> SubPublishers { get; set; } = new();
        public int Rank { get; set; } = 0;
        public bool Favorite { get; set; }
        public List<TagDto> Tags { get; set; } = new();

        private sealed class BizEqualityComparer : IEqualityComparer<PublisherDto>
        {
            public bool Equals(PublisherDto? x, PublisherDto? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                if (x.Name == y.Name && x.Rank == y.Rank && x.Favorite == y.Favorite)
                {
                    if (x.SubPublishers.Count == y.SubPublishers.Count)
                    {
                        var count = x.SubPublishers.Count;
                        for (var i = 0; i < count; i++)
                        {
                            if (!Equals(x.SubPublishers[i], y.SubPublishers[i]))
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                }

                return false;
            }

            public int GetHashCode(PublisherDto obj)
            {
                return HashCode.Combine(obj.Name, obj.SubPublishers, obj.Rank, obj.Favorite, obj.Tags);
            }
        }

        public static IEqualityComparer<PublisherDto> BizComparer { get; } = new BizEqualityComparer();
    }
}