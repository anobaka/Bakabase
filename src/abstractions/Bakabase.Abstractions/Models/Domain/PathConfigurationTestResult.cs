using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record PathConfigurationTestResult(string RootPath, List<PathConfigurationTestResult.Resource> Resources, Dictionary<int, CustomProperty> CustomPropertyMap)
    {
        public string RootPath { get; set; } = RootPath;
        public List<Resource> Resources { get; set; } = Resources;
        public Dictionary<int, CustomProperty> CustomPropertyMap { get; set; } = CustomPropertyMap;

        public record Resource(bool IsDirectory, string RelativePath)
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

            public record SegmentPropertyKey(bool IsCustom, int Id)
            {
                public int Id { get; set; } = Id;
                public bool IsCustom { get; set; } = IsCustom;
            }

            /// <summary>
            /// Value should be standardized BizValue
            /// </summary>
            public Dictionary<int, object?> CustomPropertyIdValueMap { get; set; } = [];
        }
    }
}