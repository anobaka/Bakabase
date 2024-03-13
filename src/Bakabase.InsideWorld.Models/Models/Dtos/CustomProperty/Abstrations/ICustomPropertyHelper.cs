using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
	public interface ICustomPropertyHelper
	{
		CustomPropertyType Type { get; }
		CustomPropertyDto? ToDto(Entities.CustomProperty? customProperty);
	}
}
