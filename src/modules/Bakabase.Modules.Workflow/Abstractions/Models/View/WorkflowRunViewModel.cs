using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowRunViewModel
{
    public IReadOnlyList<WorkflowParameterSourceView> ParameterSources { get; set; } = [];

    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public bool HasPlanSnapshot { get; set; }
    public string? PlanName { get; set; }
    public string? TriggerKind { get; set; }
    public List<WorkflowRunPlanNodeView> PlanNodes { get; set; } = [];
    public WorkflowRunStatus Status { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime StartedAt { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? CompletedAt { get; set; }
    public string? PayloadSummary { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public string? OutputItemsJson { get; set; }
    public bool OutputPreviewTruncated { get; set; }
    public int FailedItemCount { get; set; }
    public List<WorkflowRunStepStat> StepStats { get; set; } = [];
    public List<WorkflowNodeExecution> NodeExecutions { get; set; } = [];
    public string? ErrorMessage { get; set; }

    /// <summary>Live execution detail; persisted Status remains authoritative.</summary>
    public int? Progress { get; set; }
    public string? Process { get; set; }
    /// <summary>The current step's recorded kind, when available; live progress may fill older runs.</summary>
    public string? ActivityKind { get; set; }
    public string? Stage { get; set; }

    /// <summary>Set while the run is waiting, so the drawer can say what it is waiting for.</summary>
    public string? WaitReason { get; set; }

    /// <summary>What to ask the user for, so the resume form can render itself.</summary>
    public string? WaitPromptJson { get; set; }

    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? WaitingSince { get; set; }

    public int? CurrentStepIndex { get; set; }

    public static WorkflowRunViewModel From(WorkflowRun r) => new()
    {
        ParameterSources = r.ParameterSources,
        Id = r.Id,
        WorkflowDefinitionId = r.WorkflowDefinitionId,
        HasPlanSnapshot = r.HasPlanSnapshot,
        PlanName = r.PlanName,
        TriggerKind = r.TriggerKind,
        PlanNodes = r.PlanNodes,
        Status = r.Status,
        // Run dates are written with server-local DateTime.Now; SQLite drops Kind.
        // Normalize only the response, preserving the persisted execution record.
        StartedAt = r.StartedAt.ToUniversalTime(),
        CompletedAt = r.CompletedAt?.ToUniversalTime(),
        PayloadSummary = r.PayloadSummary,
        InputCount = r.InputCount,
        OutputCount = r.OutputCount,
        OutputItemsJson = r.OutputItemsJson,
        OutputPreviewTruncated = r.OutputPreviewTruncated,
        FailedItemCount = r.FailedItemCount,
        StepStats = r.StepStats,
        NodeExecutions = r.NodeExecutions.Select(n => n with
        {
            StartedAt = StoredNodeUtc(n.StartedAt),
            CompletedAt = n.CompletedAt is { } completed ? StoredNodeUtc(completed) : null
        }).ToList(),
        ErrorMessage = r.ErrorMessage,
        WaitReason = r.WaitReason,
        WaitPromptJson = r.WaitPromptJson,
        WaitingSince = r.WaitingSince?.ToUniversalTime(),
        CurrentStepIndex = r.CurrentStepIndex,
        ActivityKind = RecordedActivityKind(r),
    };

    // Node-attempt dates use DateTime.UtcNow, unlike the older run dates. An unspecified
    // value from SQLite is already UTC and must not be shifted by the server's time zone.
    private static DateTime StoredNodeUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();

    private static string? RecordedActivityKind(WorkflowRun run)
    {
        // A node attempt freezes its own identity and kind. Never infer an old run's
        // current kind from today's editable definition or a neighbouring completed step.
        if (run.CurrentStepIndex is { } step && step >= 0)
        {
            var node = run.NodeExecutions.Where(n => n.StepIndex == step && !string.IsNullOrWhiteSpace(n.Kind))
                .OrderByDescending(n => n.Id).ThenByDescending(n => n.Attempt).FirstOrDefault();
            return node?.Kind ?? run.StepStats.LastOrDefault(s => s.StepIndex == step && !string.IsNullOrWhiteSpace(s.Kind))?.Kind
                ?? run.PlanNodes.SingleOrDefault(n => n.Order == step)?.Kind;
        }

        // Batch runs have no checkpoint cursor, but their currently running attempt is explicit.
        return run.Status == WorkflowRunStatus.Running
            ? run.NodeExecutions.Where(n => n.Status == "running" && !string.IsNullOrWhiteSpace(n.Kind))
                .OrderByDescending(n => n.Id).ThenByDescending(n => n.Attempt).FirstOrDefault()?.Kind
            : null;
    }
}
