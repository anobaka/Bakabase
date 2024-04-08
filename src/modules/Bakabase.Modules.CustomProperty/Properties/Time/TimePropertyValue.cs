using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Time
{
    public record TimePropertyValue: TypedCustomPropertyValue<TimeSpan>
    {
        protected override bool IsMatch(TimeSpan value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
