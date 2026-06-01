using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Db;

public record WorkflowRunDbModel
{
    [Key] public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }

    public WorkflowRunStatus Status { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Serialized event payload — kept so a run can survive process restart.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Short human-readable summary of the payload for the runs list.</summary>
    public string? PayloadSummary { get; set; }

    /// <summary>Items that entered the chain (output of <c>trigger.ExtractItems</c>).</summary>
    public int InputCount { get; set; }

    /// <summary>Items surviving every activity (i.e. reached the last step's exit).</summary>
    public int OutputCount { get; set; }

    /// <summary>Items dropped because an activity threw under Skip-on-error.</summary>
    public int FailedItemCount { get; set; }

    /// <summary>Serialized <c>List&lt;WorkflowRunStepStat&gt;</c> — the per-activity funnel.</summary>
    public string? StepStatsJson { get; set; }

    public string? ErrorMessage { get; set; }
}
