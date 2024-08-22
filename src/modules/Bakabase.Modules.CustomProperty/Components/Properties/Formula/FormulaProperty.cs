using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Formula;

public record FormulaProperty() : Abstractions.Models.CustomProperty;

public record FormulaPropertyValue() : CustomPropertyValue<string>;

public class FormulaPropertyDescriptor(IStandardValueHelper standardValueHelper)
    : AbstractCustomPropertyDescriptor<FormulaProperty, FormulaPropertyValue, string, string>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Formula;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
    ];

    protected override bool IsMatch(string? value, SearchOperation operation, object? filterValue)
    {
        return operation switch
        {
            SearchOperation.IsNull => string.IsNullOrEmpty(value),
            SearchOperation.IsNotNull => !string.IsNullOrEmpty(value),
            _ => true
        };
    }
}