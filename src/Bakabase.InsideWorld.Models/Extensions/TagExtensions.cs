using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class TagExtensions
    {
        public static TagDto? ToDto(this Tag? tag, string? groupName, string? groupNamePreferredAlias)
        {
            if (tag == null)
            {
                return null;
            }

            return new()
            {
                Id = tag.Id,
                Name = tag.Name,
                Color = tag.Color,
                GroupName = groupName,
                GroupId = tag.GroupId,
                Order = tag.Order,
                GroupNamePreferredAlias = groupNamePreferredAlias
            };
        }

        public static List<ResourceDiff>? Compare(this List<TagDto>? a, List<TagDto>? b)
        {
            var pairs = a.Pair(b, TagDto.BizComparer, null, 0.6m);
            return ResourceDiff.BuildRootDiffs(ResourceDiffProperty.Tag, pairs, TagDto.BizComparer, null, Compare);
        }

        public static List<ResourceDiff>? Compare(this TagDto a, TagDto b)
        {
            var groupNameDiff = ResourceDiff.BuildRootDiff(ResourceDiffProperty.Tag, a.GroupName, b.GroupName,
                StringComparer.OrdinalIgnoreCase, nameof(TagDto.GroupName), null);
            var nameDiff = ResourceDiff.BuildRootDiff(ResourceDiffProperty.Tag, a.Name, b.Name,
                StringComparer.OrdinalIgnoreCase,
                nameof(TagDto.Name), null);
            if (groupNameDiff != null || nameDiff != null)
            {
                var diffs = new List<ResourceDiff>();
                if (groupNameDiff != null)
                {
                    diffs.Add(groupNameDiff);
                }

                if (nameDiff != null)
                {
                    diffs.Add(nameDiff);
                }

                return diffs;
            }

            return null;
        }

        public static List<TagDto> Merge(this List<TagDto> tagsA, List<TagDto> tagsB)
        {
            var list = new List<TagDto>();

            var nameMapA = tagsA.GroupBy(a => a.Name).ToDictionary(a => a.Key, a => a.First());
            var nameMapB = tagsB.GroupBy(a => a.Name).ToDictionary(a => a.Key, a => a.First());

            foreach (var (name, a) in nameMapA)
            {
                if (nameMapB.TryGetValue(name, out var b))
                {
                    var copy = a with
                    {
                        Name = b.Name,
                        GroupName = b.GroupName
                    };

                    list.Add(copy);
                }
                else
                {
                    list.Add(a with { });
                }
            }

            foreach (var (name, b) in nameMapB.Where(b => !nameMapA.ContainsKey(b.Key)))
            {
                list.Add(b with { });
            }

            return list;
        }

        public static TagDto Clone(this TagDto tag)
        {
            return new()
            {
                Id = tag.Id,
                Name = tag.Name,
                Color = tag.Color,
                GroupName = tag.GroupName,
                GroupId = tag.GroupId,
                Order = tag.Order,
                GroupNamePreferredAlias = tag.GroupNamePreferredAlias
            };
        }
    }

    public static class TagGroupExtensions
    {
        public static TagGroupDto? ToDto(this TagGroup? group)
        {
            if (group == null)
            {
                return null;
            }

            return new TagGroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Order = group.Order
            };
        }

        public static TagGroupDto Clone(this TagGroupDto group)
        {
            return new()
            {
                Id = group.Id,
                Name = group.Name,
                Order = group.Order
            };
        }
    }

    public class TagGroupNameAndNameEqualityComparer : IEqualityComparer<TagDto>
    {
        public static TagGroupNameAndNameEqualityComparer Instance = new();

        public bool Equals(TagDto? x, TagDto? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            if (!string.IsNullOrEmpty(x.GroupName) && !string.IsNullOrEmpty(y.GroupName))
            {
                return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
                       x.GroupName.Equals(y.GroupName, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && x.GroupName.IsNullOrEmpty() &&
                   y.GroupName.IsNullOrEmpty();
        }

        public int GetHashCode(TagDto obj)
        {
            return HashCode.Combine(obj.Name, obj.GroupName);
        }
    }
}