using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record PathConfiguration
    {
        public string? Path { get; set; }
        public List<PropertyPathSegmentMatcherValue>? RpmValues { get; set; }

        public static PathConfiguration CreateDefault(string rootPath)
        {
            return new PathConfiguration
            {
                Path = rootPath,
                RpmValues =
                [
                    new PropertyPathSegmentMatcherValue
                    {
                        Layer = 1, ValueType = ResourceMatcherValueType.Layer,
                        PropertyId = (int) ResourceProperty.Resource
                    }
                ]
            };
        }
    }
}