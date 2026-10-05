namespace Bakabase.Modules.PostParser.Services;

/// <summary>Optional progress observer scoped to the current asynchronous parser execution.</summary>
public static class PostParserExecutionScope
{
    private static readonly AsyncLocal<Func<string, CancellationToken, Task>?> Observer = new();

    public static IDisposable Begin(Func<string, CancellationToken, Task> reportStage)
    {
        ArgumentNullException.ThrowIfNull(reportStage);
        var previous = Observer.Value;
        Observer.Value = reportStage;
        return new Scope(previous);
    }

    public static Task ReportStageAsync(string stage, CancellationToken ct = default) =>
        Observer.Value?.Invoke(stage, ct) ?? Task.CompletedTask;

    private sealed class Scope(Func<string, CancellationToken, Task>? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Observer.Value = previous;
        }
    }
}
