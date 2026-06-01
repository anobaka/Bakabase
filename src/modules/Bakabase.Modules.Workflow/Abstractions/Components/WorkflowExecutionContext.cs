using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Per-run state object passed through every activity invocation.
/// </summary>
public sealed class WorkflowExecutionContext
{
    public required long RunId { get; init; }
    public required int WorkflowDefinitionId { get; init; }
    public required string TriggerKind { get; init; }

    /// <summary>The payload published with the trigger. Typed by <see cref="TriggerKind"/>.</summary>
    public required object Payload { get; init; }

    /// <summary>Raw config JSON of the activity currently executing.</summary>
    public required string ActivityConfigJson { get; init; }

    /// <summary>
    /// For <see cref="WorkflowItemTypeBehavior.AdaptToNext"/> activities, the resolved
    /// output type for this position (mirrors what the chain walker decided). Null for
    /// Passthrough/Fixed activities — they don't need it.
    /// </summary>
    public string? TargetItemType { get; init; }

    /// <summary>DI scope provider — use for resolving scoped services like DbContext.</summary>
    public required IServiceProvider Services { get; init; }

    public required ILogger Logger { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Deserialize the current activity's config into a typed shape.</summary>
    public T? GetConfig<T>()
    {
        if (string.IsNullOrEmpty(ActivityConfigJson)) return default;
        try { return JsonSerializer.Deserialize<T>(ActivityConfigJson, JsonOptions); }
        catch (JsonException) { return default; }
    }
}
