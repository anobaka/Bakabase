using System.Net;
using System.Text.Json;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Sends everything the client does not handle itself on to the server, and turns a
/// server that is not there into an answer the UI can render.
/// </summary>
public sealed class UpstreamForwarder(
    IHttpForwarder forwarder,
    IUpstreamTarget target,
    UpstreamTransformer transformer,
    HttpMessageInvoker invoker,
    ILogger<UpstreamForwarder> logger)
{
    /// <summary>
    /// How long a forwarded exchange may go without any traffic before it is dropped.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. This carries the UI hub, the discovery event stream and
    /// video that a viewer may pause for a long time; YARP's hundred-second default
    /// would cut all three, and the reconnect that follows is exactly the stutter the
    /// thin client is supposed to be free of. Not infinite, so a connection whose peer
    /// vanished without a FIN is eventually reclaimed.
    /// </remarks>
    public static readonly TimeSpan ActivityTimeout = TimeSpan.FromMinutes(30);

    private static readonly ForwarderRequestConfig RequestConfig = new()
    {
        ActivityTimeout = ActivityTimeout,
        // HTTP/1.1 for the upstream: the WebSocket upgrade the UI hub needs has no
        // HTTP/2 equivalent here, and the server is plain HTTP anyway.
        Version = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    public async Task ForwardAsync(HttpContext context)
    {
        var destination = target.BaseAddress;

        if (string.IsNullOrEmpty(destination))
        {
            // A window asking for a page has somewhere better to be than a JSON refusal
            // it cannot render. Everything else — the frontend's own fetches, a player
            // pulling a stream — still gets the refusal, which is what those callers
            // are written against.
            if (WantsAPage(context.Request))
            {
                context.Response.Redirect(RelayPaths.ConnectPath);
                return;
            }

            await WriteUnavailable(context, ClientForwardingFailure.NotConnected,
                "This client is not connected to a server yet.");
            return;
        }

        var error = await forwarder.SendAsync(context, destination, invoker, RequestConfig, transformer);

        if (error == ForwarderError.None)
        {
            return;
        }

        // The client hanging up is the normal end of a video or a hub connection, not a
        // failure worth reporting — and by then there is nobody left to report it to.
        if (error is ForwarderError.RequestCanceled or ForwarderError.RequestBodyCanceled
            or ForwarderError.ResponseBodyCanceled or ForwarderError.UpgradeRequestCanceled
            or ForwarderError.UpgradeResponseCanceled)
        {
            return;
        }

        var exception = context.GetForwarderErrorFeature()?.Exception;
        logger.LogWarning(exception, "Forwarding {Method} {Path} to {Destination} failed: {Error}",
            context.Request.Method, context.Request.Path, destination, error);

        await WriteUnavailable(context, ClientForwardingFailure.ServerUnreachable,
            "The Bakabase server is not answering. Check that it is running and reachable.");
    }

    /// <summary>
    /// Whether this request is a browser navigating, as opposed to code fetching.
    /// </summary>
    /// <remarks>
    /// Read from <c>Sec-Fetch-Mode</c> where it is present — it says outright whether
    /// the browser is navigating — and from <c>Accept</c> otherwise. Both are needed:
    /// the header is not sent by every embedded web view, and <c>Accept: text/html</c>
    /// alone would also match a <c>fetch</c> that happens to ask for HTML. Getting it
    /// wrong in one direction shows a JSON page; in the other it hands the frontend a
    /// redirect where it expected an error it knows how to display.
    /// </remarks>
    public static bool WantsAPage(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var mode = request.Headers["Sec-Fetch-Mode"].ToString();

        if (!string.IsNullOrEmpty(mode))
        {
            return mode.Equals("navigate", StringComparison.OrdinalIgnoreCase);
        }

        return request.Headers.Accept.ToString()
            .Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports a failure the same shape the server's own gate uses, so the frontend has
    /// one error format to understand rather than two.
    /// </summary>
    internal static async Task WriteUnavailable(HttpContext context, ClientForwardingFailure failure, string message)
    {
        if (context.Response.HasStarted)
        {
            // Mid-stream. Nothing can be said now; aborting is what tells the client
            // something went wrong, rather than a truncated body that looks complete.
            context.Abort();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = (int) HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Bakabase-Client"] = failure.ToString();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = (int) HttpStatusCode.ServiceUnavailable,
            message
        }), context.RequestAborted);
    }
}

/// <summary>
/// Why the client itself could not serve a request, as opposed to the server refusing
/// one. Travels in <c>X-Bakabase-Client</c>, alongside the server's own
/// <c>X-Bakabase-Remote-Access</c>, so the frontend can tell which side spoke.
/// </summary>
public enum ClientForwardingFailure
{
    None = 0,

    /// <summary>No server has been chosen yet.</summary>
    NotConnected = 1,

    /// <summary>The server did not answer.</summary>
    ServerUnreachable = 2,

    /// <summary>The request came from somewhere that is not this client's own UI.</summary>
    ForeignCaller = 3,

    /// <summary>
    /// An action that has to run on the user's machine, which this client does not know
    /// how to run yet. Distinct from a refusal: the answer is to update the client.
    /// </summary>
    NeedsNewerClient = 4,

    /// <summary>
    /// A server path with no local equivalent on this machine. The user has to say
    /// where that library lives here.
    /// </summary>
    PathNotMapped = 5
}
