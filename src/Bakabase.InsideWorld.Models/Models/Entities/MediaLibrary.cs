using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class MediaLibrary
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        [Required] public int CategoryId { get; set; }
        public string? PathConfigurationsJson { get; set; }
        public int Order { get; set; }
        public int ResourceCount { get; set; }

        public class PathConfiguration
        {
            public string? Path { get; set; }

            [Obsolete("Resource matcher has been moved to ResourcePropertyMatcherValue")]
            public string? Regex { get; set; }

            public int[]? FixedTagIds { get; set; } = Array.Empty<int>();

            [Obsolete("Use RpmValues instead")]
            public List<SegmentMatcher>? Segments { get; set; } = new();

            /// <summary>
            /// Resource property matcher values
            /// </summary>
            public List<MatcherValue> RpmValues { get; set; } = new();

            public class SegmentMatcher : MatcherValue
            {
                [Obsolete("Use minus layer instead")] public bool IsReverse { get; set; }
                [Obsolete("Use Property instead")] public ResourceProperty Type { get; set; }
            }
        }
    }
}