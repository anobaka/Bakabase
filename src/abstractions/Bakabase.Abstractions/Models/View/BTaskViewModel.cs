using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.VisualBasic;

namespace Bakabase.Abstractions.Models.View;

public record BTaskViewModel
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int? Percentage { get; set; }
    public TimeSpan? Interval { get; set; }
    public DateTime? EnableAfter { get; set; }
    public BTaskStatus Status { get; set; }
    public string? Error { get; set; }
    public string? MessageOnInterruption { get; set; }
    public HashSet<string>? ConflictWithTaskKeys { get; set; }
    public TimeSpan? EstimateRemainingTime { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? ReasonForUnableToStart { get; set; }

    public BTaskViewModel(BTaskDescriptor btWrapper, BTaskDbModel? dbModel, string? reasonForUnableToStart)
    {
        Name = btWrapper.Name;
        Description = btWrapper.Description;
        Percentage = btWrapper.Percentage;
        Status = btWrapper.Status;
        Error = btWrapper.Error;
        MessageOnInterruption = btWrapper.MessageOnInterruption;
        ConflictWithTaskKeys = btWrapper.ConflictKeys;
        EstimateRemainingTime = btWrapper.EstimateRemainingTime;
        StartedAt = btWrapper.StartedAt;

        Interval = dbModel?.Interval;
        EnableAfter = dbModel?.EnableAfter;
        ReasonForUnableToStart = reasonForUnableToStart;
    }
}