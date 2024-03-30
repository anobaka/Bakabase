using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Text;

public record TextProperty : CustomPropertyDto;

public record TextPropertyValue: CustomPropertyValueDto<string>
{
    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}

public abstract class TextPropertyDescriptor : AbstractCustomPropertyDescriptor<TextProperty, TextPropertyValue, string>
{
    protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}