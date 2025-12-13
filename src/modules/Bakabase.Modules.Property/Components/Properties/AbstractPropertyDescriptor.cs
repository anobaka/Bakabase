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
        IPropertyIndexProvider,
        IPropertyIndexSearcher
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

        #region IPropertyIndexSearcher

        /// <summary>
        /// 在索引上执行搜索操作
        /// </summary>
        public virtual HashSet<int>? SearchIndex(
            SearchOperation operation,
            object? filterDbValue,
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
            IReadOnlyCollection<int> allResourceIds)
        {
            // Handle null checks
            return operation switch
            {
                SearchOperation.IsNull => SearchIsNull(valueIndex, rangeIndex, allResourceIds),
                SearchOperation.IsNotNull => SearchIsNotNull(valueIndex, rangeIndex),
                _ => filterDbValue is TDbValue typedValue
                    ? SearchIndexInternal(operation, typedValue, valueIndex, rangeIndex, allResourceIds)
                    : new HashSet<int>()
            };
        }

        /// <summary>
        /// 子类覆盖此方法以提供特定的索引搜索逻辑
        /// </summary>
        protected virtual HashSet<int>? SearchIndexInternal(
            SearchOperation operation,
            TDbValue filterDbValue,
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
            IReadOnlyCollection<int> allResourceIds)
        {
            // 尝试范围搜索（用于数值和日期类型）
            if (filterDbValue is IComparable comparable && IsNumericOrDateTime(filterDbValue))
            {
                var rangeResult = SearchRangeIndex(operation, comparable, rangeIndex, allResourceIds);
                if (rangeResult != null)
                {
                    return rangeResult;
                }
            }

            // 默认：单值类型的基础实现
            var normalizedKey = NormalizeForIndex(filterDbValue);
            if (string.IsNullOrEmpty(normalizedKey))
            {
                return new HashSet<int>();
            }

            return operation switch
            {
                SearchOperation.Equals => GetExactMatch(normalizedKey, valueIndex),
                SearchOperation.NotEquals => Negate(GetExactMatch(normalizedKey, valueIndex), allResourceIds),
                SearchOperation.Contains => SearchContains(normalizedKey, valueIndex),
                SearchOperation.NotContains => Negate(SearchContains(normalizedKey, valueIndex), allResourceIds),
                SearchOperation.StartsWith => SearchStartsWith(normalizedKey, valueIndex),
                SearchOperation.NotStartsWith => Negate(SearchStartsWith(normalizedKey, valueIndex), allResourceIds),
                SearchOperation.EndsWith => SearchEndsWith(normalizedKey, valueIndex),
                SearchOperation.NotEndsWith => Negate(SearchEndsWith(normalizedKey, valueIndex), allResourceIds),
                SearchOperation.Matches => SearchMatches(normalizedKey, valueIndex),
                SearchOperation.NotMatches => Negate(SearchMatches(normalizedKey, valueIndex), allResourceIds),
                _ => null
            };
        }

        /// <summary>
        /// 将值规范化为索引键
        /// </summary>
        protected virtual string? NormalizeForIndex(TDbValue? value) =>
            value?.ToString()?.Trim().ToLowerInvariant();

        #region Index Search Helpers

        /// <summary>
        /// 精确匹配
        /// </summary>
        protected static HashSet<int> GetExactMatch(string key, IReadOnlyDictionary<string, HashSet<int>>? valueIndex)
        {
            if (valueIndex == null) return new HashSet<int>();
            return valueIndex.TryGetValue(key, out var ids) ? new HashSet<int>(ids) : new HashSet<int>();
        }

        /// <summary>
        /// 包含搜索（遍历所有索引键）
        /// </summary>
        protected static HashSet<int> SearchContains(string searchTerm, IReadOnlyDictionary<string, HashSet<int>>? valueIndex)
        {
            if (valueIndex == null) return new HashSet<int>();

            var result = new HashSet<int>();
            foreach (var (key, resourceIds) in valueIndex)
            {
                if (key.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    result.UnionWith(resourceIds);
                }
            }
            return result;
        }

        /// <summary>
        /// 前缀搜索
        /// </summary>
        protected static HashSet<int> SearchStartsWith(string prefix, IReadOnlyDictionary<string, HashSet<int>>? valueIndex)
        {
            if (valueIndex == null) return new HashSet<int>();

            var result = new HashSet<int>();
            foreach (var (key, resourceIds) in valueIndex)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.UnionWith(resourceIds);
                }
            }
            return result;
        }

        /// <summary>
        /// 后缀搜索
        /// </summary>
        protected static HashSet<int> SearchEndsWith(string suffix, IReadOnlyDictionary<string, HashSet<int>>? valueIndex)
        {
            if (valueIndex == null) return new HashSet<int>();

            var result = new HashSet<int>();
            foreach (var (key, resourceIds) in valueIndex)
            {
                if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result.UnionWith(resourceIds);
                }
            }
            return result;
        }

        /// <summary>
        /// 正则匹配搜索
        /// </summary>
        protected static HashSet<int> SearchMatches(string pattern, IReadOnlyDictionary<string, HashSet<int>>? valueIndex)
        {
            if (valueIndex == null) return new HashSet<int>();

            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1));

                var result = new HashSet<int>();
                foreach (var (key, resourceIds) in valueIndex)
                {
                    if (regex.IsMatch(key))
                    {
                        result.UnionWith(resourceIds);
                    }
                }
                return result;
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        /// <summary>
        /// 范围索引搜索
        /// </summary>
        protected static HashSet<int>? SearchRangeIndex(
            SearchOperation operation,
            IComparable filterValue,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
            IReadOnlyCollection<int> allResourceIds)
        {
            if (rangeIndex == null) return null;

            // 仅处理范围操作
            if (operation is not (SearchOperation.Equals or SearchOperation.NotEquals or
                SearchOperation.GreaterThan or SearchOperation.LessThan or
                SearchOperation.GreaterThanOrEquals or SearchOperation.LessThanOrEquals))
            {
                return null;
            }

            var result = new HashSet<int>();
            foreach (var (value, resourceIds) in rangeIndex)
            {
                try
                {
                    var matches = operation switch
                    {
                        SearchOperation.Equals => value.CompareTo(filterValue) == 0,
                        SearchOperation.NotEquals => value.CompareTo(filterValue) != 0,
                        SearchOperation.GreaterThan => value.CompareTo(filterValue) > 0,
                        SearchOperation.LessThan => value.CompareTo(filterValue) < 0,
                        SearchOperation.GreaterThanOrEquals => value.CompareTo(filterValue) >= 0,
                        SearchOperation.LessThanOrEquals => value.CompareTo(filterValue) <= 0,
                        _ => false
                    };
                    if (matches)
                    {
                        result.UnionWith(resourceIds);
                    }
                }
                catch
                {
                    // Type mismatch, skip
                }
            }

            return result;
        }

        /// <summary>
        /// IsNull: 所有资源减去有值的资源
        /// </summary>
        protected static HashSet<int> SearchIsNull(
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
            IReadOnlyCollection<int> allResourceIds)
        {
            var hasValueIds = GetAllResourceIdsWithValue(valueIndex, rangeIndex);
            return Negate(hasValueIds, allResourceIds);
        }

        /// <summary>
        /// IsNotNull: 有值的资源
        /// </summary>
        protected static HashSet<int> SearchIsNotNull(
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex)
        {
            return GetAllResourceIdsWithValue(valueIndex, rangeIndex);
        }

        /// <summary>
        /// 获取所有有值的资源ID
        /// </summary>
        protected static HashSet<int> GetAllResourceIdsWithValue(
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex)
        {
            var result = new HashSet<int>();

            if (valueIndex != null)
            {
                foreach (var ids in valueIndex.Values)
                {
                    result.UnionWith(ids);
                }
            }

            if (rangeIndex != null)
            {
                foreach (var (_, ids) in rangeIndex)
                {
                    result.UnionWith(ids);
                }
            }

            return result;
        }

        /// <summary>
        /// 取反：所有资源减去指定集合
        /// </summary>
        protected static HashSet<int> Negate(HashSet<int>? toExclude, IReadOnlyCollection<int> all)
        {
            var result = new HashSet<int>(all);
            if (toExclude != null)
            {
                result.ExceptWith(toExclude);
            }
            return result;
        }

        /// <summary>
        /// 交集：资源必须在所有集合中
        /// </summary>
        protected static HashSet<int> IntersectAll(IEnumerable<HashSet<int>?> sets)
        {
            HashSet<int>? result = null;
            foreach (var set in sets.Where(s => s != null))
            {
                if (result == null)
                {
                    result = new HashSet<int>(set!);
                }
                else
                {
                    result.IntersectWith(set!);
                }

                if (result.Count == 0)
                {
                    break;
                }
            }
            return result ?? new HashSet<int>();
        }

        /// <summary>
        /// 并集：资源在任一集合中
        /// </summary>
        protected static HashSet<int> UnionAll(IEnumerable<HashSet<int>?> sets)
        {
            var result = new HashSet<int>();
            foreach (var set in sets.Where(s => s != null))
            {
                result.UnionWith(set!);
            }
            return result;
        }

        /// <summary>
        /// 规范化字符串（小写、去空格）
        /// </summary>
        protected static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? "";

        #endregion

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