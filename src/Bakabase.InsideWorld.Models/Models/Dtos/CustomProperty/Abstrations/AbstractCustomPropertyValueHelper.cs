using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
	public abstract class AbstractCustomPropertyValueHelper<TValueDto, TValue> : ICustomPropertyValueHelper where TValueDto: CustomPropertyValueDto<TValue>, new()
	{
		public abstract CustomPropertyType Type { get; }
		public CustomPropertyValueDto? ToDto(CustomPropertyValue? value)
		{
			if (value == null)
			{
				return null;
			}

			var dto = new TValueDto
			{
				Id = value.Id,
				Property = null,
				PropertyId = value.PropertyId,
				ResourceId = value.ResourceId,
				Value = string.IsNullOrEmpty(value.Value) ? default
					: JsonConvert.DeserializeObject<TValue>(value.Value)
			};

			return dto;
		}
	}
}