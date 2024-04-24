using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Properties.Number.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Number;

public record PercentagePropertyOptions
{
    public int Precision { get; set; }
    public bool ShowProgressBar { get; set; }
}

public record PercentageProperty() : CustomProperty<PercentagePropertyOptions>;

public record PercentagePropertyValue : CustomPropertyValue<decimal>;

public class PercentagePropertyDescriptor : NumberPropertyDescriptor<PercentageProperty, PercentagePropertyOptions,
    PercentagePropertyValue>
{
    public override CustomPropertyType Type => CustomPropertyType.Percentage;
}