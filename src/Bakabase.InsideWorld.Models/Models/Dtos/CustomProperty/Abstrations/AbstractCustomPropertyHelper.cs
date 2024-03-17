using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations
{
	public abstract class AbstractCustomPropertyHelper<TProperty> : ICustomPropertyHelper
		where TProperty : CustomPropertyDto, new()
	{
		public abstract CustomPropertyType Type { get; }

		public virtual CustomPropertyDto? ToDto(Entities.CustomProperty? customProperty)
		{
			if (customProperty == null)
			{
				return null;
			}

			return new TProperty
			{
				Categories = null,
				CreatedAt = customProperty.CreatedAt,
				Id = customProperty.Id,
				Name = customProperty.Name,
				Type = customProperty.Type,
			};
		}
	}

	public abstract class AbstractCustomPropertyHelper<TProperty, TOptions> : AbstractCustomPropertyHelper<TProperty>
		where TProperty : CustomPropertyDto<TOptions>, new()
	{
		public override CustomPropertyDto? ToDto(Entities.CustomProperty? customProperty)
		{
			var p = base.ToDto(customProperty);
			if (p is TProperty sp)
			{
				sp.Options = string.IsNullOrEmpty(customProperty!.Options)
					? default
					: JsonConvert.DeserializeObject<TOptions>(customProperty.Options);
			}

			return p;
		}
	}
}