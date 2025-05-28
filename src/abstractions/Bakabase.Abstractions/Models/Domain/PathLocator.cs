using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record PathLocator
{
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
}
