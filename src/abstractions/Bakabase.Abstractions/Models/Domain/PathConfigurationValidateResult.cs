using Bakabase.Abstractions.Models.Domain.Constants;
using static Bakabase.Abstractions.Models.Domain.PathConfigurationValidateResult.Entry;

namespace Bakabase.Abstractions.Models.Domain
{
    public record PathConfigurationValidateResult(string RootPath, List<PathConfigurationValidateResult.Entry> Entries, Dictionary<int, CustomProperty> CustomPropertyMap)
    {
        public string RootPath { get; set; } = RootPath;
        public List<Entry> Entries { get; set; } = Entries;
        public Dictionary<int, CustomProperty> CustomPropertyMap { get; set; } = CustomPropertyMap;

        public record Entry(bool IsDirectory, string RelativePath)
        {
            public bool IsDirectory { get; set; } = IsDirectory;
            public string RelativePath { get; set; } = RelativePath;

            /// <summary>
            /// Relative segments
            /// </summary>
            public List<SegmentMatchResult> SegmentAndMatchedValues { get; set; } = new();

            public List<GlobalMatchedValue> GlobalMatchedValues { get; set; } = new();

            public record SegmentMatchResult(string SegmentText, List<SegmentPropertyKey> PropertyKeys)
            {
                public string SegmentText { get; set; } = SegmentText;
                public List<SegmentPropertyKey> PropertyKeys { get; set; } = PropertyKeys;
            }

            public record GlobalMatchedValue(SegmentPropertyKey PropertyKey, HashSet<string> TextValues)
            {
                public SegmentPropertyKey PropertyKey { get; set; } = PropertyKey;
                public HashSet<string> TextValues { get; set; } = TextValues;
            }

            public record SegmentPropertyKey(bool IsReserved, int Id)
            {
                public int Id { get; set; } = Id;
                public bool IsReserved { get; set; } = IsReserved;
            }

            public Dictionary<int, object?> CustomPropertyIdValueMap { get; set; } = [];
        }
    }
}