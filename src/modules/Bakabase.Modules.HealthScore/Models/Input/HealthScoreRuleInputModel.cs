using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Models.Input;

/// <summary>
/// API input shape for a scoring rule. Mirrors the DB shape so values arrive
/// pre-serialized as StandardValue strings (see
/// <c>ResourceSearchFilterInputModel.DbValue</c> for the same convention).
/// </summary>
public class HealthScoreRuleInputModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ResourceMatcherInputModel Match { get; set; } = new();
    public decimal Delta { get; set; }
}

public class ResourceMatcherInputModel : IFilterExtractable<ResourceMatcherInputModel, ResourceMatcherLeafInputModel>
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceMatcherInputModel>? Groups { get; set; }
    public List<ResourceMatcherLeafInputModel>? Leaves { get; set; }
    public bool Disabled { get; set; }

    List<ResourceMatcherInputModel>? IFilterExtractable<ResourceMatcherInputModel, ResourceMatcherLeafInputModel>.Groups => Groups;
    List<ResourceMatcherLeafInputModel>? IFilterExtractable<ResourceMatcherInputModel, ResourceMatcherLeafInputModel>.Filters => Leaves;
}

public class ResourceMatcherLeafInputModel
{
    public ResourceMatcherLeafKind Kind { get; set; }
    public bool Negated { get; set; }
    public bool Disabled { get; set; }

    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public SearchOperation? Operation { get; set; }

    /// <summary>StandardValue-serialized form (frontend serializes before sending).</summary>
    public string? PropertyValue { get; set; }

    public string? FilePredicateId { get; set; }

    /// <summary>JSON string of the predicate's parameters (frontend stringifies before sending).</summary>
    public string? FilePredicateParametersJson { get; set; }
}
