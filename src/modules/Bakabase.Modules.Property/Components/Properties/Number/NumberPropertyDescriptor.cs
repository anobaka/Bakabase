using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Number.Abstractions;

namespace Bakabase.Modules.Property.Components.Properties.Number;

public record NumberPropertyOptions
{
    public int Precision { get; set; }
}

public class NumberPropertyDescriptor : NumberPropertyDescriptor<NumberPropertyOptions>
{
    public override PropertyType Type => PropertyType.Number;
}