using Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters
{
    public class MultilineTextValueConverter : StringValueConverter
    {
        public MultilineTextValueConverter(SpecialTextService specialTextService) : base(specialTextService)
        {
        }

        public override StandardValueType Type => StandardValueType.MultilineText;
    }
}
