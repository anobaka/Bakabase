using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.Property.Components.Properties
{
    public abstract class
        AbstractPropertyDescriptor<TDbValue, TBizValue> : IPropertyDescriptor,
        IPropertySearchHandler,
        IPropertyIndexProvider
    {
        public StandardValueType DbValueType => PropertySystem.Property.GetDbValueType(Type);
        public StandardValueType BizValueType => PropertySystem.Property.GetBizValueType(Type);

        public abstract PropertyType Type { get; }

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

        public bool IsMatch(object? dbValue, SearchOperation operation, object? filterValue)
        {
            // validate filter value
            var expectedFilterValueType = PropertySystem.Property.GetDbValueType(
                SearchOperations.GetValueOrDefault(operation)?.AsType ?? Type);
            if (!filterValue.IsStandardValueType(expectedFilterValueType))
            {
                return false;
            }

            var optValue = StandardValueSystem.GetHandler(expectedFilterValueType).Optimize(dbValue);

            if (optValue is TDbValue tv)
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

            var opAsType = SearchOperations.GetValueOrDefault(sf.Value.Operation)?.AsType;
            var dbValueType = opAsType != null ? PropertySystem.Property.GetDbValueType(opAsType.Value) : (StandardValueType?)null;
            if (!dbValueType.HasValue)
            {
                return null;
            }

            return new ResourceSearchFilter
            {
                Operation = sf.Value.Operation,
                DbValue = sf.Value.DbValue,
                PropertyPool = PropertyPool.Custom,
                PropertyId = property.Id,
                Property = property
            };
        }

        protected virtual (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
            Bakabase.Abstractions.Models.Domain.Property property, string keyword) => null;

        #region IPropertyIndexProvider

        /// <summary>
        /// 生成索引条目，默认实现将值转换为字符串作为索引键
        /// 子类可以覆盖此方法提供特定的索引生成逻辑
        /// </summary>
        public virtual IEnumerable<PropertyIndexEntry> GenerateIndexEntries(
            Bakabase.Abstractions.Models.Domain.Property property,
            object? dbValue)
        {
            if (dbValue == null) yield break;

            if (dbValue is TDbValue typedValue)
            {
                foreach (var entry in GenerateIndexEntriesInternal(property, typedValue))
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// 生成索引条目的内部实现，默认将值转换为单个字符串键
        /// </summary>
        protected virtual IEnumerable<PropertyIndexEntry> GenerateIndexEntriesInternal(
            Bakabase.Abstractions.Models.Domain.Property property,
            TDbValue dbValue)
        {
            var key = dbValue?.ToString();
            if (!string.IsNullOrEmpty(key))
            {
                // 对于数值和日期类型，同时提供范围值
                IComparable? rangeValue = dbValue is IComparable c && IsNumericOrDateTime(dbValue) ? c : null;
                yield return new PropertyIndexEntry(key, rangeValue);
            }
        }

        private static bool IsNumericOrDateTime(object value)
        {
            return value is int or long or float or double or decimal
                or System.DateTime or System.DateTimeOffset or System.TimeSpan;
        }

        #endregion
    }

    public abstract class
        AbstractPropertyDescriptor<TPropertyOptions, TDbValue, TBizValue> : AbstractPropertyDescriptor<TDbValue,
        TBizValue>
        where TPropertyOptions : class, new()
    {
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