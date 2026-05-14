using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public class PropertyValueScopePreference
{
    public int ResourceId { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Ordered scope priorities for this (resource, property). Null = no override.
    /// When non-null, lower-priority layers (profile/global) are not consulted.
    /// </summary>
    public PropertyValueScope[]? Priorities { get; set; }

    /// <summary>
    /// When the highest-priority scope's value is empty, whether to fall back to the next scope in Priorities.
    /// False = render blank rather than fall back.
    /// </summary>
    public bool FallbackOnEmpty { get; set; }
}
