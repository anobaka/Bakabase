using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Models.Domain
{
    public record PathConfiguration
    {
        public string Path { get; set; } = null!;
        public List<PropertyPathSegmentMatcherValue>? RpmValues { get; set; }
        [Obsolete] public int[]? FixedTagIds { get; set; }
        [Obsolete] public List<TagDto>? FixedTags { get; set; }

        public static PathConfiguration CreateDefault(string rootPath)
        {
            return new PathConfiguration
            {
                Path = rootPath
            };
        }
    }
}