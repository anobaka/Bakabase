using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Components.Properties.Text.Abstractions;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Text;

public record LinkProperty() : Models.CustomProperty;

public record LinkPropertyValue : CustomPropertyValue<LinkValue>;

public class LinkPropertyDescriptor : TextPropertyDescriptor<LinkPropertyValue, LinkValue>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Link;

    protected override string[] GetMatchSources(LinkValue? value)
    {
        return new[] { value?.Text, value?.Url }.Where(s => !string.IsNullOrEmpty(s)).ToArray()!;
    }
}