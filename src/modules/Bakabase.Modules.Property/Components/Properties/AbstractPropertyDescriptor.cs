using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.Property.Components.Properties
{
    public abstract class
        AbstractPropertyDescriptor<TDbValue, TBizValue> : IPropertyDescriptor,
        IPropertySearchHandler
    {
        public StandardValueType DbValueType => Type.GetDbValueType();
        public StandardValueType BizValueType => Type.GetBizValueType();

        public abstract PropertyType Type { get; }
        // public Bakabase.Abstractions.Models.Domain.Property ToDomainModel(CustomPropertyDbModel customProperty)
        // {
        //     return new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom, customProperty.Id, customProperty.Type, customProperty.Name, )
        //     {
        //         Categories = null,
        //         CreatedAt = customProperty.CreatedAt,
        //         Id = customProperty.Id,
        //         Name = customProperty.Name,
        //         Type = Type,
        //         DbValueType = DbValueType,
        //         BizValueType = BizValueType
        //     };
        // }
        //
        // public CustomPropertyValue ToDomainModel(CustomPropertyValueDbModel value)
        // {
        //     var innerValue = value.Value?.DeserializeAsStandardValue(DbValueType);
        //
        //     var dto = new TPropertyValue
        //     {
        //         Id = value.Id,
        //         Property = null,
        //         PropertyId = value.PropertyId,
        //         ResourceId = value.ResourceId,
        //         TypedValue = (TDbValue?) innerValue,
        //         Scope = value.Scope
        //     };
        //
        //     return dto;
        // }

        protected virtual void EnsureOptionsType(object? options)
        {
        }

        public (object? DbValue, bool PropertyChanged) PrepareDbValue(
            Bakabase.Abstractions.Models.Domain.Property property, object? bizValue)
        {
            EnsureOptionsType(property.Options);
            return bizValue is TBizValue typedBizValue
                ? PrepareDbValueInternal(property, typedBizValue)
                : (null, false);
        }

        protected virtual (TDbValue? DbValue, bool PropertyChanged) PrepareDbValueInternal(
            Bakabase.Abstractions.Models.Domain.Property property, TBizValue bizValue) =>
            (bizValue is TDbValue x ? x : default, false);

        public object? GetBizValue(Bakabase.Abstractions.Models.Domain.Property property, object? dbValue)
        {
            EnsureOptionsType(property.Options);
            return dbValue is TDbValue v ? GetBizValueInternal(property, v) : null;
        }

        protected virtual TBizValue? GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property,
            TDbValue value) => value is TBizValue bizValue ? bizValue : default;

        public virtual object? InitializeOptions() => null;
        public virtual Type? OptionsType => null;

        // public CustomPropertyValue InitializePropertyValue(object? dbValue, int resourceId, int propertyId, int scope)
        // {
        //     return new TPropertyValue
        //     {
        //         PropertyId = propertyId,
        //         Scope = scope,
        //         ResourceId = resourceId,
        //         Value = dbValue is TDbValue? ? dbValue : default,
        //     };
        // }

        public bool IsMatch(object? dbValue, SearchOperation operation, object? filterValue)
        {
            // validate filter value
            // todo: optimize filter value
            var expectedFilterValueType = SearchOperations.GetValueOrDefault(operation)?.AsType.GetDbValueType();
            if (expectedFilterValueType.HasValue)
            {
                if (!filterValue.IsStandardValueType(expectedFilterValueType.Value))
                {
                    return false;
                }
            }

            if (dbValue is TDbValue tv)
            {
                return operation switch
                {
                    SearchOperation.IsNull => false,
                    SearchOperation.IsNotNull => true,
                    _ => filterValue == null || IsMatchInternal(tv, operation, filterValue)
                };
            }

            return operation == SearchOperation.IsNull;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dbValue">This value is pre-validated and can be used safely.</param>
        /// <param name="operation"></param>
        /// <param name="filterValue">This value is pre-validated and can be used safely.</param>
        /// <returns></returns>
        protected abstract bool IsMatchInternal(TDbValue dbValue, SearchOperation operation, object filterValue);

        public abstract Dictionary<SearchOperation, PropertySearchOperationOptions?> SearchOperations { get; }

        public ResourceSearchFilter? BuildSearchFilterByKeyword(Bakabase.Abstractions.Models.Domain.Property property,
            string keyword)
        {
            if (property.Type != Type)
            {
                throw new DevException(
                    $"Property is not compatible with descriptor. Property: {JsonConvert.SerializeObject(property)}. Descriptor: {Type}");
            }

            var sf = BuildSearchFilterByKeywordInternal(property, keyword);
            if (!sf.HasValue)
            {
                return null;
            }

            var dbValueType = SearchOperations.GetValueOrDefault(sf.Value.Operation)?.AsType.GetDbValueType();
            if (!dbValueType.HasValue)
            {
                return null;
            }

            return new ResourceSearchFilter
            {
                Operation = sf.Value.Operation,
                DbValue = sf.Value.DbValue.SerializeAsStandardValue(dbValueType.Value),
                PropertyPool = PropertyPool.Custom,
                PropertyId = property.Id,
                Property = property
            };
        }

        protected virtual (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
            Bakabase.Abstractions.Models.Domain.Property property, string keyword) => null;
    }

    public abstract class
        AbstractPropertyDescriptor<TPropertyOptions, TDbValue, TBizValue> : AbstractPropertyDescriptor<TDbValue,
        TBizValue>
        where TPropertyOptions : class, new()
    {
        // public override CustomProperty ToDomainModel(CustomPropertyDbModel customProperty)
        // {
        //     var p = base.ToDomainModel(customProperty);
        //     if (p is TProperty sp)
        //     {
        //         sp.Options = customProperty.Options?.DeserializeAsCustomPropertyOptions<TPropertyOptions>() ??
        //                      InitializeOptionsInternal();
        //     }
        //
        //     return p;
        // }

        protected sealed override void EnsureOptionsType(object? options)
        {
            if (options != null && options is not TPropertyOptions)
            {
                throw new DevException(
                    $"{nameof(options)}:{options.GetType().FullName} is not compatible to {OptionsType.FullName}");
            }
        }

        public sealed override object InitializeOptions() => InitializeOptionsInternal();
        protected virtual TPropertyOptions InitializeOptionsInternal() => new();

        public sealed override Type OptionsType { get; } = SpecificTypeUtils<TPropertyOptions>.Type;
    }
}