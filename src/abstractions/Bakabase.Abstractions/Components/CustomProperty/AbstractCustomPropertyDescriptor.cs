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
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TInnerValue> : ICustomPropertyDescriptor
        where TProperty : Models.Domain.CustomProperty, new()
        where TPropertyValue : TypedCustomPropertyValue<TInnerValue>, new()
    {
        public abstract CustomPropertyType Type { get; }

        public virtual Models.Domain.CustomProperty? BuildDomainProperty(Models.Db.CustomProperty? customProperty)
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

        public virtual CustomPropertyValue? BuildDomainValue(Models.Db.CustomPropertyValue? value)
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
                    : JsonConvert.DeserializeObject<TInnerValue>(value.Value)
            };

            return dto;
        }

        public bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter)
        {
            throw new System.NotImplementedException();
        }

        public abstract SearchOperation[] SearchOperations { get; }

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

        protected abstract bool IsMatch(TInnerValue? value, CustomPropertyValueSearchRequestModel model);
    }

    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyOptions, TPropertyValue, TValue> :
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TValue>
        where TProperty : CustomProperty<TPropertyOptions>, new()
        where TPropertyValue : TypedCustomPropertyValue<TValue>, new()
        where TPropertyOptions : new()
    {
        public override Models.Domain.CustomProperty? BuildDomainProperty(Models.Db.CustomProperty? customProperty)
        {
            var p = base.BuildDomainProperty(customProperty);
            if (p is TProperty sp)
            {
                try
                {
                    sp.Options = string.IsNullOrEmpty(customProperty!.Options)
                        ? new()
                        : JsonConvert.DeserializeObject<TPropertyOptions>(customProperty.Options);
                }
                catch (Exception e)
                {
                    sp.Options = new();
                }

            }

            return p;
        }
    }
}