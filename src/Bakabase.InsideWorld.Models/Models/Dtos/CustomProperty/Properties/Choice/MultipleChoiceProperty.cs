using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Choice;

public record MultipleChoiceProperty : ChoiceProperty<string[]>;

public record MultipleChoicePropertyValue : CustomPropertyValueDto<string[]>
{
    protected override bool IsMatch(string[]? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}

public class MultipleChoicePropertyDescriptor : AbstractCustomPropertyDescriptor<MultipleChoiceProperty, MultipleChoicePropertyValue, string[]>
{
    public override CustomPropertyType Type => CustomPropertyType.MultipleChoice;
    protected override bool IsMatch(string[]? value, CustomPropertyValueSearchRequestModel model)
    {
        throw new NotImplementedException();
    }
}