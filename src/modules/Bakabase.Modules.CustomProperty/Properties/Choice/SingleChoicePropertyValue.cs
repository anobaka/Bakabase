using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Choice
{
    public record SingleChoicePropertyValue : TypedCustomPropertyValue<string>
    {
        protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
