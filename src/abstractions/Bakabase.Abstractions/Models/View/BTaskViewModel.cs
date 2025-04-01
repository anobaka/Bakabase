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
    public BTaskLevel Level { get; set; }
    public BTaskType Type { get; set; }
    public object[]? ResourceKeys { get; set; }

    public BTaskViewModel(BTaskHandler handler, string? reasonForUnableToStart)
    {
        Id = handler.Task.Id;
        Name = handler.Task.Name;
        Description = handler.Task.Description;
        Percentage = handler.Task.Percentage;
        Process = handler.Task.Process;
        Level = handler.Task.Level;
        Status = handler.Task.Status;
        Error = handler.Task.Error;
        MessageOnInterruption = handler.Task.MessageOnInterruption;
        ConflictWithTaskKeys = handler.Task.ConflictKeys;
        EstimateRemainingTime = handler.EstimateRemainingTime;
        StartedAt = handler.Task.StartedAt;
        IsPersistent = handler.Task.IsPersistent;
        NextTimeStartAt = handler.NextTimeStartAt;
        Interval = handler.Task.Interval;
        EnableAfter = handler.Task.EnableAfter > DateTime.Now ? handler.Task.EnableAfter : null;
        Type = handler.Task.Type;
        ResourceKeys = handler.Task.ResourceKeys;

        Elapsed = handler.Sw.Elapsed == TimeSpan.Zero ? null : handler.Sw.Elapsed;
        ReasonForUnableToStart = reasonForUnableToStart;
    }
}