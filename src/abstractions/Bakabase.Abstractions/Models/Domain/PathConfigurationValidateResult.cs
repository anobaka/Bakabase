using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain
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

            public record SegmentMatchResult(string SegmentText, List<SegmentPropertyResult> Properties)
            {
                public string SegmentText { get; set; } = SegmentText;
                public List<SegmentPropertyResult> Properties { get; set; } = Properties;
            }

            public record GlobalMatchedValue(SegmentPropertyResult Property, List<string> TextValues)
            {
                public SegmentPropertyResult Property { get; set; } = Property;
                public List<string> TextValues { get; set; } = TextValues.Distinct().ToList();
            }

            public record SegmentPropertyResult(
                bool IsReserved,
                int Id,
                string? Name,
                object? PropertyValue,
                StandardValueType ValueType)
            {
                public int Id { get; set; } = Id;
                public bool IsReserved { get; set; } = IsReserved;
                public string? Name { get; set; } = Name;
                public object? PropertyValue { get; set; } = PropertyValue;
                public StandardValueType ValueType { get; set; } = ValueType;
            }
        }
    }
}