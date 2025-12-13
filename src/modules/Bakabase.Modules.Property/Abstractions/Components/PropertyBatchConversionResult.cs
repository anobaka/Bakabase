namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// Result of batch property value conversion.
/// </summary>
public record PropertyBatchConversionResult(
    /// <summary>The converted DB values</summary>
    List<object?> NewDbValues,
    /// <summary>Whether any property options were modified during conversion</summary>
    bool PropertyOptionsChanged,
    /// <summary>The updated target property (may have new options)</summary>
    Bakabase.Abstractions.Models.Domain.Property UpdatedToProperty);
