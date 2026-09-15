namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Read-only configuration/environment checks. Implementations must not create tasks, mutate
/// files or probe/download remote content. Payload can be absent during an editor check;
/// payload-dependent requirements must then remain undecided rather than fail speculatively.
/// Services belongs to the scope performing the check, not the singleton activity's scope.
/// </summary>
public sealed record WorkflowValidationContext
{
    public required IServiceProvider Services { get; init; }
    public required string ConfigJson { get; init; }
    public required string TriggerKind { get; init; }
    public string? TriggerFilterJson { get; init; }
    public bool IsExecution { get; init; }
    public object? Payload { get; init; }
}

public record WorkflowValidationIssue
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? MessageKey { get; init; }
    /// <summary>"error" blocks execution; "warning" is advisory.</summary>
    public string Severity { get; init; } = "error";
    /// <summary>
    /// This requirement can change with the actual input (for example, a cached input may not
    /// need an external provider). It is deferred only before a manual payload is built; the
    /// complete execution check must still pass after the payload is available.
    /// </summary>
    public bool DependsOnPayload { get; init; }
}
