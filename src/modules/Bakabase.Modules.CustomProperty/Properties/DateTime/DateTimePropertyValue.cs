using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.DateTime
{
    public record DateTimePropertyValue : CustomPropertyValueDto<System.DateTime>
    {
        protected override bool IsMatch(System.DateTime value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
