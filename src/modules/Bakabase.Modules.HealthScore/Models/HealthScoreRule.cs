namespace Bakabase.Modules.HealthScore.Models;

/// <summary>
/// One scoring rule: a predicate tree plus a delta to apply when matched.
/// Negative deltas reduce score; -BaseScore effectively zeroes it (no special "stop" semantics).
/// </summary>
public record HealthScoreRule
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ResourceMatcher Match { get; set; } = new();
    public decimal Delta { get; set; }
}
