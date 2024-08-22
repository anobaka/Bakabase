using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;

public record NumberPropertyOptions
{
    public int Precision { get; set; }
}

public record NumberProperty() : CustomProperty<NumberPropertyOptions>;

public record NumberPropertyValue : CustomPropertyValue<decimal?>;

public abstract class NumberPropertyDescriptor<TProperty, TOptions, TValue>(IStandardValueHelper standardValueHelper)
    : AbstractCustomPropertyDescriptor<
        TProperty,
        TOptions,
        TValue, decimal?, decimal?>(standardValueHelper) where TProperty : CustomProperty<TOptions>, new()
    where TOptions : class, new()
    where TValue : CustomPropertyValue<decimal?>, new()
{
    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Equals,
        SearchOperation.NotEquals,
        SearchOperation.GreaterThan,
        SearchOperation.LessThan,
        SearchOperation.GreaterThanOrEquals,
        SearchOperation.LessThanOrEquals,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
    ];

    protected override bool IsMatch(decimal? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            case SearchOperation.GreaterThan:
            case SearchOperation.LessThan:
            case SearchOperation.GreaterThanOrEquals:
            case SearchOperation.LessThanOrEquals:
                if (filterValue is not decimal typedTarget)
                {
                    return true;
                }

                return operation switch
                {
                    SearchOperation.Equals => value == typedTarget,
                    SearchOperation.NotEquals => value != typedTarget,
                    SearchOperation.GreaterThan => value > typedTarget,
                    SearchOperation.LessThan => value < typedTarget,
                    SearchOperation.GreaterThanOrEquals => value >= typedTarget,
                    SearchOperation.LessThanOrEquals => value <= typedTarget,
                    _ => throw new ArgumentOutOfRangeException()
                };
            case SearchOperation.IsNull:
                return false;
            case SearchOperation.IsNotNull:
                return true;
            default:
                return true;
        }
    }
}