using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharablePathFilter
{
    public PathPositioner Positioner { get; set; }
    public int? Layer { get; set; }
    public string? Regex { get; set; }
    public PathFilterFsType? FsType { get; set; }
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}