using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Signs in to a third-party site in a window on this machine.
/// </summary>
/// <remarks>
/// <para>
/// This is the one that was actively wrong before the split. The all-in-one opens a login
/// window on the machine running the server, which is right when that is the machine the
/// user is at — and, when it is not, opens somebody else's browser on a screen nobody is
/// watching and then reports success. Running it here is what makes the button mean what
/// it says.
/// </para>
/// <para>
/// The cookie goes back to the browser that asked, in the same shape the server returns,
/// and the frontend saves it exactly as it always has. It is not written here: which
/// account it belongs to and where it is stored are the server's business, and this
/// client is only the window.
/// </para>
/// </remarks>
public sealed class CookieCaptureHandler(ILogger<CookieCaptureHandler> logger) : IUserMachineHandler
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = {new JsonStringEnumConverter()}
    };

    public string RouteKey => "POST /tool/cookie-capture";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!Enum.TryParse<CookieValidatorTarget>(context.Request.Query["target"].ToString(), true, out var target))
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, "No site was named.");
            return;
        }

        // Resolved per request rather than held: these are transient, and a handler that
        // lives for the process would otherwise pin one copy of each for good.
        var flow = context.RequestServices.GetServices<ICookieCaptureFlow>()
            .FirstOrDefault(f => f.Target == target);

        if (flow == null)
        {
            await WriteAsync(context, HttpStatusCode.NotFound,
                $"Cookie capture is not supported for target: {target}");

            return;
        }

        try
        {
            var orchestrator = context.RequestServices.GetRequiredService<CookieCaptureOrchestrator>();
            var captured = await orchestrator.CaptureAsync(flow, context.RequestAborted);

            if (captured == null)
            {
                // Success with no data, matching the server: a cancelled sign-in is not an
                // error, and a 400 here would raise a toast over something the user chose.
                await WriteAsync(context, HttpStatusCode.OK,
                    context.RequestServices.GetRequiredService<ICookieCaptureLocalizer>().Cancelled);
                return;
            }

            // Carries the user agent of the window that opened here, on this machine —
            // which is the whole reason this runs on the client at all.
            await WriteAsync(context, HttpStatusCode.OK, null, captured);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Cookie capture for {Target} failed on this machine", target);
            await WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not open a sign-in window on your machine: {e.Message}");
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode status, string? message,
        CookieCaptureResult? data = null)
    {
        context.Response.StatusCode = (int) status;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = status == HttpStatusCode.OK ? 0 : (int) status,
            message,
            data
        }, Json), context.RequestAborted);
    }
}
