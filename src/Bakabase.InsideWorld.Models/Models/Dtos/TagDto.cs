using System;
using System.Collections.Generic;
using System.Text;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record TagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? PreferredAlias { get; set; }
        public string? Color { get; set; }
        public int Order { get; set; }
        public int GroupId { get; set; }
        public string? GroupName { get; set; }
        public string? GroupNamePreferredAlias { get; set; }

        public string DisplayName
        {
            get
            {
                var sb = new StringBuilder();
                var gn = GroupNamePreferredAlias ?? GroupName;
                if (gn.IsNotEmpty())
                {
                    sb.Append(gn).Append(BusinessConstants.TagNameSeparator);
                }

                sb.Append(PreferredAlias ?? Name);
                return sb.ToString();
            }
        }

        private sealed class BizEqualityComparer : IEqualityComparer<TagDto>
        {
            public bool Equals(TagDto? x, TagDto? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(x.GroupName, y.GroupName, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(TagDto obj)
            {
                var hashCode = new HashCode();
                hashCode.Add(obj.Name, StringComparer.OrdinalIgnoreCase);
                hashCode.Add(obj.GroupName, StringComparer.OrdinalIgnoreCase);
                return hashCode.ToHashCode();
            }
        }

        public static IEqualityComparer<TagDto> BizComparer { get; } = new BizEqualityComparer();
    }
}