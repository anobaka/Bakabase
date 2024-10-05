using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models;
using Bakabase.Modules.Property.Components.Properties.Number.Abstractions;

namespace Bakabase.Modules.Property.Components.Properties.Number;

public record PercentagePropertyOptions
{
    public int Precision { get; set; }
    public bool ShowProgressBar { get; set; }
}

public class PercentagePropertyDescriptor : NumberPropertyDescriptor<PercentagePropertyOptions>
{
    public override PropertyType Type => PropertyType.Percentage;
}