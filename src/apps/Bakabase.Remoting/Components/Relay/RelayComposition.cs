using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Extensions;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Remoting.Components.BatchPlay;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Diagnostics;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Remoting.Components.Relay;

/// <summary>
/// What every relay needs to know about where it runs, and nothing about who composed it.
/// </summary>
/// <param name="ResolvePort">
/// The loopback port the relay's own listener is bound to. The guard, the self-address
/// check and every URL handed to a local player are derived from it, so it must be the
/// port actually bound, not a preference. Resolved lazily: a host that binds at start
/// only knows it once the container exists.
/// </param>
/// <param name="AppVersion">This program's version, reported by the context endpoint.</param>
/// <param name="TempPlaylistDirectory">Where batch play writes playlists for a local player.</param>
public sealed record RelayEnvironment(
    Func<IServiceProvider, int> ResolvePort,
    string AppVersion,
    Func<IServiceProvider, string> TempPlaylistDirectory);

/// <summary>
/// The relay: a loopback listener an embedded browser talks to, which signs and forwards
/// to a server on another machine and runs the actions whose effect belongs on this one.
/// </summary>
/// <remarks>
/// <para>
/// Composed by two products. The legacy thin client builds exactly one, with its connect
/// page and updater around it. The all-in-one builds one per server it manages, each with
/// its own container and port, next to its own in-process server — never inside that
/// server's container or pipeline, whose play and open routes the dispatcher below would
/// otherwise intercept.
/// </para>
/// <para>
/// Deliberately thin: no response caching, no compression, no buffering, no static files.
/// Each of those would sit between a video stream and the player, and the one thing this
/// layer must not do is get in the way of bytes it is only passing along.
/// </para>
/// </remarks>
public static class RelayComposition
{
    /// <summary>
    /// Registers everything a relay needs except the connection store, the data directory
    /// and the product's own endpoints, which each composer supplies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stores and directories are <c>TryAdd</c>: a composer registers its own first — the
    /// all-in-one a one-entry view over its managed-server list, the thin client the file
    /// it has always used.
    /// </para>
    /// <para>
    /// The logger factory is the composer's too: the relay logs through whichever one was
    /// registered before this call, with the forwarder's per-request lines held back — see
    /// <see cref="RelayLogging"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRelayCore(this IServiceCollection services, RelayEnvironment environment)
    {
        services.AddRelayLoggingFloor();

        services.AddSingleton(environment);
        services.AddHttpForwarder();

        // The guard reads switch tickets from here. The all-in-one registers one instance
        // shared by every relay it runs, since the ticket is minted by whoever opens the
        // window; a relay nobody switches into — the thin client — still gets its own, so
        // the guard is always armed with something and a ticket-shaped parameter is always
        // taken out of the address.
        services.TryAddSingleton(sp => new RelayNavigationTokens(sp.GetService<TimeProvider>() ?? TimeProvider.System));

        services.TryAddSingleton<IClientConnectionStore, ClientConnectionStore>();
        services.TryAddSingleton<ServerClock>();
        services.TryAddSingleton<ActiveConnection>();
        services.TryAddSingleton<IUpstreamTarget>(sp => sp.GetRequiredService<ActiveConnection>());
        services.TryAddSingleton<IClientCredentialProvider>(sp => sp.GetRequiredService<ActiveConnection>());
        services.TryAddSingleton<UpstreamTransformer>();
        services.TryAddSingleton(_ => CreateUpstreamInvoker());
        services.TryAddSingleton<UpstreamStanding>();
        services.TryAddSingleton<UpstreamForwarder>();

        // Its own signed client, for the few calls the relay makes on its own behalf
        // rather than on the browser's.
        services.AddHttpClient<IUpstreamContextProbe, UpstreamContextProbe>()
            .AddHttpMessageHandler(sp => new DeviceSigningHandler(
                sp.GetRequiredService<IClientCredentialProvider>(), sp.GetRequiredService<ServerClock>()));

        services.AddHttpClient<IUpstreamApi, UpstreamApi>()
            .AddHttpMessageHandler(sp => new DeviceSigningHandler(
                sp.GetRequiredService<IClientCredentialProvider>(), sp.GetRequiredService<ServerClock>()));

        // The port the relay is bound to, so a handshake can recognise an address that
        // points back here.
        services.TryAddSingleton(sp => new ClientSelfAddress(environment.ResolvePort(sp)));

        services.TryAddSingleton(sp => new ClientContextEndpoint(
            sp.GetRequiredService<ActiveConnection>(),
            sp.GetRequiredService<IUpstreamContextProbe>(),
            environment.AppVersion));

        // Actions whose effect lands on whatever machine runs them. Anything declared in
        // UserMachineRoutes without a handler here is answered as "this relay is behind"
        // rather than forwarded to a server that would only refuse it.
        services.TryAddSingleton<IShellOpener, OsShellOpener>();
        services.AddSingleton<IUserMachineHandler, OpenUrlHandler>();
        services.AddSingleton<IUserMachineHandler, OpenPathHandler>();
        services.AddSingleton<IUserMachineHandler, OpenFileHandler>();
        services.AddSingleton<IUserMachineHandler, OpenResourceDirectoryHandler>();
        services.TryAddSingleton<IPlayerExecutableLocator, DefaultPlayerExecutableLocator>();
        services.TryAddSingleton<LocalPlayerResolver>();
        services.TryAddSingleton<ILoopbackAddressProvider>(sp => new LoopbackAddressProvider(environment.ResolvePort(sp)));
        services.TryAddSingleton<LocalPlayback>();
        services.AddSingleton<IUserMachineHandler, PlayItemHandler>();
        services.AddSingleton<IUserMachineHandler, PlayResourceHandler>();
        services.AddSingleton<IUserMachineHandler, PlayRandomResourceHandler>();

        // Batch play runs the server's own orchestration in this process, with the
        // library read over HTTP and the files resolved against this machine. Registered
        // ahead of AddPlayerModule, whose defaults assume both are local and which only
        // fills in what nobody claimed.
        services.TryAddScoped<IBatchPlayResourceSource, UpstreamBatchPlayResourceSource>();
        services.TryAddScoped<IBatchPlayPlaylistSource, UpstreamBatchPlayPlaylistSource>();
        services.TryAddSingleton<IBatchPlayFileResolver>(sp => new ClientBatchPlayFileResolver(
            sp.GetRequiredService<ActiveConnection>(), sp.GetRequiredService<ILoopbackAddressProvider>()));
        services.AddPlayerModule();

        services.AddSingleton<IConfigureOptions<PlayerModuleOptions>>(sp =>
            new ConfigureOptions<PlayerModuleOptions>(o =>
                o.TempPlaylistDirectory = environment.TempPlaylistDirectory(sp)));

        services.AddSingleton<IUserMachineHandler, BatchPlayCandidatesHandler>();
        services.AddSingleton<IUserMachineHandler, BatchPlayResourcesHandler>();
        services.AddSingleton<IUserMachineHandler, PlaylistBatchPlayCandidatesHandler>();
        services.AddSingleton<IUserMachineHandler, PlaylistBatchPlayHandler>();
        services.AddSingleton<IUserMachineHandler, RecycleBinHandler>();
        services.AddSingleton<IUserMachineHandler, FileIconHandler>();
        services.AddSingleton<IUserMachineHandler, TampermonkeyInstallHandler>();
        services.AddSingleton<IUserMachineHandler, OpenAigcArtifactHandler>();
        services.TryAddSingleton<ILocaleEmulatorLauncher, LocaleEmulatorLauncher>();
        services.AddSingleton<IUserMachineHandler, DLsiteLaunchHandler>();

        // Signing in to a third-party site opens a window, so it has to open here. The
        // flows and the orchestration are the server's own; only the window is local.
        //
        // "Resources" is what the modules' resource names are laid out for, and what every
        // host built through AppUtils.CreateAppHostBuilder already sets — the thin client
        // among them. Stated here because a relay the desktop app composes has no such host
        // around it, and with the default the sign-in window's labels came out as their
        // resource keys.
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.TryAddTransient<ICookieCaptureLocalizer, ThirdPartyCookieCaptureLocalizer>();
        services.TryAddTransient<CookieCaptureOrchestrator>();
        foreach (var flow in typeof(ICookieCaptureFlow).Assembly.GetTypes()
                     .Where(t => t is {IsAbstract: false, IsInterface: false} &&
                                 typeof(ICookieCaptureFlow).IsAssignableFrom(t)))
        {
            services.AddTransient(typeof(ICookieCaptureFlow), flow);
        }

        services.AddSingleton<IUserMachineHandler, CookieCaptureHandler>();
        services.TryAddSingleton<UserMachineDispatcher>();

        services.AddRouting();

        return services;
    }

    /// <summary>
    /// The relay's pipeline: the loopback guard, then the user-machine dispatcher, then
    /// the relay's own endpoints and the composer's, then everything else to the server.
    /// </summary>
    /// <param name="mapLocal">The composer's own endpoints, under <see cref="RelayPaths.Prefix"/>.</param>
    /// <param name="mapThisMachinesDiagnostics">
    /// <para>
    /// Whether the page may read this program's own log (<see cref="ClientLogEndpoints"/>)
    /// and learn and open its directories (<see cref="ClientAppEndpoints"/>). Off unless a
    /// composer asks, because what those reveal depends entirely on what "this program" is.
    /// </para>
    /// <para>
    /// The thin client asks: it runs no server, its log and its data directory are only its
    /// own, and its window has nowhere else to show them. The desktop app's relays must not:
    /// there the log is the whole app's — this device's own server included, with the
    /// pairing code it prints while locked out and the address of every other server it
    /// manages — and the data directory is the one holding every managed server's key. The
    /// page asking is another server's own code, so answering would hand that server the
    /// way to take over the device that manages it. Unmapped, those paths fall through to
    /// the composer's own <c>/client</c> catch-all, never to the server.
    /// </para>
    /// </param>
    public static void UseRelayPipeline(this IApplicationBuilder app, Action<IEndpointRouteBuilder> mapLocal,
        bool mapThisMachinesDiagnostics = false)
    {
        var environment = app.ApplicationServices.GetRequiredService<RelayEnvironment>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RelayComposition).FullName!);
        // The port comes from the address the host actually bound, not from a second copy of
        // the same decision — the guard has to be right about it or it either refuses
        // everything or protects nothing.
        var guard = new LoopbackOriginGuard(environment.ResolvePort(app.ApplicationServices),
            app.ApplicationServices.GetRequiredService<RelayNavigationTokens>());

        app.Use(async (context, next) =>
        {
            var request = LoopbackGuardRequest.From(context.Request);
            var decision = guard.Evaluate(request);

            switch (decision.Verdict)
            {
                case LoopbackGuardVerdict.Allowed:
                    if (guard.IsOwnOrigin(request.Origin))
                    {
                        // The server's own UI, as far as the server is concerned: see
                        // UpstreamTransformer on what that changes on the way out.
                        UpstreamTransformer.MarkFromRelayPage(context);
                    }

                    await next();
                    return;

                case LoopbackGuardVerdict.DropTicket:
                    // The path only, never the query: that is where the ticket was.
                    logger.LogInformation("Took a switch ticket off {Method} {Path} (Sec-Fetch-Site: {FetchSite})",
                        request.Method, context.Request.Path, request.FetchSite);

                    await WriteContinuationAsync(context, decision.ContinueTo!);
                    return;

                default:
                    logger.LogWarning(
                        "Refused {Verdict} request {Method} {Path} (Host: {Host}, Origin: {Origin}, " +
                        "Sec-Fetch-Site: {FetchSite}, Sec-Fetch-Mode: {FetchMode}, Sec-Fetch-Dest: {FetchDest}, " +
                        "switch ticket: {CarriedTicket})",
                        decision.Verdict, request.Method, context.Request.Path, request.Host, request.Origin,
                        request.FetchSite, request.FetchMode, request.FetchDest, decision.CarriedTicket);

                    context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                    context.Response.Headers["X-Bakabase-Client"] = ClientForwardingFailure.ForeignCaller.ToString();
                    await context.Response.WriteAsync("This address only serves Bakabase's own window.");
                    return;
            }
        });

        // Ahead of routing, because these are the server's routes — the relay is
        // intercepting them, not defining its own.
        app.Use(async (context, next) =>
        {
            var dispatcher = context.RequestServices.GetRequiredService<UserMachineDispatcher>();

            if (!await dispatcher.TryHandleAsync(context))
            {
                await next();
            }
        });

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            // Answered here rather than upstream: the question is about the relay, and
            // only the relay knows the answer.
            endpoints.MapGet(ClientContextEndpoint.Path,
                (HttpContext context, ClientContextEndpoint endpoint) => endpoint.WriteAsync(context));

            // This program's own log and directories, which the server has no way to
            // answer for — only where they are this program's alone. See the parameter.
            if (mapThisMachinesDiagnostics)
            {
                ClientLogEndpoints.Map(endpoints);
                ClientAppEndpoints.Map(endpoints);
            }

            mapLocal(endpoints);

            // Everything else is the server's.
            //
            // The pattern is spelled out because MapFallback's default one is
            // "{*path:nonfile}", and nonfile excludes every path whose last segment
            // contains a dot. That would drop the entire frontend on the floor — the
            // server sends it as /assets/index-<hash>.js and friends — and leave the
            // window blank with a 404 per asset.
            endpoints.MapFallback("/{**path}", (HttpContext context, UpstreamForwarder forwarder) =>
                forwarder.ForwardAsync(context));
        });
    }

    /// <summary>
    /// Sends the window on to <paramref name="target"/> from a page on this origin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a 302, which is what this looks like it should be. The browser computes
    /// <c>Sec-Fetch-Site</c> over the whole redirect chain, relative to the page that
    /// started the navigation, so the request a redirect produces is exactly as cross-site
    /// as the one that carried the ticket — and no longer carries it. A navigation this
    /// page starts has this origin as its initiator, so it and everything the document it
    /// loads asks for arrive same-origin.
    /// </para>
    /// <para>
    /// <c>location.replace</c> so neither the ticket nor this page stays in the window's
    /// history, no referrer so the spent ticket is not handed to the server in a
    /// <c>Referer</c>, and an absolute address on the host the guard has just verified, so
    /// a target beginning with <c>//</c> cannot be read as another host.
    /// </para>
    /// <para>
    /// The fragment is carried over by the page itself. The browser never sends it, so the
    /// target above has none — and unlike a redirect, whose <c>Location</c> inherits the
    /// original fragment, a script navigation to an address without one drops it. The UI
    /// routes on the hash, so that would land every switch to <c>#/resource</c> or the
    /// devices page on the UI's root instead. Appending <c>location.hash</c> to an absolute
    /// address on this host can only add a fragment, and an empty one adds nothing. The
    /// <c>noscript</c> refresh cannot carry it; it is only the fallback for a window that
    /// runs no script at all.
    /// </para>
    /// </remarks>
    private static Task WriteContinuationAsync(HttpContext context, string target)
    {
        var url = $"{context.Request.Scheme}://{context.Request.Host.Value}{(target.StartsWith('/') ? "" : "/")}{target}";

        // JsonSerializer's default encoder escapes <, >, &, ' and ", so the string cannot
        // close the script element or the literal it sits in.
        var script = JsonSerializer.Serialize(url);
        var attribute = HtmlEncoder.Default.Encode(url);

        context.Response.StatusCode = (int) HttpStatusCode.OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.XContentTypeOptions = "nosniff";

        return context.Response.WriteAsync(
            "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"no-referrer\">" +
            "<title>Bakabase</title>" +
            $"<script>location.replace({script}+location.hash);</script>" +
            $"<noscript><meta http-equiv=\"refresh\" content=\"0;url={attribute}\"></noscript>" +
            "</head><body></body></html>");
    }

    /// <summary>
    /// The invoker YARP relays through. Everything that would normally be helpful is
    /// turned off: automatic decompression would break a byte-range video, cookies would
    /// mix the browser's with the relay's, and following redirects would hide the
    /// server's own answer.
    /// </summary>
    private static HttpMessageInvoker CreateUpstreamInvoker() =>
        new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            // Long-lived by design: this carries the hub connection and video streams.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ActivityHeadersPropagator = null
        });
}
