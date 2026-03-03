using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// A property reference with optional per-property scope priority override.
/// Extends the concept of PropertyKey with scope priority configuration.
/// </summary>
public class PropertyKeyWithScopePriority
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }

    /// <summary>
    /// Optional per-property scope priority override.
    /// When null, the global PropertyValueScopePriority from ResourceOptions is used.
    /// When set, overrides the global priority for this specific property.
    /// </summary>
    public PropertyValueScope[]? ScopePriority { get; set; }
}
