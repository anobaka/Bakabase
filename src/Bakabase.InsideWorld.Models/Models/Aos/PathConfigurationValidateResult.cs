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

                public record SegmentPropertyResult(bool IsReserved, int Id, string? Name)
                {
                    public int Id { get; set; } = Id;
                    public bool IsReserved { get; set; } = IsReserved;
                    public string? Name { get; set; } = Name;
                    public object? Value { get; set; }
                    public StandardvalueType
                }
            }

            public record GlobalMatchedValue(
                bool IsReservedProperty,
                int PropertyId,
                string? PropertyName,
                HashSet<string> V)
            {
                public int PropertyId { get; set; } = PropertyId;
                public bool IsReservedProperty { get; set; } = IsReservedProperty;
                public string? PropertyName { get; set; } = PropertyName;
                public List<string> Values { get; set; } = V.Distinct().ToList();
            }
        }
    }
}