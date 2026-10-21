using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Telemetry;

public static class TelemetryCapture
{
    public static void TryTrack(IServiceProvider services, string eventName,
        IReadOnlyDictionary<string, object?> properties, TelemetryActor? actor = null)
    {
        try { services.GetService<IProductTelemetry>()?.Track(eventName, properties, actor); }
        catch { /* Optional diagnostics cannot change task/domain outcomes. */ }
    }
}
