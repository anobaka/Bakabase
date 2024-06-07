using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Abstractions.Models.Domain
{
    public record PathConfiguration
    {
        public string Path { get; set; } = null!;
        public List<PropertyPathSegmentMatcherValue>? RpmValues { get; set; }

        public static PathConfiguration CreateDefault(string rootPath)
        {
            return new PathConfiguration
            {
                Path = rootPath
            };
        }
    }
}