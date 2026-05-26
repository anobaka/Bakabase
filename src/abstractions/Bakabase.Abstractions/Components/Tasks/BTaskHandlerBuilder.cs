using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Blueprint for a background task. Construct via <see cref="BTaskBuilder.Create(string)"/>
/// and chain fluent extension methods — the old object-initializer form still
/// works but external integrators don't have to puzzle out which of the 16
/// fields are mandatory.
///
/// Only <see cref="Id"/> and <see cref="RunAction"/> have no usable default;
/// everything else falls back to a sensible value (Any/Any type, ID-based
/// localized name, no conflict keys, etc.). Use the With* / On* / etc.
/// extensions to override.
/// </summary>
public record BTaskHandlerBuilder
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public Func<string> GetName { get; init; } = () => string.Empty;
    public Func<string?>? GetDescription { get; set; }
    public Func<string?>? GetMessageOnInterruption { get; set; }
    public CancellationToken? CancellationToken { get; init; }

    /// <summary>
    /// The actual task body. Stored under a non-clashing name so the fluent
    /// <c>.Run(args => ...)</c> extension method isn't shadowed by the
    /// property when chaining (since invoking a delegate property has the
    /// same syntactic shape as a method call).
    /// </summary>
    public Func<BTaskArgs, Task> RunAction { get; init; } = _ => Task.CompletedTask;

    public HashSet<string>? ConflictKeys { get; init; }
    public HashSet<string>? DependsOn { get; init; }
    public BTaskLevel Level { get; init; } = BTaskLevel.Default;
    public TimeSpan? Interval { get; init; }
    public bool IsPersistent { get; init; }
    public IServiceProvider? RootServiceProvider { get; init; }
    public Func<BTaskStatus, BTask, Task>? OnStatusChange { get; set; }
    public Func<BTask, Task>? OnPercentageChanged { get; set; }
    public BTaskType Type { get; init; } = BTaskType.Any;
    public BTaskResourceType ResourceType { get; init; } = BTaskResourceType.Any;
    public object[]? ResourceKeys { get; set; }
    public bool StartNow { get; set; }
    public BTaskDuplicateIdHandling DuplicateIdHandling { get; set; } = BTaskDuplicateIdHandling.Reject;
    public BTaskRetryPolicy? RetryPolicy { get; init; }
    public BTaskDependencyFailurePolicy DependencyFailurePolicy { get; init; } = BTaskDependencyFailurePolicy.Wait;
}

/// <summary>
/// Entry point for the fluent task-builder API.
/// <code>
/// await taskManager.Enqueue(
///     BTaskBuilder.Create("MyTask")
///         .Named(() => localizer.BTask_Name("MyTask"))
///         .ConflictsWith("MyTask")
///         .Persistent()
///         .Run(async args => { ... }));
/// </code>
/// </summary>
public static class BTaskBuilder
{
    /// <summary>
    /// Creates a builder with an auto-generated GUID id. Use the <c>(id)</c>
    /// overload whenever the task has a meaningful key that needs to dedupe or
    /// be looked up by id.
    /// </summary>
    public static BTaskHandlerBuilder Create() => new();

    public static BTaskHandlerBuilder Create(string id) => new() { Id = id, GetName = () => id };

    public static BTaskHandlerBuilder Create(string id, Func<string> getName) =>
        new() { Id = id, GetName = getName };
}

/// <summary>
/// Chainable setters for <see cref="BTaskHandlerBuilder"/>. Each returns a new
/// record (with-expression) so callers can build pipelines without an
/// intermediate variable.
/// </summary>
public static class BTaskHandlerBuilderFluentExtensions
{
    public static BTaskHandlerBuilder Named(this BTaskHandlerBuilder b, Func<string> getName) =>
        b with { GetName = getName };

    public static BTaskHandlerBuilder Named(this BTaskHandlerBuilder b, string name) =>
        b with { GetName = () => name };

    public static BTaskHandlerBuilder Describe(this BTaskHandlerBuilder b, Func<string?> getDescription) =>
        b with { GetDescription = getDescription };

    public static BTaskHandlerBuilder Describe(this BTaskHandlerBuilder b, string? description) =>
        b with { GetDescription = () => description };

    public static BTaskHandlerBuilder InterruptionMessage(this BTaskHandlerBuilder b,
        Func<string?> getMessage) =>
        b with { GetMessageOnInterruption = getMessage };

    public static BTaskHandlerBuilder InterruptionMessage(this BTaskHandlerBuilder b,
        string? message) =>
        b with { GetMessageOnInterruption = () => message };

    public static BTaskHandlerBuilder Run(this BTaskHandlerBuilder b, Func<BTaskArgs, Task> run) =>
        b with { RunAction = run };

    public static BTaskHandlerBuilder OfType(this BTaskHandlerBuilder b, BTaskType type) =>
        b with { Type = type };

    public static BTaskHandlerBuilder OfResourceType(this BTaskHandlerBuilder b,
        BTaskResourceType resourceType) =>
        b with { ResourceType = resourceType };

    public static BTaskHandlerBuilder ConflictsWith(this BTaskHandlerBuilder b, params string[] keys) =>
        b with { ConflictKeys = keys.Length == 0 ? null : new HashSet<string>(keys) };

    public static BTaskHandlerBuilder ConflictsWith(this BTaskHandlerBuilder b,
        IEnumerable<string>? keys)
    {
        if (keys == null) return b with { ConflictKeys = null };
        var set = keys as HashSet<string> ?? [..keys];
        return b with { ConflictKeys = set.Count == 0 ? null : set };
    }

    public static BTaskHandlerBuilder DependsOn(this BTaskHandlerBuilder b, params string[] taskIds) =>
        b with { DependsOn = taskIds.Length == 0 ? null : new HashSet<string>(taskIds) };

    public static BTaskHandlerBuilder DependsOn(this BTaskHandlerBuilder b,
        IEnumerable<string>? taskIds)
    {
        if (taskIds == null) return b with { DependsOn = null };
        var set = taskIds as HashSet<string> ?? [..taskIds];
        return b with { DependsOn = set.Count == 0 ? null : set };
    }

    public static BTaskHandlerBuilder Every(this BTaskHandlerBuilder b, TimeSpan? interval) =>
        b with { Interval = interval };

    public static BTaskHandlerBuilder Persistent(this BTaskHandlerBuilder b, bool persistent = true) =>
        b with { IsPersistent = persistent };

    public static BTaskHandlerBuilder StartImmediately(this BTaskHandlerBuilder b,
        bool startNow = true) =>
        b with { StartNow = startNow };

    public static BTaskHandlerBuilder OnDuplicateId(this BTaskHandlerBuilder b,
        BTaskDuplicateIdHandling handling) =>
        b with { DuplicateIdHandling = handling };

    /// <summary>Shortcut for the most common non-default <see cref="DuplicateIdHandling"/>.</summary>
    public static BTaskHandlerBuilder ReplaceIfExists(this BTaskHandlerBuilder b) =>
        b with { DuplicateIdHandling = BTaskDuplicateIdHandling.Replace };

    /// <summary>Shortcut for the second-most-common <see cref="DuplicateIdHandling"/>.</summary>
    public static BTaskHandlerBuilder IgnoreIfExists(this BTaskHandlerBuilder b) =>
        b with { DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore };

    public static BTaskHandlerBuilder AtLevel(this BTaskHandlerBuilder b, BTaskLevel level) =>
        b with { Level = level };

    public static BTaskHandlerBuilder Critical(this BTaskHandlerBuilder b) =>
        b with { Level = BTaskLevel.Critical };

    public static BTaskHandlerBuilder WithRetry(this BTaskHandlerBuilder b,
        BTaskRetryPolicy? policy) =>
        b with { RetryPolicy = policy };

    public static BTaskHandlerBuilder OnDependencyFailure(this BTaskHandlerBuilder b,
        BTaskDependencyFailurePolicy policy) =>
        b with { DependencyFailurePolicy = policy };

    public static BTaskHandlerBuilder ForResources(this BTaskHandlerBuilder b,
        params object[] resourceKeys) =>
        b with { ResourceKeys = resourceKeys.Length == 0 ? null : resourceKeys };

    public static BTaskHandlerBuilder WithCancellationToken(this BTaskHandlerBuilder b,
        CancellationToken ct) =>
        b with { CancellationToken = ct };

    public static BTaskHandlerBuilder WithServiceProvider(this BTaskHandlerBuilder b,
        IServiceProvider sp) =>
        b with { RootServiceProvider = sp };

    // Named WhenStatusChanges / WhenPercentageChanges (not OnStatusChange /
    // OnPercentageChanged) to avoid colliding with the delegate properties of
    // the same shape — `builder.OnStatusChange(...)` would otherwise resolve to
    // invoking the delegate, not setting it.
    public static BTaskHandlerBuilder WhenStatusChanges(this BTaskHandlerBuilder b,
        Func<BTaskStatus, BTask, Task> handler) =>
        b with { OnStatusChange = handler };

    public static BTaskHandlerBuilder WhenPercentageChanges(this BTaskHandlerBuilder b,
        Func<BTask, Task> handler) =>
        b with { OnPercentageChanged = handler };
}
