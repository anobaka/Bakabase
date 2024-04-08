using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Multilevel
{
    internal record MultilevelPropertyValue : TypedCustomPropertyValue<string>
    {
        protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
