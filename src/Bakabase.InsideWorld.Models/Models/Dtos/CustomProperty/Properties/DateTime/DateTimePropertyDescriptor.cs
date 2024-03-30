using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.DateTime
{
    public class DateTimePropertyDescriptor: AbstractCustomPropertyDescriptor<DateTimeProperty, DateTimePropertyValue, System.DateTime>
    {
        public override CustomPropertyType Type => CustomPropertyType.DateTime;
        protected override bool IsMatch(System.DateTime value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
