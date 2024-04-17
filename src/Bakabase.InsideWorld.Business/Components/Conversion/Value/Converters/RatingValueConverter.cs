using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters
{
    public class RatingValueConverter : NumberValueConverter
    {
        public override StandardValueType Type => StandardValueType.Rating;
    }
}
