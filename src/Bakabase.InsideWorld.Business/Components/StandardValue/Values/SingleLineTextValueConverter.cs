using Bakabase.InsideWorld.Business.Components.StandardValue.Values.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class SingleLineTextValueConverter : StringValueConverter
    {
        public override StandardValueType Type => StandardValueType.SingleLineText;
        public SingleLineTextValueConverter(SpecialTextService specialTextService) : base(specialTextService)
        {
        }
    }
}