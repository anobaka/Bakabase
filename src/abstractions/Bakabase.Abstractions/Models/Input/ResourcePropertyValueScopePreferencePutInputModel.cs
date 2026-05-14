using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Input;

public class ResourcePropertyValueScopePreferencePutInputModel
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Ordered scopes; null or empty list resets the preference to defaults (falls through to profile/global).
    /// </summary>
    public PropertyValueScope[]? Priorities { get; set; }

    public bool FallbackOnEmpty { get; set; }
}
