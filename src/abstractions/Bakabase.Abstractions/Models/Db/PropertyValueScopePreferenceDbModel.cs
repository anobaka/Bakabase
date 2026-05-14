using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record PropertyValueScopePreferenceDbModel
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Comma-separated PropertyValueScope ints in priority order; null means no override (fall through to global/profile rules).
    /// </summary>
    public string? Priorities { get; set; }

    public bool FallbackOnEmpty { get; set; }
}
