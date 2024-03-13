using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;

public interface ICustomPropertyValue
{
	bool IsMatch(CustomPropertyValueSearchRequestModel model);
}