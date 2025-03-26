using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.VisualBasic;

namespace Bakabase.Abstractions.Models.View;

public record BTaskViewModel
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int? Percentage { get; set; }
    public string? Process { get; set; }
    public TimeSpan? Interval { get; set; }
    public DateTime? EnableAfter { get; set; }
    public BTaskStatus Status { get; set; }
    public string? Error { get; set; }
    public string? MessageOnInterruption { get; set; }
    public HashSet<string>? ConflictWithTaskKeys { get; set; }
    public TimeSpan? EstimateRemainingTime { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? ReasonForUnableToStart { get; set; }
    public bool IsPersistent { get; set; }
    public DateTime? NextTimeStartAt { get; set; }
    public TimeSpan? Elapsed { get; set; }

    public BTaskViewModel(BTaskDescriptor descriptor, string? reasonForUnableToStart)
    {
        Id = descriptor.Id;
        Name = descriptor.Name;
        Description = descriptor.Description;
        Percentage = descriptor.Percentage;
        Process = descriptor.Process;
        Status = descriptor.Status;
        Error = descriptor.Error;
        MessageOnInterruption = descriptor.MessageOnInterruption;
        ConflictWithTaskKeys = descriptor.ConflictKeys;
        EstimateRemainingTime = descriptor.EstimateRemainingTime;
        StartedAt = descriptor.StartedAt;
        IsPersistent = descriptor.IsPersistent;
        NextTimeStartAt = descriptor.NextTimeStartAt;
        Interval = descriptor.Interval;
        EnableAfter = descriptor.EnableAfter > DateTime.Now ? descriptor.EnableAfter : null;
        Elapsed = descriptor.Sw.Elapsed == TimeSpan.Zero ? null : descriptor.Sw.Elapsed;

        ReasonForUnableToStart = reasonForUnableToStart;
    }
}