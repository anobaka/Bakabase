namespace Bakabase.Abstractions.Components.Telemetry;

/// <summary>In-memory correlation inherited by one task's asynchronous work; never persisted with a recurring job.</summary>
public static class TelemetryExecutionContext
{
    private static readonly AsyncLocal<State?> Current = new();
    public static bool HasScope => Current.Value != null;
    public static TelemetryActor? CurrentActor => Current.Value?.Actor;

    public static IDisposable Begin(TelemetryActor? actor)
    {
        var previous = Current.Value;
        Current.Value = new State(actor);
        return new Restore(previous);
    }

    private sealed record State(TelemetryActor? Actor);
    private sealed class Restore(State? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
