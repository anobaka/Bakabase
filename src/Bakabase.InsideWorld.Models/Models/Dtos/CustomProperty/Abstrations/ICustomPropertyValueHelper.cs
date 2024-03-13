using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
	public interface ICustomPropertyValueHelper
	{
		CustomPropertyType Type { get; }
		CustomPropertyValueDto? ToDto(Entities.CustomPropertyValue? value);
	}
}
