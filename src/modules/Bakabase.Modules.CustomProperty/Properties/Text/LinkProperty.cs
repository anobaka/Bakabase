using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

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

public record LinkProperty() : Abstractions.Models.Domain.CustomProperty;

public record LinkPropertyValue: TypedCustomPropertyValue<LinkData>
{
    protected override bool IsMatch(LinkData? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}

public class LinkPropertyDescriptor : AbstractCustomPropertyDescriptor<LinkProperty, LinkPropertyValue, LinkData>
{
    public override CustomPropertyType Type => CustomPropertyType.Link;

    protected override bool IsMatch(LinkData? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}