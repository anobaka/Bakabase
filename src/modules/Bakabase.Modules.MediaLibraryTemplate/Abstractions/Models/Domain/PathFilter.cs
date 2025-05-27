using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record PathFilter : PathLocator
{
    public PathFilterFsType? FsType { get; set; }
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}
