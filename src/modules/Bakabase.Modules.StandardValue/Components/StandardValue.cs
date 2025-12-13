using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components;

/// <summary>
/// Type-safe wrapper for StandardValue with compile-time type checking.
/// </summary>
/// <typeparam name="T">The CLR type of the value</typeparam>
public record StandardValue<T>(StandardValueType Type, T? Value)
{
    /// <summary>
    /// Get the value as object (for APIs that require object)
    /// </summary>
    public object? AsObject() => Value;

    /// <summary>
    /// Check if the value is null or empty
    /// </summary>
    public bool IsEmpty => Value switch
    {
        null => true,
        string s => string.IsNullOrEmpty(s),
        List<string> l => l.Count == 0,
        List<List<string>> ll => ll.Count == 0,
        List<TagValue> lt => lt.Count == 0,
        LinkValue lv => lv.IsEmpty,
        _ => false
    };
}
