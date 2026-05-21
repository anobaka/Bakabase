using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

/// <summary>
/// Describes how a single <see cref="ReservedProperty"/> maps to a typed column of the domain
/// <see cref="ReservedPropertyValue"/>.
/// <para>
/// This is a positional record on purpose: when a new per-property concern is needed, add a
/// constructor parameter here and every <see cref="ReservedProperties"/> entry stops compiling
/// until it is filled in.
/// </para>
/// </summary>
public sealed record ReservedPropertyDefinition(
    ReservedProperty Property,
    Func<ReservedPropertyValue, object?> Read,
    Action<ReservedPropertyValue, object?> Write);

/// <summary>
/// Single source of truth for reserved-property value access. Adding a new
/// <see cref="ReservedProperty"/> forces an update here at compile time (see <see cref="Get"/>).
/// </summary>
public static class ReservedProperties
{
    public static readonly ReservedPropertyDefinition Rating = new(
        ReservedProperty.Rating,
        v => v.Rating,
        (v, x) => v.Rating = x as decimal?);

    public static readonly ReservedPropertyDefinition Introduction = new(
        ReservedProperty.Introduction,
        v => v.Introduction,
        (v, x) => v.Introduction = x as string);

    public static readonly ReservedPropertyDefinition Cover = new(
        ReservedProperty.Cover,
        v => v.CoverPaths,
        (v, x) => v.CoverPaths = x as List<string>);

    public static readonly ReservedPropertyDefinition Name = new(
        ReservedProperty.Name,
        v => v.Name,
        (v, x) => v.Name = x as string);

    /// <summary>
    /// Maps every <see cref="ReservedProperty"/> to its definition.
    /// <para>
    /// This switch has NO discard (<c>_</c>) arm by design. A new <see cref="ReservedProperty"/>
    /// without a case here is reported as CS8509, which <c>.editorconfig</c> promotes to a build
    /// error for this file — so the omission cannot reach runtime.
    /// </para>
    /// </summary>
    public static ReservedPropertyDefinition Get(ReservedProperty property) => property switch
    {
        ReservedProperty.Rating => Rating,
        ReservedProperty.Introduction => Introduction,
        ReservedProperty.Cover => Cover,
        ReservedProperty.Name => Name
    };

    /// <summary>
    /// Non-throwing lookup. Returns <c>false</c> only for an undefined (unnamed) enum value —
    /// every declared <see cref="ReservedProperty"/> is guaranteed to resolve.
    /// </summary>
    public static bool TryGet(ReservedProperty property,
        [NotNullWhen(true)] out ReservedPropertyDefinition? definition)
    {
        if (Enum.IsDefined(property))
        {
            definition = Get(property);
            return true;
        }

        definition = null;
        return false;
    }
}

/// <summary>
/// Reads and writes reserved-property values by <see cref="ReservedProperty"/>, routed through
/// <see cref="ReservedProperties"/>. Callers pick the throwing or the <c>Try</c> variant
/// depending on whether an unknown property should fail loudly.
/// </summary>
public static class ReservedPropertyValueAccessor
{
    public static object? GetValue(this ReservedPropertyValue value, ReservedProperty property)
        => ReservedProperties.TryGet(property, out var definition)
            ? definition.Read(value)
            : throw new ArgumentOutOfRangeException(nameof(property), property,
                $"'{property}' is not a known reserved property.");

    /// <summary>Writes the value, throwing when <paramref name="property"/> is not a known reserved property.</summary>
    public static void SetValue(this ReservedPropertyValue value, ReservedProperty property, object? newValue)
    {
        if (!value.TrySetValue(property, newValue))
        {
            throw new ArgumentOutOfRangeException(nameof(property), property,
                $"'{property}' is not a known reserved property.");
        }
    }

    /// <summary>
    /// Writes the value and returns <c>true</c>; returns <c>false</c> without writing when
    /// <paramref name="property"/> is not a known reserved property.
    /// </summary>
    public static bool TrySetValue(this ReservedPropertyValue value, ReservedProperty property, object? newValue)
    {
        if (!ReservedProperties.TryGet(property, out var definition))
        {
            return false;
        }

        definition.Write(value, newValue);
        return true;
    }
}
