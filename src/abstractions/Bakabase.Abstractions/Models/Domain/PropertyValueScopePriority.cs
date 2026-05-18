using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public class PropertyValueScopePriority
{
    public PropertyValueScope Scope { get; set; }

    /// <summary>
    /// When this scope's value is empty, whether to continue with the next scope in the list.
    /// False = stop and render blank. Ignored for the last entry (no next to fall through to).
    /// </summary>
    public bool FallbackOnEmpty { get; set; }
}
