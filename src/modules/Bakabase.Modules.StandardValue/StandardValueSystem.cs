using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue;

/// <summary>
/// Unified entry point for the StandardValue system: conversion, validation, handlers.
/// Serialization has exactly one API — the SerializeAsStandardValue /
/// DeserializeAsStandardValue extension methods in
/// Bakabase.Modules.StandardValue.Extensions (tolerant by default; pass
/// throwOnError: true to surface malformed data).
/// </summary>
public static class StandardValueSystem
{
    #region Conversion

    /// <summary>
    /// Convert value between StandardValueTypes (sync, no custom datetime parsing).
    /// For async conversion with datetime parsing, use IStandardValueService.
    /// </summary>
    public static object? Convert(object? value, StandardValueType fromType, StandardValueType toType) =>
        StandardValueInternals.HandlerMap[fromType].Convert(value, toType);

    /// <summary>
    /// Get the conversion rules (flags indicating what info might be lost) for a type pair.
    /// </summary>
    public static StandardValueConversionRule GetConversionRules(
        StandardValueType fromType, StandardValueType toType) =>
        StandardValueInternals.ConversionRules[fromType][toType];

    /// <summary>
    /// Get all conversion rules between all StandardValueTypes.
    /// Key: fromType -> toType -> rules flags
    /// </summary>
    public static IReadOnlyDictionary<StandardValueType,
        IReadOnlyDictionary<StandardValueType, StandardValueConversionRule>> GetAllConversionRules() =>
        StandardValueInternals.ConversionRules
            .ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<StandardValueType, StandardValueConversionRule>)
                    new Dictionary<StandardValueType, StandardValueConversionRule>(kv.Value));

    #endregion

    #region Validation

    /// <summary>
    /// Validate that a value matches the expected StandardValueType.
    /// </summary>
    public static bool Validate(object? value, StandardValueType expectedType) =>
        value.IsStandardValueType(expectedType);

    /// <summary>
    /// Infer StandardValueType from a CLR type.
    /// </summary>
    public static StandardValueType? InferType<T>() =>
        typeof(T).InferStandardValueType();

    /// <summary>
    /// Infer StandardValueType from a value.
    /// </summary>
    public static StandardValueType? InferType(object? value) =>
        value?.InferStandardValueType();

    #endregion

    #region Handlers

    /// <summary>
    /// Get the value handler for a StandardValueType.
    /// </summary>
    public static IStandardValueHandler GetHandler(StandardValueType type) =>
        StandardValueInternals.HandlerMap[type];

    /// <summary>
    /// Try get the value handler for a StandardValueType.
    /// </summary>
    public static IStandardValueHandler? TryGetHandler(StandardValueType type) =>
        StandardValueInternals.HandlerMap.GetValueOrDefault(type);

    #endregion

    #region Test Data

    /// <summary>
    /// Get expected conversion test data for all type pairs.
    /// Primarily for testing/debugging purposes.
    /// </summary>
    public static IReadOnlyDictionary<StandardValueType,
        IReadOnlyDictionary<StandardValueType, IReadOnlyList<(object? FromValue, object? ExpectedValue)>>>
        GetExpectedConversions() =>
        StandardValueInternals.ExpectedConversions
            .ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<StandardValueType, IReadOnlyList<(object? FromValue, object? ExpectedValue)>>)
                    kv.Value.ToDictionary(
                        inner => inner.Key,
                        inner => (IReadOnlyList<(object? FromValue, object? ExpectedValue)>)inner.Value));

    #endregion

    #region Separators

    /// <summary>
    /// Common separator for list items (used for ListString, Tags display, etc.)
    /// </summary>
    public const char CommonListItemSeparator = ',';

    /// <summary>
    /// Inner separator for ListListString (used between items in inner lists)
    /// </summary>
    public const char ListListStringInnerSeparator = '/';

    #endregion

    #region Combine

    /// <summary>
    /// Combines multiple values into a single value.
    /// This is NOT string concatenation - it determines how to resolve multiple property values into one.
    /// For collection types (ListString, ListTag, etc.): aggregates all values (union).
    /// For single-value types (String, Decimal, etc.): returns the first non-null value.
    /// </summary>
    /// <param name="values">Multiple values to combine.</param>
    /// <param name="type">The StandardValueType of the values.</param>
    /// <returns>The combined value, or null if all inputs are null.</returns>
    public static object? Combine(IEnumerable<object?> values, StandardValueType type) =>
        GetHandler(type).Combine(values);

    #endregion
}
