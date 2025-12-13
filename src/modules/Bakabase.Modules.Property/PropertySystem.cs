using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Accessors;
using Bakabase.Modules.Property.Components.BuiltinProperty;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using SvSystem = Bakabase.Modules.StandardValue.StandardValueSystem;
using SvTypes = Bakabase.Modules.StandardValue.Models.Domain;
using SvComponents = Bakabase.Modules.StandardValue.Components;

namespace Bakabase.Modules.Property;

/// <summary>
/// Unified entry point for the Property system.
/// Provides type-safe access to StandardValue and Property operations.
/// </summary>
public static class PropertySystem
{
    /// <summary>
    /// StandardValue operations - creating, serializing, converting values.
    /// Delegates to SvSystem for implementation.
    /// </summary>
    public static class Value
    {
        /// <summary>
        /// Type-safe StandardValue factory.
        /// Usage: PropertySystem.Value.Create.String("hello")
        /// </summary>
        public static StandardValueFactoryAccessor Create => default;

        /// <summary>
        /// Serialize a value to string representation.
        /// </summary>
        public static string? Serialize(object? value, StandardValueType type) =>
            SvSystem.Serialize(value, type);

        /// <summary>
        /// Serialize a typed StandardValue.
        /// </summary>
        public static string? Serialize<T>(SvComponents.StandardValue<T>? value) =>
            SvSystem.Serialize(value);

        /// <summary>
        /// Deserialize a string to value.
        /// </summary>
        public static object? Deserialize(string? serialized, StandardValueType type) =>
            SvSystem.Deserialize(serialized, type);

        /// <summary>
        /// Deserialize a string to typed value.
        /// </summary>
        public static T? Deserialize<T>(string? serialized, StandardValueType type) =>
            SvSystem.Deserialize<T>(serialized, type);

        /// <summary>
        /// Validate that a value matches the expected StandardValueType.
        /// </summary>
        public static bool Validate(object? value, StandardValueType expectedType) =>
            SvSystem.Validate(value, expectedType);

        /// <summary>
        /// Infer StandardValueType from a CLR type.
        /// </summary>
        public static StandardValueType? InferType<T>() =>
            SvSystem.InferType<T>();

        /// <summary>
        /// Infer StandardValueType from a value.
        /// </summary>
        public static StandardValueType? InferType(object? value) =>
            SvSystem.InferType(value);

        /// <summary>
        /// Get the value handler for a StandardValueType.
        /// </summary>
        public static IStandardValueHandler GetHandler(StandardValueType type) =>
            SvSystem.GetHandler(type);

        /// <summary>
        /// Convert value between StandardValueTypes (sync, no custom datetime parsing).
        /// For async conversion with datetime parsing, use IStandardValueService.
        /// </summary>
        public static object? Convert(object? value, StandardValueType fromType, StandardValueType toType) =>
            SvSystem.Convert(value, fromType, toType);

        /// <summary>
        /// Get the conversion rules (flags indicating what info might be lost) for a type pair.
        /// </summary>
        public static StandardValueConversionRule GetConversionRules(
            StandardValueType fromType, StandardValueType toType) =>
            SvSystem.GetConversionRules(fromType, toType);

        /// <summary>
        /// Get all conversion rules between all StandardValueTypes.
        /// Key: fromType -> toType -> rules flags
        /// </summary>
        public static IReadOnlyDictionary<StandardValueType,
            IReadOnlyDictionary<StandardValueType, StandardValueConversionRule>> GetAllConversionRules() =>
            SvSystem.GetAllConversionRules();

        /// <summary>
        /// Get expected conversion test data for all type pairs.
        /// Primarily for testing/debugging purposes.
        /// </summary>
        public static IReadOnlyDictionary<StandardValueType,
            IReadOnlyDictionary<StandardValueType, IReadOnlyList<(object? FromValue, object? ExpectedValue)>>>
            GetExpectedConversions() =>
            SvSystem.GetExpectedConversions();
    }

    /// <summary>
    /// Property operations - descriptors, type info, value conversion.
    /// For property value construction, use PropertyValueFactory.{Type}.Db/Biz directly.
    /// </summary>
    public static class Property
    {
        /// <summary>
        /// Get the property descriptor for a PropertyType.
        /// </summary>
        public static IPropertyDescriptor GetDescriptor(PropertyType type) =>
            PropertyInternals.DescriptorMap[type];

        /// <summary>
        /// Try get the property descriptor for a PropertyType.
        /// </summary>
        public static IPropertyDescriptor? TryGetDescriptor(PropertyType type) =>
            PropertyInternals.DescriptorMap.GetValueOrDefault(type);

        /// <summary>
        /// Get property attribute (db/biz value types, is reference type).
        /// </summary>
        public static PropertyAttribute GetAttribute(PropertyType type) =>
            PropertyInternals.PropertyAttributeMap[type];

        /// <summary>
        /// Try get property attribute (db/biz value types, is reference type).
        /// </summary>
        public static PropertyAttribute? TryGetAttribute(PropertyType type) =>
            PropertyInternals.PropertyAttributeMap.GetValueOrDefault(type);

        /// <summary>
        /// Get the DB value type for a PropertyType.
        /// </summary>
        public static StandardValueType GetDbValueType(PropertyType type) =>
            PropertyInternals.PropertyAttributeMap[type].DbValueType;

        /// <summary>
        /// Get the Biz value type for a PropertyType.
        /// </summary>
        public static StandardValueType GetBizValueType(PropertyType type) =>
            PropertyInternals.PropertyAttributeMap[type].BizValueType;

        /// <summary>
        /// Check if a PropertyType is a reference value type (stores UUIDs).
        /// </summary>
        public static bool IsReferenceValueType(PropertyType type) =>
            PropertyInternals.PropertyAttributeMap[type].IsReferenceValueType;

        /// <summary>
        /// Convert DbValue to BizValue for a property.
        /// </summary>
        public static object? ToBizValue(Bakabase.Abstractions.Models.Domain.Property property, object? dbValue) =>
            GetDescriptor(property.Type).GetBizValue(property, dbValue);

        /// <summary>
        /// Convert DbValue to typed BizValue for a property.
        /// </summary>
        public static TBizValue? ToBizValue<TBizValue>(
            Bakabase.Abstractions.Models.Domain.Property property, object? dbValue)
        {
            var bizValue = ToBizValue(property, dbValue);
            return bizValue is TBizValue typed ? typed : default;
        }

        /// <summary>
        /// Convert BizValue to DbValue for a property.
        /// </summary>
        public static (object? DbValue, bool PropertyChanged) ToDbValue(
            Bakabase.Abstractions.Models.Domain.Property property, object? bizValue) =>
            GetDescriptor(property.Type).PrepareDbValue(property, bizValue);

        /// <summary>
        /// Get the search handler for a PropertyType.
        /// </summary>
        public static IPropertySearchHandler GetSearchHandler(PropertyType type) =>
            PropertyInternals.PropertySearchHandlerMap[type];

        /// <summary>
        /// Get the index provider for a PropertyType.
        /// Returns null if the descriptor doesn't implement IPropertyIndexProvider.
        /// </summary>
        public static IPropertyIndexProvider? TryGetIndexProvider(PropertyType type) =>
            TryGetDescriptor(type) as IPropertyIndexProvider;

        /// <summary>
        /// Try get the search handler for a PropertyType.
        /// </summary>
        public static IPropertySearchHandler? TryGetSearchHandler(PropertyType type) =>
            PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(type);

        /// <summary>
        /// Get a virtual property instance for a PropertyType.
        /// Useful when you need a Property object but don't have a stored property.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property GetVirtual(PropertyType type) =>
            PropertyInternals.VirtualPropertyMap[type];

        /// <summary>
        /// Try get a virtual property instance for a PropertyType.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property? TryGetVirtual(PropertyType type) =>
            PropertyInternals.VirtualPropertyMap.GetValueOrDefault(type);

        /// <summary>
        /// Get expected conversion test data for all property type pairs.
        /// Primarily for testing/debugging purposes.
        /// </summary>
        public static IReadOnlyDictionary<PropertyType,
            IReadOnlyDictionary<PropertyType, IReadOnlyList<(object? FromBizValue, object? ExpectedBizValue)>>>
            GetExpectedConversions() =>
            PropertyInternals.ExpectedConversions
                .ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyDictionary<PropertyType, IReadOnlyList<(object? FromBizValue, object? ExpectedBizValue)>>)
                        kv.Value.ToDictionary(
                            inner => inner.Key,
                            inner => (IReadOnlyList<(object? FromBizValue, object? ExpectedBizValue)>) inner.Value));

        /// <summary>
        /// Combines multiple serialized DB values into a single serialized DB value.
        /// This is NOT string concatenation - it determines how to resolve multiple property values into one.
        /// For collection-type properties (Tags, MultipleChoice, etc.): aggregates all values (union).
        /// For single-value properties (SingleLineText, Number, etc.): returns the first non-null value.
        /// </summary>
        /// <param name="propertyType">The property type.</param>
        /// <param name="serializedDbValues">Multiple serialized DB values to combine.</param>
        /// <returns>The combined serialized DB value, or null if all inputs are null/empty.</returns>
        public static string? CombineSerializedDbValues(PropertyType propertyType, IEnumerable<string?> serializedDbValues)
        {
            var dbValueType = GetDbValueType(propertyType);

            // Deserialize all non-null values
            var values = serializedDbValues
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => SvSystem.Deserialize(s, dbValueType))
                .ToList();

            if (values.Count == 0) return null;

            // Combine using the handler's strategy
            var combined = SvSystem.Combine(values, dbValueType);

            // Serialize back
            return SvSystem.Serialize(combined, dbValueType);
        }
    }

    /// <summary>
    /// Built-in property access - Internal and Reserved properties.
    /// For generic properties, only Definition is exposed.
    /// For specialized properties (MediaLibraryV2Multi, ParentResource), full accessor is available.
    /// Use PropertyValueFactory for value construction.
    /// </summary>
    public static class Builtin
    {
        #region Internal Properties - Definitions

        /// <summary>
        /// Resource filename (SingleLineText) - property definition
        /// Use PropertyValueFactory.SingleLineText for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property Filename =>
            BuiltinProperties.Internal.Filename.Definition;

        /// <summary>
        /// Parent directory path (SingleLineText) - property definition
        /// Use PropertyValueFactory.SingleLineText for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property DirectoryPath =>
            BuiltinProperties.Internal.DirectoryPath.Definition;

        /// <summary>
        /// Record creation time (DateTime) - property definition
        /// Use PropertyValueFactory.DateTime for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property CreatedAt =>
            BuiltinProperties.Internal.CreatedAt.Definition;

        /// <summary>
        /// File system creation time (DateTime) - property definition
        /// Use PropertyValueFactory.DateTime for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property FileCreatedAt =>
            BuiltinProperties.Internal.FileCreatedAt.Definition;

        /// <summary>
        /// File system modification time (DateTime) - property definition
        /// Use PropertyValueFactory.DateTime for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property FileModifiedAt =>
            BuiltinProperties.Internal.FileModifiedAt.Definition;

        /// <summary>
        /// Last playback time (DateTime) - property definition
        /// Use PropertyValueFactory.DateTime for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property PlayedAt =>
            BuiltinProperties.Internal.PlayedAt.Definition;

        #endregion

        #region Internal Properties - Specialized Accessors

        /// <summary>
        /// Media library binding with multiple choice support.
        /// Provides int-based API since media library IDs are integers.
        /// </summary>
        public static MediaLibraryPropertyAccessor MediaLibraryV2Multi =>
            BuiltinProperties.Internal.MediaLibraryV2Multi;

        /// <summary>
        /// Parent resource link (SingleChoice).
        /// Provides int-based API since resource IDs are integers.
        /// </summary>
        public static ParentResourcePropertyAccessor ParentResource =>
            BuiltinProperties.Internal.ParentResource;

        #endregion

        #region Reserved Properties - Definitions

        /// <summary>
        /// User rating (Rating/Decimal) - property definition
        /// Use PropertyValueFactory.Number for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property Rating =>
            BuiltinProperties.Reserved.Rating.Definition;

        /// <summary>
        /// Resource description (MultilineText) - property definition
        /// Use PropertyValueFactory.MultilineText for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property Introduction =>
            BuiltinProperties.Reserved.Introduction.Definition;

        /// <summary>
        /// Cover image paths (Attachment/ListString) - property definition
        /// Use PropertyValueFactory.Attachment for value construction.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property Cover =>
            BuiltinProperties.Reserved.Cover.Definition;

        #endregion

        /// <summary>
        /// Get property definition by ResourceProperty enum.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property Get(ResourceProperty prop) =>
            BuiltinProperties.Get(prop);

        /// <summary>
        /// Try get property definition by ResourceProperty enum.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property? TryGet(ResourceProperty prop) =>
            BuiltinProperties.TryGet(prop);

        /// <summary>
        /// Get property definition by ReservedProperty enum.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property Get(Bakabase.Abstractions.Models.Domain.Constants.ReservedProperty prop) =>
            BuiltinProperties.Get((ResourceProperty)prop);

        /// <summary>
        /// Try get property definition by ReservedProperty enum.
        /// </summary>
        public static Bakabase.Abstractions.Models.Domain.Property? TryGetReserved(Bakabase.Abstractions.Models.Domain.Constants.ReservedProperty prop) =>
            BuiltinProperties.TryGet((ResourceProperty)prop);
    }

    /// <summary>
    /// Search/Filter operations - get filter value types, build filters, check matches
    /// </summary>
    public static class Search
    {
        /// <summary>
        /// MediaLibraryV2Multi search filter value builder.
        /// Provides type-safe methods to build filter values for media library searches.
        /// </summary>
        public static MediaLibraryPropertyAccessor MediaLibraryV2Multi =>
            BuiltinProperties.Internal.MediaLibraryV2Multi;

        /// <summary>
        /// Get the property to use for filter value based on operation.
        /// For example, SingleChoice + In → returns MultipleChoice property.
        /// This is useful when the filter value type differs from the property type.
        /// </summary>
        /// <param name="property">The property being filtered on</param>
        /// <param name="operation">The search operation</param>
        /// <returns>The property to use for filter value (may be different from input)</returns>
        public static Bakabase.Abstractions.Models.Domain.Property GetFilterValueProperty(
            Bakabase.Abstractions.Models.Domain.Property property,
            SearchOperation operation)
        {
            var psh = Property.TryGetSearchHandler(property.Type);
            if (psh?.SearchOperations.TryGetValue(operation, out var options) == true)
            {
                if (options?.ConvertProperty != null)
                {
                    return options.ConvertProperty(property);
                }
            }
            return property;
        }

        /// <summary>
        /// Get supported search operations for a property type.
        /// </summary>
        public static IReadOnlyCollection<SearchOperation> GetSupportedOperations(PropertyType type)
        {
            var handler = Property.TryGetSearchHandler(type);
            return handler?.SearchOperations.Keys.ToList() ?? [];
        }

        /// <summary>
        /// Get the value type info for a filter based on property type and operation.
        /// </summary>
        /// <param name="propertyType">The property type being filtered</param>
        /// <param name="operation">The search operation</param>
        /// <returns>Tuple of (DbValueType, BizValueType) for the filter value</returns>
        public static (StandardValueType DbValueType, StandardValueType BizValueType) GetFilterValueTypes(
            PropertyType propertyType,
            SearchOperation operation)
        {
            var psh = Property.TryGetSearchHandler(propertyType);
            var asType = psh?.SearchOperations.GetValueOrDefault(operation)?.AsType ?? propertyType;
            var attr = Property.GetAttribute(asType);
            return (attr.DbValueType, attr.BizValueType);
        }

        /// <summary>
        /// Check if a property value matches the filter criteria.
        /// </summary>
        /// <param name="type">The property type</param>
        /// <param name="dbValue">The property's db value</param>
        /// <param name="operation">The search operation</param>
        /// <param name="filterValue">The filter value to match against</param>
        /// <returns>True if the value matches the filter criteria</returns>
        public static bool IsMatch(PropertyType type, object? dbValue, SearchOperation operation, object? filterValue)
        {
            var handler = Property.TryGetSearchHandler(type);
            return handler?.IsMatch(dbValue, operation, filterValue) ?? true;
        }

        /// <summary>
        /// Build a search filter from keyword for a property.
        /// </summary>
        /// <param name="property">The property to build filter for</param>
        /// <param name="keyword">The search keyword</param>
        /// <returns>A search filter, or null if the property doesn't support keyword search</returns>
        public static ResourceSearchFilter? BuildFilterByKeyword(
            Bakabase.Abstractions.Models.Domain.Property property,
            string keyword)
        {
            var handler = Property.TryGetSearchHandler(property.Type);
            return handler?.BuildSearchFilterByKeyword(property, keyword);
        }

        /// <summary>
        /// Serialize a filter value to string based on property type and operation.
        /// </summary>
        /// <param name="value">The filter value to serialize</param>
        /// <param name="propertyType">The property type being filtered</param>
        /// <param name="operation">The search operation</param>
        /// <returns>Serialized filter value</returns>
        public static string? SerializeFilterValue(object? value, PropertyType propertyType, SearchOperation operation)
        {
            var (dbValueType, _) = GetFilterValueTypes(propertyType, operation);
            return Value.Serialize(value, dbValueType);
        }

        /// <summary>
        /// Deserialize a filter value from string based on property type and operation.
        /// </summary>
        /// <param name="serialized">The serialized filter value</param>
        /// <param name="propertyType">The property type being filtered</param>
        /// <param name="operation">The search operation</param>
        /// <returns>Deserialized filter value</returns>
        public static object? DeserializeFilterValue(string? serialized, PropertyType propertyType, SearchOperation operation)
        {
            var (dbValueType, _) = GetFilterValueTypes(propertyType, operation);
            return Value.Deserialize(serialized, dbValueType);
        }

        /// <summary>
        /// Convert a serialized filter value from one property to another.
        /// If both properties use the same filter value type for the given operation, the original serialized value is returned unchanged.
        /// This is useful for migrating search filters when property types change (e.g., SingleChoice to MultipleChoice).
        /// </summary>
        /// <param name="serializedValue">The serialized filter value to convert</param>
        /// <param name="fromProperty">The source property definition</param>
        /// <param name="toProperty">The target property definition</param>
        /// <param name="operation">The search operation</param>
        /// <returns>Converted serialized filter value, or null if input is null/empty</returns>
        public static string? ConvertFilterValue(
            string? serializedValue,
            Bakabase.Abstractions.Models.Domain.Property fromProperty,
            Bakabase.Abstractions.Models.Domain.Property toProperty,
            SearchOperation operation)
        {
            return ConvertFilterValue(serializedValue, fromProperty.Type, toProperty.Type, operation);
        }

        /// <summary>
        /// Convert a serialized filter value from one property type to another.
        /// If both types use the same filter value type for the given operation, the original serialized value is returned unchanged.
        /// This is useful for migrating search filters when property types change (e.g., SingleChoice to MultipleChoice).
        /// </summary>
        /// <param name="serializedValue">The serialized filter value to convert</param>
        /// <param name="fromType">The source property type</param>
        /// <param name="toType">The target property type</param>
        /// <param name="operation">The search operation</param>
        /// <returns>Converted serialized filter value, or null if input is null/empty</returns>
        public static string? ConvertFilterValue(
            string? serializedValue,
            PropertyType fromType,
            PropertyType toType,
            SearchOperation operation)
        {
            if (string.IsNullOrEmpty(serializedValue)) return null;

            // Get filter value types for both properties
            var (fromDbType, _) = GetFilterValueTypes(fromType, operation);
            var (toDbType, _) = GetFilterValueTypes(toType, operation);

            // If same type, no conversion needed - return original value
            if (fromDbType == toDbType)
            {
                return serializedValue;
            }

            // Deserialize from source type, convert, and re-serialize to target type
            var value = Value.Deserialize(serializedValue, fromDbType);
            if (value == null) return null;

            var converted = SvSystem.Convert(value, fromDbType, toDbType);
            return Value.Serialize(converted, toDbType);
        }

        /// <summary>
        /// Evaluate a search filter against pre-built indexes.
        /// This method delegates to the property descriptor's SearchIndex implementation.
        /// </summary>
        /// <param name="filter">The search filter containing property type, operation, and filter value</param>
        /// <param name="valueIndex">The value-based index for the property (normalized key -> resource IDs)</param>
        /// <param name="rangeIndex">The range-based index for the property (comparable value -> resource IDs)</param>
        /// <param name="allResourceIds">All indexed resource IDs (for negation operations)</param>
        /// <returns>Set of matching resource IDs, or null if not supported or matches all</returns>
        public static HashSet<int>? EvaluateOnIndex(
            ResourceSearchFilter filter,
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
            IReadOnlyCollection<int> allResourceIds)
        {
            var searcher = Property.TryGetDescriptor(filter.Property.Type) as IPropertyIndexSearcher;
            return searcher?.SearchIndex(
                filter.Operation,
                filter.DbValue,
                valueIndex,
                rangeIndex,
                allResourceIds);
        }

        /// <summary>
        /// Evaluate a search filter against pre-built indexes using property type.
        /// </summary>
        /// <param name="propertyType">The property type</param>
        /// <param name="operation">The search operation</param>
        /// <param name="filterDbValue">The filter's database value</param>
        /// <param name="valueIndex">The value-based index for the property</param>
        /// <param name="rangeIndex">The range-based index for the property</param>
        /// <param name="allResourceIds">All indexed resource IDs</param>
        /// <returns>Set of matching resource IDs, or null if not supported or matches all</returns>
        public static HashSet<int>? EvaluateOnIndex(
            PropertyType propertyType,
            SearchOperation operation,
            object? filterDbValue,
            IReadOnlyDictionary<string, HashSet<int>>? valueIndex,
            IReadOnlyList<KeyValuePair<IComparable, HashSet<int>>>? rangeIndex,
            IReadOnlyCollection<int> allResourceIds)
        {
            var searcher = Property.TryGetDescriptor(propertyType) as IPropertyIndexSearcher;
            return searcher?.SearchIndex(
                operation,
                filterDbValue,
                valueIndex,
                rangeIndex,
                allResourceIds);
        }
    }

    /// <summary>
    /// Accessor struct for StandardValueFactory (allows static class syntax).
    /// Delegates to SvSystem.Create for implementation.
    /// </summary>
    public readonly struct StandardValueFactoryAccessor
    {
        public SvComponents.StandardValue<string> String(string? value) => SvSystem.Create.String(value);
        public SvComponents.StandardValue<List<string>> ListString(List<string>? value) => SvSystem.Create.ListString(value);
        public SvComponents.StandardValue<List<string>> ListString(params string[] values) => SvSystem.Create.ListString(values);
        public SvComponents.StandardValue<decimal> Decimal(decimal value) => SvSystem.Create.Decimal(value);
        public SvComponents.StandardValue<decimal>? Decimal(decimal? value) => SvSystem.Create.Decimal(value);
        public SvComponents.StandardValue<SvTypes.LinkValue> Link(string? text, string? url) => SvSystem.Create.Link(text, url);
        public SvComponents.StandardValue<SvTypes.LinkValue> Link(SvTypes.LinkValue? value) => SvSystem.Create.Link(value);
        public SvComponents.StandardValue<bool> Boolean(bool value) => SvSystem.Create.Boolean(value);
        public SvComponents.StandardValue<bool>? Boolean(bool? value) => SvSystem.Create.Boolean(value);
        public SvComponents.StandardValue<DateTime> DateTime(DateTime value) => SvSystem.Create.DateTime(value);
        public SvComponents.StandardValue<DateTime>? DateTime(DateTime? value) => SvSystem.Create.DateTime(value);
        public SvComponents.StandardValue<TimeSpan> Time(TimeSpan value) => SvSystem.Create.Time(value);
        public SvComponents.StandardValue<TimeSpan>? Time(TimeSpan? value) => SvSystem.Create.Time(value);
        public SvComponents.StandardValue<List<List<string>>> ListListString(List<List<string>>? value) => SvSystem.Create.ListListString(value);
        public SvComponents.StandardValue<List<List<string>>> ListListString(params List<string>[] values) => SvSystem.Create.ListListString(values);
        public SvComponents.StandardValue<List<SvTypes.TagValue>> ListTag(List<SvTypes.TagValue>? value) => SvSystem.Create.ListTag(value);
        public SvComponents.StandardValue<List<SvTypes.TagValue>> ListTag(params SvTypes.TagValue[] values) => SvSystem.Create.ListTag(values);
        public SvComponents.StandardValue<List<SvTypes.TagValue>> ListTag(params (string? Group, string Name)[] values) => SvSystem.Create.ListTag(values);
    }

}
