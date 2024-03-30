using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Time
{
    internal class TimePropertyDescriptor: AbstractCustomPropertyDescriptor<TimeProperty, TimePropertyValue, TimeSpan>
    {
        public override CustomPropertyType Type => CustomPropertyType.Time;
        protected override bool IsMatch(TimeSpan value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
