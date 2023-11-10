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
            var pairs = a.Pair(b, TagDto.BizComparer, null, 0);
            return ResourceDiff.Build(ResourceProperty.Tag, pairs, TagDto.BizComparer, null, Compare);
        }

        public static List<ResourceDiff>? Compare(this TagDto a, TagDto b)
        {
            var groupNameDiff = ResourceDiff.Build(ResourceProperty.Tag, a.GroupName, b.GroupName,
                StringComparer.OrdinalIgnoreCase, nameof(TagDto.GroupName), null);
            var nameDiff = ResourceDiff.Build(ResourceProperty.Tag, a.Name, b.Name, StringComparer.OrdinalIgnoreCase,
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
}