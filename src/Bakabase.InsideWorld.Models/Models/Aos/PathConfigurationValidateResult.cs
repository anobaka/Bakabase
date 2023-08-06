using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record PathConfigurationValidateResult(List<PathConfigurationValidateResult.Entry> Entries)
    {
        public List<Entry> Entries { get; set; } = Entries;
        // public int ConflictSegmentConfigurationCount { get; set; }

        public record Entry(bool IsDirectory, string RelativePath)
        {
            public bool IsDirectory { get; set; } = IsDirectory;
            public string RelativePath { get; set; } = RelativePath;
            public List<TagDto> FixedTags { get; set; } = new();
            public List<SegmentMatchResult> SegmentAndMatchedValues { get; set; } = new();
            public Dictionary<int, HashSet<string>> GlobalMatchedValues { get; set; } = new();

            public record SegmentMatchResult(string Value)
            {
                public string Value { get; set; } = Value;
                public HashSet<ResourceProperty> Properties { get; set; } = new();
            }
        }
    }
}