using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class SeriesExtensions
    {
        public static SeriesDto ToDto(this Series s)
        {
            if (s == null)
            {
                return null;
            }

            return new SeriesDto
            {
                Id = s.Id,
                Name = s.Name
            };
        }

        public static List<ResourceDiff>? Compare(this SeriesDto? a, SeriesDto? b)
        {
            if (a?.Id > 0 && a.Id == b?.Id)
            {
                return null;
            }

            var nameDiff = ResourceDiff.BuildRootDiff(ResourceDiffProperty.Series, a?.Name, b?.Name,
                StringComparer.OrdinalIgnoreCase, nameof(SeriesDto.Name), null);
            return nameDiff == null ? null : new List<ResourceDiff> {nameDiff};
        }

        public static SeriesDto Clone(this SeriesDto series)
        {
            return series with { };
        }
    }
}