using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Time
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
