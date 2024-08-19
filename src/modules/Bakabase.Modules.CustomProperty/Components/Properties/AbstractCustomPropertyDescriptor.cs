using Bakabase.Abstractions.Exceptions;
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
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Components.Properties
{
    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TDbValue, TBizValue>(
            IStandardValueHelper standardValueHelper) : ICustomPropertyDescriptor
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

            var innerValue = standardValueHelper.Deserialize(value.Value, DbValueType);

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

        public ResourceSearchFilter? BuildSearchFilterByKeyword(
            Bakabase.Abstractions.Models.Domain.CustomProperty property, string keyword)
        {
            if (property.Type != Type || property is not TProperty typedProperty)
            {
                throw new DevException(
                    $"Property is not compatible with descriptor. Property: {JsonConvert.SerializeObject(property)}. Descriptor: {EnumType}");
            }

            var sf = BuildSearchFilterByKeyword(typedProperty, keyword);
            if (!sf.HasValue)
            {
                return null;
            }

            return new ResourceSearchFilter
            {
                Operation = sf.Value.Operation,
                DbValue = sf.Value.DbValue.SerializeAsStandardValue(),
                IsCustomProperty = true,
                PropertyId = property.Id
            };
        }

        protected virtual (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeyword(TProperty property,
            string keyword) => null;

        public bool IsMatch(CustomPropertyValue? value, ResourceSearchFilter filter)
        {
            var typedValue = value as TPropertyValue;

            // simple pre-check
            if (typedValue == null || typedValue.TypedValue == null)
            {
                if (filter.Operation == SearchOperation.IsNull)
                {
                    return true;
                }
            }

            var filterValue = filter.DbValue?.DeserializeAsStandardValue(DbValueType);

            return IsMatch(typedValue == null ? default : typedValue.TypedValue, filter.Operation, filterValue);
        }

        protected abstract bool IsMatch(TDbValue? value, SearchOperation operation, object? filterValue);
    }

    public abstract class
        AbstractCustomPropertyDescriptor<TProperty, TPropertyOptions, TPropertyValue, TDbValue, TBizValue>(
            IStandardValueHelper standardValueHelper) :
        AbstractCustomPropertyDescriptor<TProperty, TPropertyValue, TDbValue, TBizValue>(standardValueHelper)
        where TProperty : CustomProperty<TPropertyOptions>, new()
        where TPropertyValue : CustomPropertyValue<TDbValue>, new()
        where TPropertyOptions : class, new()
    {
        public override Models.CustomProperty? ToDomainModel(
            Bakabase.Abstractions.Models.Db.CustomProperty? customProperty)
        {
            var p = base.ToDomainModel(customProperty);
            if (p is TProperty sp)
            {
                sp.Options = customProperty!.Options?.DeserializeAsCustomPropertyOptions<TPropertyOptions>() ??
                             new TPropertyOptions();
            }

            return p;
        }
    }
}