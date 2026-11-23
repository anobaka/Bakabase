namespace Bakabase.Modules.AI.Services;

/// <summary>Optional caller-owned progress observation; the AI module never depends on a workflow.</summary>
public static class LlmExecutionScope
{
    private static readonly AsyncLocal<Func<LlmFeatureExecutionState, CancellationToken, Task>?> Observer = new();

    public static IDisposable Begin(Func<LlmFeatureExecutionState, CancellationToken, Task> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var previous = Observer.Value;
        Observer.Value = observer;
        return new Scope(previous);
    }

    internal static Task ReportAsync(LlmFeatureExecutionState state, CancellationToken ct) =>
        Observer.Value?.Invoke(state, ct) ?? Task.CompletedTask;

    private sealed class Scope(Func<LlmFeatureExecutionState, CancellationToken, Task>? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Observer.Value = previous;
            _disposed = true;
        }
    }
}
