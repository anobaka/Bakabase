using Bakabase.InsideWorld.Business.Components.StandardValue.Values.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class MultipleChoiceValueConverter : ListStringValueConverter
    {
        public MultipleChoiceValueConverter(SpecialTextService specialTextService) : base(specialTextService)
        {
        }

        public override StandardValueType Type => StandardValueType.MultipleChoice;
    }
}
