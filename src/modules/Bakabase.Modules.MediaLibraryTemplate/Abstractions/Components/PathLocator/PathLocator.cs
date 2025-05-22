namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

public record PathLocator
{
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
}
