using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.HealthScore.Models;

public enum ResourceMatcherLeafKind
{
    Property = 1,
    File = 2,
}

/// <summary>
/// A leaf node in <see cref="ResourceMatcher"/>. Negation is per-leaf to keep
/// the tree shallow without adding a Not combinator everywhere.
/// </summary>
public record ResourceMatcherLeaf
{
    public ResourceMatcherLeafKind Kind { get; set; }
    public bool Negated { get; set; }
    public bool Disabled { get; set; }

    // Property-leaf fields (Kind == Property)
    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public SearchOperation? Operation { get; set; }
    public object? PropertyDbValue { get; set; }

    // File-leaf fields (Kind == File)
    public string? FilePredicateId { get; set; }

    /// <summary>
    /// Predicate parameters as a raw JSON string. Stored as a string (not
    /// JsonElement) so it survives ASP.NET's pooled request body buffer
    /// release — see https://github.com/dotnet/aspnetcore/issues/29048.
    /// The frontend serializes its parameter object before sending; the
    /// predicate deserializes back to its concrete <c>ParametersType</c>
    /// when invoked.
    /// </summary>
    public string? FilePredicateParametersJson { get; set; }
}
