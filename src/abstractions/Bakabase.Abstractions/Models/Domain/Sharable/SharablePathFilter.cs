using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharablePathFilter : PathLocator
{
    public PathFilterFsType? FsType { get; set; }
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}