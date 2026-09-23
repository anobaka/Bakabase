using System.Net;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Opens a resource's folder in this machine's file manager.
/// </summary>
/// <remarks>
/// <para>
/// The library lives on the server, so the path has to be asked for before it can be
/// translated. That is one round trip the all-in-one does not make, and it is why an
/// unreachable server is reported here as its own thing rather than folded into "no
/// mapping".
/// </para>
/// <para>
/// The server falls back to a resource's containing folder when its own path no longer
/// exists — a folder moved or deleted outside Bakabase. The same fallback runs here, but
/// checked against <em>this</em> filesystem, which is the one that has to open it. A
/// resource can be perfectly present on the server and missing on a machine whose mount
/// is stale, and only the local check notices.
/// </para>
/// </remarks>
public sealed class OpenResourceDirectoryHandler(
    ActiveConnection connection,
    IUpstreamApi upstream,
    IShellOpener shell,
    ILogger<OpenResourceDirectoryHandler> logger) : PathHandlerBase(connection)
{
    public override string RouteKey => "GET /resource/directory";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!int.TryParse(context.Request.Query["id"].ToString(), out var id))
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, "No resource id was given.");
            return;
        }

        var resource = await upstream.GetResourceAsync(id, context.RequestAborted);

        if (resource == null)
        {
            await WriteAsync(context, HttpStatusCode.ServiceUnavailable,
                $"Could not ask the server where resource {id} is. Check that it is still reachable.");
            return;
        }

        if (string.IsNullOrEmpty(resource.Path))
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, $"Resource {id} has no path.");
            return;
        }

        var localPath = await MapOrRefuseAsync(context, resource.Path);

        if (localPath == null)
        {
            return;
        }

        // Existence is judged here, not there. A mount that has gone stale on this
        // machine looks perfectly healthy from the server.
        if (!File.Exists(localPath) && !Directory.Exists(localPath))
        {
            var fallback = ClientPathMapperFallback(resource.Directory);

            if (fallback == null)
            {
                await WriteAsync(context, HttpStatusCode.NotFound,
                    $"'{localPath}' does not exist on this machine. Check the path mapping for that library.");
                return;
            }

            localPath = fallback;
        }

        try
        {
            shell.Reveal(localPath, inParentDirectory: false);
            await WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {Path}", localPath);
            await WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not open '{localPath}' on your machine: {e.Message}");
        }
    }

    /// <summary>
    /// The resource's containing folder on this machine, if it maps and exists. Null
    /// means there is nothing left to fall back to.
    /// </summary>
    private string? ClientPathMapperFallback(string? serverDirectory)
    {
        if (string.IsNullOrEmpty(serverDirectory))
        {
            return null;
        }

        var mapped = Paths.ClientPathMapper.Map(serverDirectory, Connection.Server?.PathMappings ?? []);

        return mapped.Mapped && Directory.Exists(mapped.LocalPath) ? mapped.LocalPath : null;
    }
}
