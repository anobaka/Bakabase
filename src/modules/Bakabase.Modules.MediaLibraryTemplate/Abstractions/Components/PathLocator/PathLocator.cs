namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

public record PathLocator(PathPositioner Positioner, int? Layer, string? Regex);