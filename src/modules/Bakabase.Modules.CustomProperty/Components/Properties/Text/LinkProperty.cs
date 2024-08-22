using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Text.Abstractions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Text;

public record LinkProperty() : CustomProperty.Abstractions.Models.CustomProperty;

public record LinkPropertyValue : CustomPropertyValue<LinkValue>;

public class LinkPropertyDescriptor(IStandardValueHelper standardValueHelper) : TextPropertyDescriptor<LinkPropertyValue, LinkValue>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Link;

    protected override string[] GetMatchSources(LinkValue? value)
    {
        return new[] { value?.Text, value?.Url }.Where(s => !string.IsNullOrEmpty(s)).ToArray()!;
    }
}