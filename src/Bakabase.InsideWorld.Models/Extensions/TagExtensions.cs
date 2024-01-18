using System;
using System.Collections.Generic;
using System.Linq;
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