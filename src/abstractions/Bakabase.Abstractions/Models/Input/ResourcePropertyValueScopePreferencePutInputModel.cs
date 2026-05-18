using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Input;

public class ResourcePropertyValueScopePreferencePutInputModel
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Ordered scopes with per-scope fallback flags; null or empty list resets the preference
    /// to defaults (falls through to profile/global).
    /// </summary>
    public PropertyValueScopePriority[]? Priorities { get; set; }
}
