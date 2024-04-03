using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Newtonsoft.Json;
using CustomPropertyValue = Bakabase.Abstractions.Models.Domain.CustomPropertyValue;

namespace Bakabase.Abstractions.Components.CustomProperty
{
	public abstract class
		AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TValue> : ICustomPropertyDescriptor
		where TProperty : Models.Domain.CustomProperty, new() where TPropertyValue : CustomPropertyValueDto<TValue>, new()
	{
		public abstract CustomPropertyType Type { get; }

		public virtual Models.Domain.CustomProperty? BuildPropertyDto(Models.Db.CustomProperty? customProperty)
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

        public virtual CustomPropertyValue? BuildValueDto(Models.Db.CustomPropertyValue? value)
		{
			if (value == null)
			{
				return null;
			}

			var dto = new TPropertyValue
			{
				Id = value.Id,
				Property = null,
				PropertyId = value.PropertyId,
				ResourceId = value.ResourceId,
				Value = string.IsNullOrEmpty(value.Value)
					? default
					: JsonConvert.DeserializeObject<TValue>(value.Value)
			};

			return dto;
		}

        public bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter)
        {
            throw new System.NotImplementedException();
        }

        public bool IsMatch(CustomPropertyValue? value, CustomPropertyValueSearchRequestModel model)
		{
			var typedValue = value as TPropertyValue;

			// simple pre-check
			if (typedValue == null || typedValue.Value == null)
			{
				if (model.Operation == SearchOperation.IsNull)
				{
					return true;
				}
			}

			return IsMatch(typedValue == null ? default : typedValue.Value, model);
		}

		protected abstract bool IsMatch(TValue? value, CustomPropertyValueSearchRequestModel model);
	}

	public abstract class
		AbstractCustomPropertyDescriptor<TProperty, TPropertyOptions, TPropertyValue, TValue> :
		AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TValue>
		where TProperty : CustomProperty<TPropertyOptions>, new()
		where TPropertyValue : CustomPropertyValueDto<TValue>, new()
	{
		public override Models.Domain.CustomProperty? BuildPropertyDto(Models.Db.CustomProperty? customProperty)
		{
			var p = base.BuildPropertyDto(customProperty);
			if (p is TProperty sp)
			{
				sp.Options = string.IsNullOrEmpty(customProperty!.Options)
					? default
					: JsonConvert.DeserializeObject<TPropertyOptions>(customProperty.Options);
			}

			return p;
		}
	}
}