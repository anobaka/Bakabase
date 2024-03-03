using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Returns true if they have same id or their names and sub-publishers are same.
        /// </summary>
        private sealed class BizEqualityComparer : IEqualityComparer<PublisherDto>
        {
            public bool Equals(PublisherDto? x, PublisherDto? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;

                if (x.Id != 0 && y.Id != 0)
                {
                    if (x.Id == y.Id)
                    {
                        return true;
                    }
                }

                if (!string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (x.SubPublishers.Count != y.SubPublishers.Count)
                {
                    return false;
                }

                return !x.SubPublishers.Where((t, i) => !Equals(t, y.SubPublishers[i])).Any();
            }

            public int GetHashCode(PublisherDto obj)
            {
                var hashCode = new HashCode();
                hashCode.Add(obj.Name, StringComparer.OrdinalIgnoreCase);
                hashCode.Add(obj.SubPublishers);
                return hashCode.ToHashCode();
            }
        }

        /// <summary>
        /// <inheritdoc cref="BizEqualityComparer"/>
        /// </summary>
        public static IEqualityComparer<PublisherDto> BizComparer { get; } = new BizEqualityComparer();
    }
}