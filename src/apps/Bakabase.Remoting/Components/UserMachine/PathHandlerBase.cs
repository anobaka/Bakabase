using System.Net;
using System.Text.Json;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.Paths;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Shared groundwork for handlers that act on a file the server named.
/// </summary>
/// <remarks>
/// The refusal is the interesting part. A path with no mapping is a question only the
/// user can answer — which folder on this machine is that library? — so it is reported
/// with the path in it and a header the frontend can key a "set this up" prompt off,
/// rather than being opened at a guessed location or failing as though something broke.
/// </remarks>
public abstract class PathHandlerBase(ActiveConnection connection) : IUserMachineHandler
{
    /// <summary>The server in use, and with it the mappings that apply to its paths.</summary>
    protected ActiveConnection Connection => connection;

    public abstract string RouteKey { get; }

    public abstract Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues);

    /// <summary>
    /// Translates a server path for this machine, or writes the refusal and returns null.
    /// </summary>
    protected async Task<string?> MapOrRefuseAsync(HttpContext context, string? serverPath)
    {
        var result = ClientPathMapper.Map(serverPath, connection.Server?.PathMappings ?? []);

        if (result.Mapped)
        {
            return result.LocalPath;
        }

        context.Response.StatusCode = (int) HttpStatusCode.NotFound;
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Bakabase-Client"] = ClientForwardingFailure.PathNotMapped.ToString();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = (int) HttpStatusCode.NotFound,
            message = string.IsNullOrWhiteSpace(serverPath)
                ? "The server did not say which file to open."
                : $"There is no folder on this machine mapped to '{serverPath}'. " +
                  "Add a path mapping for that library in the client's settings.",
            serverPath = result.UnmappedServerPath
        }), context.RequestAborted);

        return null;
    }

    protected static Task WriteAsync(HttpContext context, HttpStatusCode status, string? message) =>
        UserMachineResponse.WriteAsync(context, status, message);
}
