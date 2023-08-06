using System;
using System.Collections.Generic;
using System.Linq;
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

        public static OriginalDto ToDto(this Original original)
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
    }
}