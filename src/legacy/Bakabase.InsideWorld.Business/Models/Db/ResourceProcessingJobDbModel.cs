using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Models.Db;

/// <summary>Durable coordinator, prepared-owner or worker state; never a replicated resource row.</summary>
public sealed class ResourceProcessingJobDbModel
{
    [Key] public string Id { get; set; } = null!;
    public string JobId { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string RequesterId { get; set; } = null!;
    public string OwnerId { get; set; } = null!;
    public string OwnerEpoch { get; set; } = null!;
    public int ResourceId { get; set; }
    public long ResourceRevision { get; set; }
    public string RuleHash { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string State { get; set; } = null!;
    public string InputJson { get; set; } = null!;
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public long ProcessedCount { get; set; }
    public long? TotalCount { get; set; }
    public string? Phase { get; set; }
    public string? CurrentStepPhase { get; set; }
    public long? CurrentStepProcessedCount { get; set; }
    public long? CurrentStepTotalCount { get; set; }
    public int Attempt { get; set; }
    public bool PauseRequested { get; set; }
    public bool CancelRequested { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
