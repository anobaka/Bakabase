using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Abstractions.Components.Tasks;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Per-run state object passed through every activity invocation.
/// </summary>
public sealed class WorkflowExecutionContext
{
    /// <summary>Frozen public invocation values, inherited unchanged by nested execution contexts.</summary>
    public WorkflowInvocationSnapshot? ParameterSnapshot { get; init; }

    public required long RunId { get; init; }
    public required int WorkflowDefinitionId { get; init; }
    public required string TriggerKind { get; init; }

    /// <summary>The payload published with the trigger. Typed by <see cref="TriggerKind"/>.</summary>
    public required object Payload { get; init; }

    /// <summary>Raw config JSON of the activity currently executing.</summary>
    public required string ActivityConfigJson { get; init; }

    /// <summary>Server-persisted prompt authorizing this resume only. Never supplied by the caller or inherited by nested planners.</summary>
    public string? ResumePromptJson { get; init; }

    /// <summary>The validated remaining chain. Artifact planning stops at an undeclared step.</summary>
    public IReadOnlyList<WorkflowPlannedActivity> FollowingActivities { get; init; } = [];

    /// <summary>Rebase planning on the consumer being inspected, so nested planners cannot see themselves or upstream nodes.</summary>
    public WorkflowExecutionContext ForPlannedActivity(string configJson, IReadOnlyList<WorkflowPlannedActivity> following) => new()
    {
        ParameterSnapshot = ParameterSnapshot,
        RunId = RunId, WorkflowDefinitionId = WorkflowDefinitionId, TriggerKind = TriggerKind,
        Payload = Payload, ActivityConfigJson = configJson, FollowingActivities = following,
        TargetItemType = TargetItemType, Variables = Variables, Services = Services, Logger = Logger,
        ReportProgress = ReportProgress, ReportStage = ReportStage, ReportOutputMetadata = ReportOutputMetadata
    };

    /// <summary>
    /// For <see cref="WorkflowItemTypeBehavior.AdaptToNext"/> activities, the resolved
    /// output type for this position (mirrors what the chain walker decided). Null for
    /// Passthrough/Fixed activities — they don't need it.
    /// </summary>
    public string? TargetItemType { get; init; }

    /// <summary>
    /// The current item's variable bag (capability map E4): named values captured by upstream
    /// activities (<c>transform.text.capture</c>) and read back by interpolating ones
    /// (<c>transform.text.template</c>). The bag travels WITH the item, not inside it — an
    /// item's CLR shape never grows fields for chain-local state. On expansion each child
    /// inherits a copy of its parent's bag. Mutations are visible to every later step for
    /// the same item.
    /// </summary>
    public IDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>DI scope provider — use for resolving scoped services like DbContext.</summary>
    public required IServiceProvider Services { get; init; }

    public required ILogger Logger { get; init; }

    /// <summary>
    /// Progress within this one step, 0-100, plus a line describing what it is doing. The runner
    /// already advances the run's own progress a step at a time; this is for an activity whose
    /// single step takes long enough that a step boundary is far too coarse — downloading a file,
    /// unpacking an archive. It is scaled into this step's share of the run, so an activity
    /// reporting 0-100 never fights the runner's own accounting.
    /// <para>Defaults to doing nothing, so no activity has to check whether it is set.</para>
    /// </summary>
    public Func<int, BTaskText?, Task> ReportProgress { get; init; } = (_, _) => Task.CompletedTask;

    /// <summary>Transient, machine-readable activity phase; never persisted as a completed result.</summary>
    public Func<string, Task> ReportStage { get; init; } = _ => Task.CompletedTask;

    /// <summary>Explicit safe, bounded output metadata; persisted independently of checkpoint payloads.</summary>
    public Func<IReadOnlyList<WorkflowOutputBlock>, Task> ReportOutputMetadata { get; init; } = _ => Task.CompletedTask;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Deserialize the current activity's config into a typed shape. Absent config is the
    /// activity's business (it gets <c>default</c> and applies its own defaults); MALFORMED
    /// config is not — that throws <see cref="WorkflowActivityConfigException"/>, which fails
    /// the run rather than silently running the step on defaults.
    /// </summary>
    public T? GetConfig<T>()
    {
        if (string.IsNullOrEmpty(ActivityConfigJson)) return default;
        try { return JsonSerializer.Deserialize<T>(ActivityConfigJson, JsonOptions); }
        catch (JsonException ex)
        {
            throw new WorkflowActivityConfigException(
                $"The step's configuration is not valid for {typeof(T).Name} and the run cannot proceed: {ex.Message}",
                ex);
        }
    }
}
