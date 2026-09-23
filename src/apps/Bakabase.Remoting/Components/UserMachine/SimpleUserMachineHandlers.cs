using System.Net;
using System.Text.Json;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Explains that the deleted files are on the server, not here.
/// </summary>
/// <remarks>
/// Deleting happens on the server — that is where the files are — so they land in the
/// server's recycle bin, and opening this machine's would show something unrelated. This
/// is not a path-mapping problem and no amount of configuration fixes it, which is why
/// the answer says so rather than offering to be set up.
/// </remarks>
public sealed class RecycleBinHandler : IUserMachineHandler
{
    public string RouteKey => "GET /file/recycle-bin";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        context.Response.StatusCode = (int) HttpStatusCode.NotImplemented;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = (int) HttpStatusCode.NotImplemented,
            message = "Deleting happens on the server, so the files are in its recycle bin rather than this " +
                      "machine's. Open it there."
        }), context.RequestAborted);
    }
}

/// <summary>
/// Answers the file-icon lookup with nothing.
/// </summary>
/// <remarks>
/// Not a degradation: the desktop app's own GUI adapter has always returned null here
/// too, so this matches the behaviour the frontend already handles everywhere. Answering
/// locally rather than forwarding saves a round trip to a server that would have to
/// refuse it anyway.
/// </remarks>
public sealed class FileIconHandler : IUserMachineHandler
{
    public string RouteKey => "GET /file/icon";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new {code = 0, data = (string?) null}),
            context.RequestAborted);
    }
}

/// <summary>
/// Opens the userscript install link in this machine's browser.
/// </summary>
/// <remarks>
/// The link is built against this client's own loopback address on purpose. A userscript
/// installed from here runs in an ordinary browser tab that has no device key, so it can
/// only reach Bakabase through the forwarding layer — which does have one, and signs on
/// its behalf.
/// </remarks>
public sealed class TampermonkeyInstallHandler(
    ILoopbackAddressProvider loopback,
    IShellOpener shell,
    ILogger<TampermonkeyInstallHandler> logger) : IUserMachineHandler
{
    public string RouteKey => "GET /tampermonkey/install";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var url = loopback.BuildUrl("/tampermonkey/script/bakabase.user.js");

        context.Response.ContentType = "application/json";

        try
        {
            shell.Launch(url);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new {code = 0}), context.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {Url}", url);
            context.Response.StatusCode = (int) HttpStatusCode.InternalServerError;

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = (int) HttpStatusCode.InternalServerError,
                message = $"Could not open the script installer on your machine: {e.Message}"
            }), context.RequestAborted);
        }
    }
}

/// <summary>
/// Opens an AIGC artifact's file on this machine.
/// </summary>
/// <remarks>
/// Artifacts are produced and stored by the server, so where the file is has to be asked
/// for before it can be translated — the same shape as opening a resource's folder, and
/// the same reason an unreachable server is reported separately from an unmapped
/// library.
/// </remarks>
public sealed class OpenAigcArtifactHandler(
    ActiveConnection connection,
    IUpstreamApi upstream,
    IShellOpener shell,
    ILogger<OpenAigcArtifactHandler> logger) : PathHandlerBase(connection)
{
    public override string RouteKey => "POST /aigc/artifacts/{id:int}/open";

    public override async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        if (!routeValues.TryGetValue("id", out var idText) || !int.TryParse(idText, out var id))
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, "No artifact id was given.");
            return;
        }

        var serverPath = await upstream.GetAigcArtifactPathAsync(id, context.RequestAborted);

        if (serverPath == null)
        {
            await WriteAsync(context, HttpStatusCode.ServiceUnavailable,
                $"Could not ask the server where artifact {id} is. Check that it is still reachable.");
            return;
        }

        var localPath = await MapOrRefuseAsync(context, serverPath);

        if (localPath == null)
        {
            return;
        }

        if (!File.Exists(localPath) && !Directory.Exists(localPath))
        {
            await WriteAsync(context, HttpStatusCode.NotFound,
                $"'{localPath}' does not exist on this machine. Check the path mapping for that library.");
            return;
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
}
