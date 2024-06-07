using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Formula;

public record FormulaProperty() : Models.CustomProperty;

public record FormulaPropertyValue() : CustomPropertyValue<string>;

public class FormulaPropertyDescriptor : AbstractCustomPropertyDescriptor<FormulaProperty, FormulaPropertyValue, string, string>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Formula;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
    ];

    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        return model.Operation switch
        {
            SearchOperation.IsNull => string.IsNullOrEmpty(value),
            SearchOperation.IsNotNull => !string.IsNullOrEmpty(value),
            _ => true
        };
    }
}