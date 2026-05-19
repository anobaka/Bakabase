using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public class PropertyValueScopePreference
{
    public int ResourceId { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Ordered scope priorities for this (resource, property). Null = no override.
    /// Each entry carries a FallbackOnEmpty flag: when that scope has no value,
    /// the chain continues only if the flag is true; otherwise it stops and renders blank.
    /// </summary>
    public PropertyValueScopePriority[]? Priorities { get; set; }
}
