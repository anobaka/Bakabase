using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Models;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number;

public record PercentagePropertyOptions
{
    public int Precision { get; set; }
    public bool ShowProgressBar { get; set; }
}

public record PercentageProperty() : CustomProperty<PercentagePropertyOptions>;

public record PercentagePropertyValue : CustomPropertyValue<decimal?>;

public class PercentagePropertyDescriptor(IStandardValueHelper standardValueHelper) : NumberPropertyDescriptor<PercentageProperty, PercentagePropertyOptions,
    PercentagePropertyValue>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Percentage;
}