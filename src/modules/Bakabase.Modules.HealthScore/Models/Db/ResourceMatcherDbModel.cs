using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Models.Db;

public record ResourceMatcherDbModel : IFilterExtractable<ResourceMatcherDbModel, ResourceMatcherLeafDbModel>
{
    public SearchCombinator Combinator { get; set; }
    public List<ResourceMatcherDbModel>? Groups { get; set; }
    public List<ResourceMatcherLeafDbModel>? Leaves { get; set; }
    public bool Disabled { get; set; }

    List<ResourceMatcherDbModel>? IFilterExtractable<ResourceMatcherDbModel, ResourceMatcherLeafDbModel>.Groups => Groups;
    List<ResourceMatcherLeafDbModel>? IFilterExtractable<ResourceMatcherDbModel, ResourceMatcherLeafDbModel>.Filters => Leaves;
}

public record ResourceMatcherLeafDbModel
{
    public ResourceMatcherLeafKind Kind { get; set; }
    public bool Negated { get; set; }
    public bool Disabled { get; set; }

    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public SearchOperation? Operation { get; set; }

    /// <summary>StandardValue-serialized form, see <see cref="Bakabase.Modules.StandardValue.Extensions"/>.</summary>
    public string? PropertyValue { get; set; }

    public string? FilePredicateId { get; set; }

    /// <summary>JSON-as-string. See <see cref="ResourceMatcherLeaf.FilePredicateParametersJson"/>.</summary>
    public string? FilePredicateParametersJson { get; set; }
}
