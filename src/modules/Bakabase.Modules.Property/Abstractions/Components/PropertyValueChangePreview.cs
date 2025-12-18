namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// Preview of a single value change.
/// </summary>
public record PropertyValueChangePreview(
    /// <summary>Original value display</summary>
    string? FromDisplay,
    /// <summary>Converted value display</summary>
    string? ToDisplay,
    /// <summary>Original serialized biz value</summary>
    string? FromSerialized,
    /// <summary>Converted serialized biz value</summary>
    string? ToSerialized);
