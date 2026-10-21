namespace Bakabase.Abstractions.Components.Telemetry;

/// <summary>Best-effort product events. Never affects the outcome of a business operation.</summary>
public interface IProductTelemetry
{
    void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null,
        TelemetryActor? actor = null);
}

/// <summary>Anonymous analytics correlation only. Never use these client-supplied values for authorization.</summary>
public sealed record TelemetryActor(string ClientId, string? SessionId = null,
    string? OperationId = null, string? Surface = null);

public interface ITelemetryActorAccessor
{
    TelemetryActor? CurrentActor { get; }
}
