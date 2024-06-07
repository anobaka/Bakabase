using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Models.Dto
{
    public class MediaLibraryPatchDto
    {
        public string? Name { get; set; }
        public List<PathConfiguration>? PathConfigurations { get; set; }
        public int? Order { get; set; }
    }
}