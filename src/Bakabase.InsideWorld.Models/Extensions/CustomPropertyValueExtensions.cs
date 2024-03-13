using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Extensions
{
	public static class CustomPropertyValueExtensions
	{
		public static Dictionary<CustomPropertyType, ICustomPropertyValueHelper> Helpers = [];

		public static CustomPropertyValueDto? ToDto(this CustomPropertyValue? entity)
		{
			if (entity == null)
			{
				return null;
			}

			return Helpers[entity.Property.Type].ToDto(entity);
		}
	}
}
