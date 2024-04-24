using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using CustomPropertyValue = Bakabase.Abstractions.Models.Domain.CustomPropertyValue;

namespace Bakabase.Modules.CustomProperty.Models.Domain
{
    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TInnerValue> : ICustomPropertyDescriptor
        where TProperty : Abstractions.Models.Domain.CustomProperty, new()
        where TPropertyValue : CustomPropertyValue<TInnerValue>, new()
    {
        public abstract StandardValueType ValueType { get; }
        public abstract CustomPropertyType Type { get; }

        public virtual Abstractions.Models.Domain.CustomProperty? BuildDomainProperty(
            Abstractions.Models.Db.CustomProperty? customProperty)
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

        public virtual CustomPropertyValue? BuildDomainValue(Abstractions.Models.Db.CustomPropertyValue? value)
        {
            if (value == null)
            {
                return null;
            }

            var innerValue = value.Value?.DeserializeAsStandardValue(ValueType);

            var dto = new TPropertyValue
            {
                Id = value.Id,
                Property = null,
                PropertyId = value.PropertyId,
                ResourceId = value.ResourceId,
                TypedValue = innerValue == null ? default : (TInnerValue)innerValue,
                Layer = value.Layer
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
            if (typedValue == null || typedValue.TypedValue == null)
            {
                if (model.Operation == SearchOperation.IsNull)
                {
                    return true;
                }
            }

            return IsMatch(typedValue == null ? default : typedValue.TypedValue, model);
        }

        protected abstract bool IsMatch(TInnerValue? value, CustomPropertyValueSearchRequestModel model);
    }

    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyOptions, TPropertyValue, TInnerValue> :
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TInnerValue>
        where TProperty : CustomProperty<TPropertyOptions>, new()
        where TPropertyValue : CustomPropertyValue<TInnerValue>, new()
        where TPropertyOptions : new()
    {
        public override Abstractions.Models.Domain.CustomProperty? BuildDomainProperty(
            Abstractions.Models.Db.CustomProperty? customProperty)
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