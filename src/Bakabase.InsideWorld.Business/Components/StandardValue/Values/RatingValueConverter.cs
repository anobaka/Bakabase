using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class RatingValueConverter : NumberValueConverter
    {
        public override StandardValueType Type => StandardValueType.Rating;
    }
}
