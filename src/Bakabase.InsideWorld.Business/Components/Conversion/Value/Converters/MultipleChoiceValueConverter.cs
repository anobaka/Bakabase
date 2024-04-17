using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters
{
    public class MultipleChoiceValueConverter : ListStringValueConverter
    {
        public MultipleChoiceValueConverter(SpecialTextService specialTextService) : base(specialTextService)
        {
        }

        public override StandardValueType Type => StandardValueType.MultipleChoice;
    }
}
