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

    /// <summary>
    /// Which step the run is on, for a single-item run that persists its progress step by step.
    /// Null for a batch run, which has no meaningful per-step cursor — the whole chain is one unit
    /// of work there.
    /// </summary>
    public int? CurrentStepIndex { get; set; }

    /// <summary>
    /// The item at the cursor, snapshotted with its CLR type so the run can be picked up by a
    /// different process than the one that suspended it.
    /// </summary>
    public string? CurrentItemJson { get; set; }

    /// <summary>Why the run is waiting; shown in the runs list.</summary>
    public string? WaitReason { get; set; }

    /// <summary>What the interface should ask for, shaped by the activity that suspended.</summary>
    public string? WaitPromptJson { get; set; }

    /// <summary>When the wait started, so "waiting for three days" is visible as such.</summary>
    public DateTime? WaitingSince { get; set; }

    /// <summary>
    /// The answer, parked here between the resume request and the run actually restarting. The
    /// runner is re-entered through a background task carrying only the run id, so the signal has
    /// to travel with the row.
    /// </summary>
    public string? PendingSignalJson { get; set; }
}
