using System;
using Bakabase.Abstractions.Components.Telemetry;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Services.Telemetry;

public static class TelemetryHttpContext
{
    public const string HeaderPrefix = "X-Bakabase-Analytics-";

    public static TelemetryActor? Read(HttpContext? context)
    {
        if (context == null || context.Request.Headers[HeaderPrefix + "Consent"] != "1") return null;
        var headers = context.Request.Headers;
        var client = Uuid(headers[HeaderPrefix + "Client"].ToString());
        if (client == null) return null;
        return new TelemetryActor(client, Uuid(headers[HeaderPrefix + "Session"].ToString()),
            Uuid(headers[HeaderPrefix + "Operation"].ToString()), Surface(headers[HeaderPrefix + "Surface"].ToString()));
    }

    public static string? Uuid(string? value) => value is { Length: 36 } &&
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id.ToString("D") : null;

    public static string Surface(string? value) => value switch
    {
        "browser" or "desktop_webview" or "mobile_app" => value,
        _ => "unknown"
    };

    public static TelemetryActor? Validate(TelemetryActor? actor) => actor != null && Uuid(actor.ClientId) is { } id
        ? new TelemetryActor(id, Uuid(actor.SessionId), Uuid(actor.OperationId), Surface(actor.Surface)) : null;
}
