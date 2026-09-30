using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.ResourceMove;

/// <summary>One source participates in a move; it never owns the physical move or its task state.</summary>
public interface IResourceSourceMoveHandler
{
    ResourceSource Source { get; }
    int Version => 1;
    /// <summary>Also evaluate moves of resources inside this source's local root. Such ancestor
    /// checks are constraints only; they do not move the ancestor or create location mutations.</summary>
    bool ProtectsLocalTree => false;

    /// <summary>Read-only, including during preview. Its result is persisted before physical work.</summary>
    Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Read-only recovery check against the frozen plan. Accept the recorded old or new
    /// location; reject a changed owner/third location without choosing a new plan or executor.</summary>
    Task ValidateRecordedStateAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Apply the persisted location mutation idempotently. A third/unexpected location
    /// must be rejected rather than overwritten. Never move files or change source identity.</summary>
    Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default);
}

public sealed record ResourceSourceMoveContext(
    int RootResourceId, int ResourceId, string SourcePath, string DestPath, ResourceSourceLink Link,
    bool IsAncestorConstraint = false, bool IsDestinationConstraint = false, bool IsSourceRestoration = false);

public sealed record ResourceSourceMoveEvaluation
{
    public string? ReasonCode { get; init; }
    public string? PreviousLocation { get; init; }
    public string? NewLocation { get; init; }
    /// <summary>Handler-owned immutable data; include stable platform row identity when needed.</summary>
    public string? StateJson { get; init; }
    /// <summary>Optional physical capability requirement. Multiple sources must agree on the
    /// same executor; the orchestrator invokes it once for the whole physical root.</summary>
    public ResourceMoveExecutorReference? Executor { get; init; }
}

public sealed record ResourceMoveExecutorReference(string Id, int Version = 1);

/// <summary>Durable per-link participation, frozen before moving any file.</summary>
public sealed class ResourceSourceMoveStep
{
    public int RootResourceId { get; set; }
    public int ResourceId { get; set; }
    public int LinkId { get; set; }
    public ResourceSource Source { get; set; }
    public string SourceKey { get; set; } = null!;
    public int HandlerVersion { get; set; }
    public string SourcePath { get; set; } = null!;
    public string DestPath { get; set; } = null!;
    public string? PreviousLocation { get; set; }
    public string? NewLocation { get; set; }
    public string? StateJson { get; set; }
    /// <summary>A checkpoint only; ApplyAsync must also tolerate a crash before this is saved.</summary>
    public bool Applied { get; set; }
}

public sealed class ResourceMoveExecutionPlan
{
    public int Version { get; set; } = 1;
    public string ExecutorId { get; set; } = "local-files";
    public int ExecutorVersion { get; set; } = 1;
    public List<ResourceSourceMoveStep> Sources { get; set; } = [];
}

public sealed class ResourceSourceMoveException(string reasonCode, string? message = null)
    : InvalidOperationException(message ?? reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}
