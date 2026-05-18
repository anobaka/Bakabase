using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record PropertyValueScopePreferenceDbModel
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Comma-separated "scope:fallbackFlag" entries in priority order, where flag is 0 or 1.
    /// Example: "0:1,1000:1,1002:0". Null/empty means no override (fall through to profile/global rules).
    /// </summary>
    public string? Priorities { get; set; }
}
