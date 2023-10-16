using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record PathConfigurationValidateResult(string RootPath, List<PathConfigurationValidateResult.Entry> Entries)
    {
        public string RootPath { get; set; } = RootPath;
        public List<Entry> Entries { get; set; } = Entries;

        public record Entry(bool IsDirectory, string RelativePath)
        {
            public bool IsDirectory { get; set; } = IsDirectory;
            public string RelativePath { get; set; } = RelativePath;

            /// <summary>
            /// Relative segments
            /// </summary>
            public List<SegmentMatchResult> SegmentAndMatchedValues { get; set; } = new();

            public List<GlobalMatchedValue> GlobalMatchedValues { get; set; } = new();

            public record SegmentMatchResult(string Value, List<SegmentMatchResult.SegmentPropertyResult> Properties)
            {
                public string Value { get; set; } = Value;
                public List<SegmentPropertyResult> Properties { get; set; } = Properties;

                public record SegmentPropertyResult(ResourceProperty Property, List<string> Keys)
                {
                    public ResourceProperty Property { get; set; } = Property;
                    public List<string> Keys { get; set; } = Keys.Distinct().ToList();
                }
            }

            public record GlobalMatchedValue(ResourceProperty Property, List<string> Values, string? Key)
            {
                public ResourceProperty Property { get; set; } = Property;

                /// <summary>
                ///  Custom Key
                /// </summary>
                public string? Key { get; set; } = Key;

                public List<string> Values { get; set; } = Values.Distinct().ToList();
            }
        }
    }
}