using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions
{
	public interface ICustomPropertyDescriptor
	{
		CustomPropertyType Type { get; }
		CustomPropertyDto? BuildPropertyDto(Entities.CustomProperty? customProperty);
		CustomPropertyValueDto? BuildValueDto(Entities.CustomPropertyValue? value);
		bool IsMatch(CustomPropertyValueDto? value, ResourceSearchFilter filter);
	}
}