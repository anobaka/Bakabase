using Bakabase.Abstractions.Components.Telemetry;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Services.Telemetry;

public sealed class TelemetryActorAccessor(IHttpContextAccessor httpContext) : ITelemetryActorAccessor
{
    public TelemetryActor? CurrentActor => TelemetryExecutionContext.HasScope ? TelemetryExecutionContext.CurrentActor :
        TelemetryHttpContext.Read(httpContext.HttpContext);
}
