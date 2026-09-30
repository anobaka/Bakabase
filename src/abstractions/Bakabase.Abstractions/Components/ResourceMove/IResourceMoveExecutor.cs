using Bootstrap.Components.Tasks;

namespace Bakabase.Abstractions.Components.ResourceMove;

/// <summary>Exactly one versioned executor runs for a physical root, irrespective of its source links.
/// Implementations persist ownership before mutations and must resume from their existing journal.</summary>
public interface IResourceMoveExecutor
{
    string Id { get; }
    int Version { get; }
    Task ExecuteOrResumeAsync(ResourceMoveExecutionState state, Func<Task> checkpoint,
        Func<int, Task> progress, PauseToken pause, CancellationToken cancellation,
        Func<string, string, bool> authorized);
    void Cleanup(ResourceMoveExecutionState state);
}

/// <summary>The physical executor cannot mutate domain status, source links or reservations.</summary>
public sealed class ResourceMoveExecutionState
{
    public required int Id { get; init; }
    public required string SourcePath { get; init; }
    public required string DestPath { get; init; }
    public string? MoveJournalJson { get; set; }
    public bool PhysicalMoveStarted { get; set; }
}
