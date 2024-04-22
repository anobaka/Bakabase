using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class PercentageValueConverter : NumberValueConverter
    {
        public override StandardValueType Type => StandardValueType.Percentage;
    }
}
