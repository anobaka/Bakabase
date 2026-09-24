using System.Net;
using System.Text.Json;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Sends everything the relay does not handle itself on to the server, and turns a server
/// that is not there — or not the one this window shows — into an answer the UI can render.
/// </summary>
/// <remarks>
/// Nothing goes out before <see cref="UpstreamIdentity"/> has confirmed that the address
/// answers as the server this relay is for, and every request goes to the address it
/// confirmed. A confirmation is read from memory; see there for when one is asked for.
/// </remarks>
public sealed class UpstreamForwarder(
    IHttpForwarder forwarder,
    ActiveConnection target,
    UpstreamIdentity identity,
    UpstreamTransformer transformer,
    HttpMessageInvoker invoker,
    UpstreamStanding standing,
    ILogger<UpstreamForwarder> logger,
    IRelayUnavailablePage? unavailablePage = null)
{
    /// <summary>The header the relay names its own refusals in. A wire name, kept from the removed thin client.</summary>
    public const string FailureHeader = "X-Bakabase-Client";

    /// <summary>
    /// How long a forwarded exchange may go without any traffic before it is dropped.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. This carries the UI hub, the discovery event stream and
    /// video that a viewer may pause for a long time; YARP's hundred-second default
    /// would cut all three, and the reconnect that follows is exactly the stutter a
    /// managed server's window is supposed to be free of. Not infinite, so a connection whose peer
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
        if (string.IsNullOrEmpty(target.BaseAddress))
        {
            await RefuseWithoutServerAsync(context);
            return;
        }

        UpstreamIdentityCheck? check;

        try
        {
            check = await identity.EnsureAsync(context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The browser gave up waiting; there is nobody left to answer.
            return;
        }

        if (check == null)
        {
            // Forgotten while this request waited.
            await RefuseWithoutServerAsync(context);
            return;
        }

        if (!check.IsConfirmed)
        {
            await RefuseAsync(context, check);
            return;
        }

        // The address that was confirmed, not a second read of the store: the two differ
        // only when the address changed a moment ago, and this one is known to be the
        // server's.
        var destination = check.Address;
        var error = await forwarder.SendAsync(context, destination, invoker, RequestConfig, transformer);

        if (error == ForwarderError.None)
        {
            standing.Answered(context.Response);
            return;
        }

        var exception = context.GetForwarderErrorFeature()?.Exception;

        if (UpstreamIdentityHandler.Refusal(exception) is { } refused)
        {
            // A new connection was needed, and by then the address answered as someone else
            // (or nobody). Nothing was sent; said the way a refusal before sending is.
            if (context.Response.HasStarted)
            {
                context.Abort();
            }
            else if (refused.Check == null)
            {
                await RefuseWithoutServerAsync(context);
            }
            else
            {
                await RefuseAsync(context, refused.Check);
            }

            return;
        }

        standing.Failed(error);

        if (UpstreamStanding.IsTheServers(error))
        {
            // Gone mid-exchange: a restart, a network change — the moments an address
            // changes hands. Whoever answers there next is asked who it is first.
            identity.Suspect();
        }

        // The client hanging up is the normal end of a video or a hub connection, not a
        // failure worth reporting — and by then there is nobody left to report it to.
        if (error is ForwarderError.RequestCanceled or ForwarderError.RequestBodyCanceled
            or ForwarderError.ResponseBodyCanceled or ForwarderError.UpgradeRequestCanceled
            or ForwarderError.UpgradeResponseCanceled)
        {
            return;
        }

        logger.LogWarning(exception, "Forwarding {Method} {Path} to {Destination} failed: {Error}",
            context.Request.Method, context.Request.Path, destination, error);

        await WriteUnavailable(context, ClientForwardingFailure.ServerUnreachable,
            "The Bakabase server is not answering. Check that it is running and reachable.");
    }

    /// <summary>The relay has no server: it was removed on this computer.</summary>
    private static async Task RefuseWithoutServerAsync(HttpContext context)
    {
        // A window asking for a page has somewhere better to be than a JSON refusal it
        // cannot render. Everything else — the frontend's own fetches, a player pulling a
        // stream — still gets the refusal, which is what those callers are written against.
        if (WantsAPage(context.Request))
        {
            context.Response.Redirect(RelayPaths.ConnectPath);
            return;
        }

        // In the desktop app a relay has no server only once that server was removed here:
        // the store view behind it is empty from then on.
        await WriteUnavailable(context, ClientForwardingFailure.NotConnected,
            "This computer no longer manages this server. Add it again from this computer's " +
            "Devices and sharing page.");
    }

    /// <summary>
    /// Nothing is forwarded: the address answers as someone else, or nobody could be
    /// identified there.
    /// </summary>
    /// <remarks>
    /// A navigation gets a page saying so, at the address it asked for, so a reload once
    /// things are put right asks again rather than landing somewhere else. Everything else
    /// gets the relay's JSON refusal with the reason in its message.
    /// </remarks>
    private async Task RefuseAsync(HttpContext context, UpstreamIdentityCheck check)
    {
        standing.Refused(check);

        var name = target.Server?.ServerName;

        if (unavailablePage != null && WantsAPage(context.Request) && !context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = (int) HttpStatusCode.ServiceUnavailable;
            await unavailablePage.WriteAsync(context, check, name);
            return;
        }

        await WriteUnavailable(context,
            check.IsMismatch ? ClientForwardingFailure.WrongServer : ClientForwardingFailure.ServerUnreachable,
            check.Describe(name));
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
        context.Response.Headers[FailureHeader] = failure.ToString();

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = (int) HttpStatusCode.ServiceUnavailable,
            message
        }), context.RequestAborted);
    }
}

/// <summary>
/// Why the relay itself could not serve a request, as opposed to the server refusing one.
/// Travels in <c>X-Bakabase-Client</c>, alongside the server's own
/// <c>X-Bakabase-Remote-Access</c>, so the frontend can tell which side spoke.
/// </summary>
/// <remarks>
/// The names are wire values that a managed server's UI of any version reads, including
/// one written for the removed thin client, so they are never renamed.
/// </remarks>
public enum ClientForwardingFailure
{
    None = 0,

    /// <summary>The relay has no server: this computer no longer manages it.</summary>
    NotConnected = 1,

    /// <summary>The server did not answer, or nobody could be identified at its address.</summary>
    ServerUnreachable = 2,

    /// <summary>The request came from somewhere that is not the relay's own window.</summary>
    ForeignCaller = 3,

    /// <summary>
    /// An action that has to run on the user's machine, which this computer does not know
    /// how to run yet. Distinct from a refusal: the answer is to update Bakabase here.
    /// </summary>
    NeedsNewerClient = 4,

    /// <summary>
    /// A server path with no local equivalent on this machine. The user has to say
    /// where that library lives here.
    /// </summary>
    PathNotMapped = 5,

    /// <summary>
    /// The server's address answers as another server, or as this computer itself, so
    /// nothing was sent there.
    /// </summary>
    WrongServer = 6
}

/// <summary>
/// The page a window navigating through a relay is shown when nothing can be forwarded:
/// its address answers as someone else, or nobody could be identified there.
/// </summary>
public interface IRelayUnavailablePage
{
    /// <param name="serverName">What the server this relay is for is called here, if known.</param>
    Task WriteAsync(HttpContext context, UpstreamIdentityCheck check, string? serverName);
}
