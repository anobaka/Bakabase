using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.DateTime
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
