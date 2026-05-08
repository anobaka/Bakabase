namespace Bakabase.Modules.AI.Components.Aigc;

/// <summary>
/// Executes a single generation run identified by its id. Handles provider invocation,
/// artifact persistence, status transitions, and progress reporting.
/// Implementation lives behind a scoped DI registration so each run gets a fresh DbContext.
/// </summary>
public interface IAigcRunExecutor
{
    Task ExecuteAsync(
        int runId,
        Func<int, string?, CancellationToken, Task>? onProgress,
        CancellationToken ct);
}
