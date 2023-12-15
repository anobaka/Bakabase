using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class OriginalExtensions
    {
        public static List<string> AnalyzeOriginals(this string originalString)
        {
            return string.IsNullOrEmpty(originalString)
                ? null
                : originalString.Split(new[] {'、'}, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static string BuildOriginalsString(this List<OriginalDto> originals, bool includeBracket = true)
        {
            if (originals?.Any() == true)
            {
                var t = string.Join("、", originals.Select(a => a.Name));
                if (includeBracket)
                {
                    t = $"({t})";
                }

                return t;
            }

            return null;
        }

        public static string ConvertToFeatureValue(this List<OriginalDto> originals)
        {
            return originals?.Any() == true ? originals.Count.ToString() : null;
        }

        public static OriginalDto? ToDto(this Original? original)
        {
            if (original == null)
            {
                return null;
            }

            return new()
            {
                Id = original.Id,
                Name = original.Name
            };
        }

        public static List<ResourceDiff>? Compare(this List<OriginalDto>? a, List<OriginalDto>? b)
        {
            return ResourceDiff.Build(ResourceDiffProperty.Original, a.PairByString(b, o => o.Name),
                OriginalDto.BizComparer, nameof(ResourceDto.Originals), Compare);
        }

        public static List<ResourceDiff>? Compare(this OriginalDto a, OriginalDto b)
        {
            var nameDiff = ResourceDiff.Build(ResourceDiffProperty.Original, a.Name, b.Name,
                StringComparer.OrdinalIgnoreCase, nameof(PublisherDto.Name), null);

            return nameDiff != null ? new List<ResourceDiff> {nameDiff} : null;
        }

        public static List<OriginalDto> Merge(this List<OriginalDto> originalsA, List<OriginalDto> originalsB)
        {
            return originalsA.Concat(originalsB).GroupBy(a => a.Name).Select(a => a.First() with { }).ToList();
        }
    }
}