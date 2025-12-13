using Bakabase.Abstractions.Models.Domain.Constants;
using JetBrains.Annotations;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Interface for building predefined background tasks.
/// Implementations are automatically discovered and registered by the DI container.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IPredefinedBTaskBuilder
{
    /// <summary>
    /// Unique identifier for the task
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Determines if the task should be enabled
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Gets the task interval. Return null for one-time tasks.
    /// Default is 1 minute for recurring tasks.
    /// </summary>
    TimeSpan? GetInterval() => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Types of options that this task depends on.
    /// When any of these options change, the task will be re-evaluated.
    /// </summary>
    Type[] WatchedOptionsTypes => [];

    /// <summary>
    /// Gets the localized task name
    /// </summary>
    string GetName();

    /// <summary>
    /// Gets the localized task description
    /// </summary>
    string? GetDescription() => null;

    /// <summary>
    /// Gets the localized message shown when task is interrupted
    /// </summary>
    string? GetMessageOnInterruption() => null;

    /// <summary>
    /// The task type
    /// </summary>
    BTaskType Type => BTaskType.Any;

    /// <summary>
    /// The resource type this task operates on
    /// </summary>
    BTaskResourceType ResourceType => BTaskResourceType.Any;

    /// <summary>
    /// Keys used for conflict detection. Tasks with overlapping keys cannot run simultaneously.
    /// </summary>
    HashSet<string>? ConflictKeys => null;

    /// <summary>
    /// Task IDs that must complete before this task starts
    /// </summary>
    HashSet<string>? DependsOn => null;

    /// <summary>
    /// Task level. Critical tasks block app exit.
    /// </summary>
    BTaskLevel Level => BTaskLevel.Default;

    /// <summary>
    /// Whether the task should persist in the task list after completion
    /// </summary>
    bool IsPersistent => true;

    /// <summary>
    /// Retry policy for failed tasks. Return null to disable retries.
    /// </summary>
    BTaskRetryPolicy? RetryPolicy => null;

    /// <summary>
    /// How to handle dependency failures
    /// </summary>
    BTaskDependencyFailurePolicy DependencyFailurePolicy => BTaskDependencyFailurePolicy.Wait;

    /// <summary>
    /// Executes the task logic
    /// </summary>
    Task RunAsync(BTaskArgs args);
}
