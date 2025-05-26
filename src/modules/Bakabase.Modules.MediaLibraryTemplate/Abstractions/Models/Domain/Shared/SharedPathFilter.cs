using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;

public record SharedPathFilter : PathLocator
{
    public PathFilterFsType? FsType { get; set; }
    public ExtensionGroup[]? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}
