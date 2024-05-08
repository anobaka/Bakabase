using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;

public record TextProperty : Models.CustomProperty;

public record TextPropertyValue : CustomPropertyValue<string>;

public abstract class
    TextPropertyDescriptor<TPropertyValue, TInnerValue> : AbstractCustomPropertyDescriptor<TextProperty, TPropertyValue, TInnerValue> where TPropertyValue : CustomPropertyValue<TInnerValue>, new()
{

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals,
        SearchOperation.NotEquals,
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.StartsWith,
        SearchOperation.NotStartsWith,
        SearchOperation.EndsWith,
        SearchOperation.NotEndsWith,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
        SearchOperation.Matches,
        SearchOperation.NotMatches
    ];

    protected abstract string[] GetMatchSources(TInnerValue? value);

    protected override bool IsMatch(TInnerValue? value, CustomPropertyValueSearchRequestModel model)
    {
        var candidates = GetMatchSources(value);
        switch (model.Operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
            case SearchOperation.StartsWith:
            case SearchOperation.NotStartsWith:
            case SearchOperation.EndsWith:
            case SearchOperation.NotEndsWith:
            case SearchOperation.Matches:
            case SearchOperation.NotMatches:
                {
                    var typedTarget = model.DeserializeValue<string>();
                    if (string.IsNullOrEmpty(typedTarget))
                    {
                        return true;
                    }

                    return candidates.Any(c =>
                    {
                        return model.Operation switch
                        {
                            SearchOperation.Equals => c.Equals(typedTarget),
                            SearchOperation.NotEquals => !c.Equals(typedTarget),
                            SearchOperation.Contains => c.Contains(typedTarget),
                            SearchOperation.NotContains => !c.Contains(typedTarget),
                            SearchOperation.StartsWith => c.StartsWith(typedTarget),
                            SearchOperation.NotStartsWith => !c.StartsWith(typedTarget),
                            SearchOperation.EndsWith => c.EndsWith(typedTarget),
                            SearchOperation.NotEndsWith => !c.EndsWith(typedTarget),
                            SearchOperation.Matches => Regex.IsMatch(c, typedTarget),
                            SearchOperation.NotMatches => !Regex.IsMatch(c, typedTarget),
                            _ => false
                        };
                    });
                }
            case SearchOperation.IsNull:
                return !candidates.Any();
            case SearchOperation.IsNotNull:
                return candidates.Any();
            default:
                return true;
        }
    }
}