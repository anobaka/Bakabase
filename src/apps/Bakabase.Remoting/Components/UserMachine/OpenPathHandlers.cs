using System.Net;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Opens a file or folder in this machine's file manager, or reveals it in its parent.
/// </summary>
public sealed class OpenPathHandler(ActiveConnection connection, IShellOpener shell,
    ILogger<OpenPathHandler> logger) : PathHandlerBase(connection)
{
    public override string RouteKey => "GET /tool/open";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var localPath = await MapOrRefuseAsync(context, context.Request.Query["path"].ToString());

        if (localPath == null)
        {
            return;
        }

        var openInDirectory = string.Equals(context.Request.Query["openInDirectory"].ToString(), "true",
            StringComparison.OrdinalIgnoreCase);

        try
        {
            shell.Reveal(localPath, openInDirectory);
            await WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {Path}", localPath);
            await WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not open '{localPath}' on your machine: {e.Message}");
        }
    }
}

/// <summary>
/// Opens a file with whatever this machine associates with it.
/// </summary>
/// <remarks>
/// The server hands the path straight to <c>UseShellExecute</c>, and there that is fine:
/// the path came from its own library scan on its own disk. Here the path has been
/// translated onto this machine, and it must exist as a file before being run — without
/// that check, a mapping pointed at the wrong root turns "open this video" into starting
/// whatever program happens to sit at the resulting path.
/// </remarks>
public sealed class OpenFileHandler(ActiveConnection connection, IShellOpener shell,
    ILogger<OpenFileHandler> logger) : PathHandlerBase(connection)
{
    public override string RouteKey => "GET /tool/open-file";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var localPath = await MapOrRefuseAsync(context, context.Request.Query["path"].ToString());

        if (localPath == null)
        {
            return;
        }

        if (!File.Exists(localPath))
        {
            await WriteAsync(context, HttpStatusCode.NotFound,
                Directory.Exists(localPath)
                    ? $"'{localPath}' is a folder, not a file."
                    : $"'{localPath}' does not exist on this machine. Check the path mapping for that library.");
            return;
        }

        try
        {
            shell.Launch(localPath);
            await WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {Path}", localPath);
            await WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not open '{localPath}' on your machine: {e.Message}");
        }
    }
}
