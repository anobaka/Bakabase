using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.CustomProperty.Properties.Text;

public record LinkData
{
    public string? Text { get; set; }
    public string? Url { get; set; }

    public override string? ToString()
    {
        if (string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(Url))
        {
            return null;
        }

        if (string.IsNullOrEmpty(Text))
        {
            return Url;
        }

        if (string.IsNullOrEmpty(Url))
        {
            return Text;
        }

        return "[{Text}]({Url})";
    }
}

public record LinkProperty() : Bakabase.Abstractions.Models.Domain.CustomProperty;

public record LinkPropertyValue : TypedCustomPropertyValue<LinkData>;

public class LinkPropertyDescriptor : TextPropertyDescriptor<LinkPropertyValue, LinkData>
{
    public override CustomPropertyType Type => CustomPropertyType.Link;

    protected override string[] GetMatchSources(LinkData? value)
    {
        return new[] {value?.Text, value?.Url}.Where(s => !string.IsNullOrEmpty(s)).ToArray()!;
    }
}