using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record PathLocator
{
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
}
