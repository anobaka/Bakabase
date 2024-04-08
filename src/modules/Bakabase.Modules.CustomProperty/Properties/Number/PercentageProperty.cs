using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Number;

public record PercentagePropertyOptions
{
    public int Precision { get; set; }
    public bool ShowProgressBar { get; set; }
}

public record PercentageProperty() : CustomProperty<PercentagePropertyOptions>;

public record PercentagePropertyValue : TypedCustomPropertyValue<decimal>
{
    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class PercentagePropertyDescriptor : AbstractCustomPropertyDescriptor<PercentageProperty, PercentagePropertyOptions,
    PercentagePropertyValue, decimal>
{
    public override CustomPropertyType Type => CustomPropertyType.Percentage;

    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}