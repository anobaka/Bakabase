using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Time
{
    internal record TimePropertyValue: CustomPropertyValueDto<TimeSpan>
    {
        protected override bool IsMatch(TimeSpan value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
