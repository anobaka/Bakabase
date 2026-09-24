using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Opens a link in the browser on the machine the user is sitting at.
/// </summary>
/// <remarks>
/// <para>
/// The all-in-one hands the URL straight to the shell, and there that is fine: the only
/// thing that can reach the endpoint is a page the same machine is serving. Here the URL
/// arrives from a server that may be somebody else's, so the scheme is checked first.
/// <c>UseShellExecute</c> asks the OS to open whatever the string names — on Windows
/// that includes <c>file:</c> paths and any registered protocol handler, which is a
/// program launch dressed as a link.
/// </para>
/// <para>
/// This is the shape of hardening the split creates generally: an input the all-in-one
/// could treat as its own now crosses a machine boundary.
/// </para>
/// </remarks>
public sealed class OpenUrlHandler(IShellOpener shell, ILogger<OpenUrlHandler> logger) : IUserMachineHandler
{
    public string RouteKey => "GET /gui/url";

    public async Task HandleAsync(HttpContext context, IReadOnlyDictionary<string, string> routeValues)
    {
        var url = context.Request.Query["url"].ToString();

        if (!IsOpenable(url))
        {
            logger.LogWarning("Refused to open {Url}: only http and https links are opened on this machine.", url);
            await WriteAsync(context, HttpStatusCode.BadRequest,
                "Only http and https links can be opened on your machine.");
            return;
        }

        try
        {
            shell.Launch(url);
            await WriteAsync(context, HttpStatusCode.OK, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {Url}", url);
            await WriteAsync(context, HttpStatusCode.InternalServerError,
                $"Could not open the link on your machine: {e.Message}");
        }
    }

    /// <summary>
    /// Web links only. Anything else is a request to start a program, whatever it looks
    /// like in the address bar.
    /// </summary>
    public static bool IsOpenable(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    private static async Task WriteAsync(HttpContext context, HttpStatusCode status, string? message)
    {
        context.Response.StatusCode = (int) status;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = status == HttpStatusCode.OK ? 0 : (int) status,
            message
        }), context.RequestAborted);
    }
}
