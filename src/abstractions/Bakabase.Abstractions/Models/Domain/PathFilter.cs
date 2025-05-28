using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record PathFilter : PathLocator
{
    public PathFilterFsType? FsType { get; set; }
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}
