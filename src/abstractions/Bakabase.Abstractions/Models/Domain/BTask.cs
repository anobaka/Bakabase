using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record BTask
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? MessageOnInterruption { get; set; }
    public int? Percentage { get; set; }
    public TimeSpan? Interval { get; set; }
    public DateTime? EnableAfter { get; set; }
    public BTaskStatus Status { get; set; }
    public string? Error { get; set; }
    public HashSet<string>? ConflictWithTaskKeys { get; set; }
    public TimeSpan? EstimateRemainingTime { get; set; }
}