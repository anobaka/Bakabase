using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Helpers;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Components.Properties
{
    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TDbValue, TBizValue> : ICustomPropertyDescriptor
        where TProperty : Models.CustomProperty, new()
        where TPropertyValue : CustomPropertyValue<TDbValue>, new()
    {
        public StandardValueType DbValueType => EnumType.GetDbValueType();
        public StandardValueType BizValueType => EnumType.GetBizValueType();
        public abstract CustomPropertyType EnumType { get; }

        public int Type => (int) EnumType;

        public virtual Models.CustomProperty? ToDomainModel(
            Bakabase.Abstractions.Models.Db.CustomProperty? customProperty)
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
                Type = (int) EnumType,
                DbValueType = DbValueType,
                EnumType = EnumType,
                BizValueType = BizValueType
            };
        }

        public virtual CustomPropertyValue? ToDomainModel(Bakabase.Abstractions.Models.Db.CustomPropertyValue? value)
        {
            if (value == null)
            {
                return null;
            }

            var innerValue = value.Value?.DeserializeAsStandardValue(DbValueType);

            var dto = new TPropertyValue
            {
                Id = value.Id,
                Property = null,
                PropertyId = value.PropertyId,
                ResourceId = value.ResourceId,
                TypedValue = innerValue == null ? default : (TDbValue) innerValue,
                Scope = value.Scope
            };

            return dto;
        }

        public (object? DbValue, bool PropertyChanged) PrepareDbValueFromBizValue(
            Bakabase.Abstractions.Models.Domain.CustomProperty property, object? bizValue)
        {
            if (bizValue is TBizValue typedBizValue)
            {
                return TypedPrepareDbValueFromBizValue(property.Cast<TProperty>(), typedBizValue);
            }

            return (null, false);
        }

        protected virtual (TDbValue? DbValue, bool PropertyChanged) TypedPrepareDbValueFromBizValue(TProperty property,
            TBizValue bizValue) => (bizValue is TDbValue x ? x : default, false);

        public bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter)
        {
            throw new NotImplementedException();
        }


        public object? ConvertDbValueToBizValue(Bakabase.Abstractions.Models.Domain.CustomProperty property,
            object? dbValue)
        {
            return dbValue is TDbValue typedDbValue
                ? TypedConvertDbValueToBizValue(property.Cast<TProperty>(), typedDbValue)
                : null;
        }

        protected virtual TBizValue? TypedConvertDbValueToBizValue(TProperty property, TDbValue value) =>
            value is TBizValue bizValue ? bizValue : default;

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

        protected abstract bool IsMatch(TDbValue? value, CustomPropertyValueSearchRequestModel model);
    }

    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyOptions, TPropertyValue, TDbValue, TBizValue> :
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TDbValue, TBizValue>
        where TProperty : CustomProperty<TPropertyOptions>, new()
        where TPropertyValue : CustomPropertyValue<TDbValue>, new()
        where TPropertyOptions : new()
    {
        public override Models.CustomProperty? ToDomainModel(
            Bakabase.Abstractions.Models.Db.CustomProperty? customProperty)
        {
            var p = base.ToDomainModel(customProperty);
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