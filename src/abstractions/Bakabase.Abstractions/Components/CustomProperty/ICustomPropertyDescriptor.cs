using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Abstractions.Components.CustomProperty
{
	public interface ICustomPropertyDescriptor
	{
		CustomPropertyType Type { get; }
		Models.Domain.CustomProperty? BuildPropertyDto(Models.Db.CustomProperty? customProperty);
		CustomPropertyValue? BuildValueDto(Models.Db.CustomPropertyValue? value);
		bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter);
	}
}