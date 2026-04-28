using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.HealthScore.Models.Input;

namespace Bakabase.Modules.HealthScore.Extensions;

/// <summary>
/// Input → DB structural copy. The two shapes are intentionally identical
/// (both use <see cref="ResourceMatcherLeafDbModel.PropertyValue"/>-style
/// StandardValue strings) so this is a pure rename. They are kept as
/// separate types only to avoid leaking "DbModel" naming into the API
/// contract.
/// </summary>
public static class HealthScoreInputExtensions
{
    public static HealthScoreRuleDbModel ToDbModel(this HealthScoreRuleInputModel input) => new()
    {
        Id = input.Id,
        Name = input.Name,
        Delta = input.Delta,
        Match = input.Match.ToDbModel(),
    };

    public static ResourceMatcherDbModel ToDbModel(this ResourceMatcherInputModel input) => new()
    {
        Combinator = input.Combinator,
        Disabled = input.Disabled,
        Groups = input.Groups?.Select(g => g.ToDbModel()).ToList(),
        Leaves = input.Leaves?.Select(l => l.ToDbModel()).ToList(),
    };

    public static ResourceMatcherLeafDbModel ToDbModel(this ResourceMatcherLeafInputModel input) => new()
    {
        Kind = input.Kind,
        Negated = input.Negated,
        Disabled = input.Disabled,
        PropertyPool = input.PropertyPool,
        PropertyId = input.PropertyId,
        Operation = input.Operation,
        PropertyValue = input.PropertyValue,
        FilePredicateId = input.FilePredicateId,
        FilePredicateParametersJson = input.FilePredicateParametersJson,
    };
}
