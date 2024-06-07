using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Models;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number;

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
    public override CustomPropertyType EnumType => CustomPropertyType.Percentage;
}