using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;

public record PathFilter(
    PathPositioner Positioner,
    int? Layer,
    string? Regex,
    PathFilterFsType? FsType,
    HashSet<string>? Extensions) : PathLocator.PathLocator(Positioner, Layer, Regex);