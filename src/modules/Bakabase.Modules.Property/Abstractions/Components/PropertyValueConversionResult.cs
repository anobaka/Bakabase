namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// Result of a single property value conversion.
/// </summary>
public record PropertyValueConversionResult(
    /// <summary>The converted DB value</summary>
    object? NewDbValue,
    /// <summary>Whether the property options were modified during conversion</summary>
    bool PropertyOptionsChanged,
    /// <summary>The updated target property (may have new options)</summary>
    Bakabase.Abstractions.Models.Domain.Property UpdatedToProperty);
