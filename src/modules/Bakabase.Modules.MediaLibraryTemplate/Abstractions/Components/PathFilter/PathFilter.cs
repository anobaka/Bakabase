using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;

public record PathFilter : PathLocator.PathLocator
{
    public PathFilterFsType? FsType { get; set; }
    public int[]? ExtensionGroupIds { get; set; }
    public ExtensionGroup[]? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}
