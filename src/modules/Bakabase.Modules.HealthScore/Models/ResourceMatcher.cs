using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.HealthScore.Models;

/// <summary>
/// And/or/not predicate tree over a Resource. Leaves are property-value or
/// file-predicate checks. Used by health-score rules and the membership filter.
/// </summary>
public record ResourceMatcher : IFilterExtractable<ResourceMatcher, ResourceMatcherLeaf>
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceMatcher>? Groups { get; set; }
    public List<ResourceMatcherLeaf>? Leaves { get; set; }
    public bool Disabled { get; set; }

    List<ResourceMatcher>? IFilterExtractable<ResourceMatcher, ResourceMatcherLeaf>.Groups => Groups;
    List<ResourceMatcherLeaf>? IFilterExtractable<ResourceMatcher, ResourceMatcherLeaf>.Filters => Leaves;
}
