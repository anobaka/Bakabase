using System;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Text;

public record LinkData
{
    public string? Text { get; set; }
    public string? Url { get; set; }
}

public record LinkProperty() : CustomPropertyDto;

public record LinkPropertyValue: CustomPropertyValueDto<LinkData>
{
    protected override bool IsMatch(LinkData? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}

public class LinkPropertyDescriptor: AbstractCustomPropertyDescriptor<LinkProperty, LinkPropertyValue, LinkData>
{
    public override CustomPropertyType Type => CustomPropertyType.Link;
    protected override bool IsMatch(LinkData? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}