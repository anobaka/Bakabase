using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain;
using Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.CustomProperty.Properties.Text;

public record LinkProperty() : Bakabase.Abstractions.Models.Domain.CustomProperty;

public record LinkPropertyValue : CustomPropertyValue<LinkData>;

public class LinkPropertyDescriptor : TextPropertyDescriptor<LinkPropertyValue, LinkData>
{
    public override StandardValueType ValueType => StandardValueType.Link;
    public override CustomPropertyType Type => CustomPropertyType.Link;

    protected override string[] GetMatchSources(LinkData? value)
    {
        return new[] {value?.Text, value?.Url}.Where(s => !string.IsNullOrEmpty(s)).ToArray()!;
    }
}