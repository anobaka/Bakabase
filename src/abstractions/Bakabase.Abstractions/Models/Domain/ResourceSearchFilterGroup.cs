using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceSearchFilterGroup : IFilterExtractable<ResourceSearchFilterGroup, ResourceSearchFilter>
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceSearchFilterGroup>? Groups { get; set; }
    public List<ResourceSearchFilter>? Filters { get; set; }
}