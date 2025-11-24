using System;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Defines a predefined task with its enable conditions and configuration dependencies
/// </summary>
public class PredefinedTaskDefinition
{
    /// <summary>
    /// Unique identifier for the task
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Factory method to build the task handler
    /// </summary>
    public required Func<IServiceProvider, BTaskHandlerBuilder> BuildHandler { get; init; }

    /// <summary>
    /// Predicate to determine if the task should be enabled
    /// </summary>
    public required Func<bool> IsEnabled { get; init; }

    /// <summary>
    /// Optional method to get the task interval (can be dynamically calculated from options)
    /// </summary>
    public Func<TimeSpan?>? GetInterval { get; init; }

    /// <summary>
    /// Types of options that this task depends on. When any of these options change, the task will be re-evaluated.
    /// </summary>
    public required Type[] WatchedOptionsTypes { get; init; }
}
