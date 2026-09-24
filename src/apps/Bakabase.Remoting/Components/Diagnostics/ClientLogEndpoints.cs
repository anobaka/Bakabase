using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Infrastructures.Components.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.Diagnostics;

/// <summary>
/// The client's own log, under <c>/client/log</c>.
/// </summary>
/// <remarks>
/// The forwarded <c>/log</c> shows the server's, which is the right answer for every
/// question about the library and the wrong one for every question about this program:
/// a failed handshake, a refused signature, a player that would not start. Both are
/// reachable from the same page, which is why they are separate routes rather than one
/// that guesses.
/// </remarks>
public static class ClientLogEndpoints
{
    public const string Prefix = "/client/log";

    /// <summary>
    /// Most a single request will return. The page pages through nothing — it is a tail,
    /// not an archive — and an unbounded read of a rolled 100 MB file would hang the
    /// window it is being rendered in.
    /// </summary>
    public const int MaxTake = 2000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Prefix, async (HttpContext context) =>
        {
            var directory = LogDirectory(context);

            if (directory == null)
            {
                await WriteAsync(context, new {available = false, entries = Array.Empty<object>()});
                return;
            }

            var take = Math.Clamp(
                int.TryParse(context.Request.Query["take"].ToString(), out var requested) ? requested : 200,
                1, MaxTake);

            var entries = ClientLogReader.Read(directory, take,
                context.Request.Query["level"].ToString(),
                context.Request.Query["contains"].ToString());

            await WriteAsync(context, new {available = true, directory, entries});
        });

        // Deliberately takes no path. `/tool/open` is a forwarded route: the client
        // translates the server path it is given and refuses one that maps nowhere, which
        // is exactly the wrong treatment for a directory this process owns on this disk.
        // The log page keeps its own route rather than naming a directory through
        // /client/app/open, because from here there is only one directory to mean.
        endpoints.MapPost($"{Prefix}/open", (HttpContext context, ILoggerFactory loggers) =>
            ClientAppEndpoints.OpenAsync(context, LogDirectory(context), loggers));
    }

    /// <summary>
    /// Where this client writes its log, or null in a host that has no application data
    /// directory — a test host, which has no log either.
    /// </summary>
    private static string? LogDirectory(HttpContext context)
    {
        try
        {
            return (context.RequestServices.GetService(typeof(AppService)) as AppService)?.AppInfo.LogPath;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(HttpContext context, object payload)
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = context.Response.StatusCode == StatusCodes.Status200OK ? 0 : context.Response.StatusCode,
            data = payload
        }, Json), context.RequestAborted);
    }
}
