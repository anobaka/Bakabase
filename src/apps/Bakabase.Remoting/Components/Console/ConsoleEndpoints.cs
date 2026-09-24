using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.UserMachine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// A managed server's relay answers <c>/client/*</c> itself: what the server's own UI,
/// shown in this device's window, is told about the window it is in.
/// </summary>
/// <remarks>
/// <para>
/// The API grew out of the removed thin client's, and keeps its shape wherever the two mean
/// the same thing — status, path mappings, the tray report — because the page asking may be
/// a managed server's older UI, written when that was the only program answering here.
/// <c>host: "console"</c> is how a newer UI knows it is in the desktop app and offers the
/// server switcher.
/// </para>
/// <para>
/// The thin client's connect and pairing routes answer 409 <c>ManagedByHost</c> here, for
/// such an older UI: pairing, switching the active server and forgetting one are the
/// desktop app's own business, done from its own UI, and a server's page — which is the
/// other machine's code — has no say in them. Everything else it had (updater, migration
/// export, its log and directories) answers 404: this device's log and data directory are
/// the whole app's, and nothing about this device beyond what is listed here is the
/// server's page's to read or open.
/// </para>
/// <para>
/// Nothing under <c>/client</c> is forwarded, including paths nobody mapped: the server
/// does not own that prefix, and a request there reaching it would be a request the
/// server's code could use to probe this relay's routing.
/// </para>
/// </remarks>
public static class ConsoleEndpoints
{
    public const string Prefix = RelayPaths.Prefix;

    /// <summary>What <c>/client/status</c> says this host is.</summary>
    public const string HostName = "console";

    /// <summary>The refusal a route that only the removed thin client served answers with.</summary>
    public const string ManagedByHost = "ManagedByHost";

    /// <summary>
    /// The removed thin client's connect and pairing routes, which a managed server's older UI
    /// may still call and which mean nothing when the desktop app is the host.
    /// </summary>
    public static IReadOnlyList<(string Method, string Pattern)> ManagedByHostRoutes { get; } =
    [
        (HttpMethods.Post, $"{Prefix}/connect"),
        (HttpMethods.Post, $"{Prefix}/pair/code"),
        (HttpMethods.Post, $"{Prefix}/pair/request"),
        (HttpMethods.Post, $"{Prefix}/pair/claim"),
        (HttpMethods.Post, $"{Prefix}/servers/{{serverId}}/activate"),
        (HttpMethods.Delete, $"{Prefix}/servers/{{serverId}}"),
        (HttpMethods.Get, $"{Prefix}/discover")
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{Prefix}/status",
            async (HttpContext context, ConsoleRelayContext relay, IClientConnectionStore store,
                IUpstreamContextProbe probe, UserMachineDispatcher dispatcher) =>
            {
                var data = store.Read();
                var upstream = await probe.ReadAsync(context.RequestAborted);

                await WriteAsync(context, new
                {
                    clientVersion = relay.AppVersion,
                    deviceName = data.DeviceName ?? relay.Navigator.LocalName,
                    platform = data.Platform == RemoteDevicePlatform.Unknown
                        ? ClientPairingService.CurrentPlatform()
                        : data.Platform,
                    activeServerId = data.ActiveServerId,
                    serverReachable = upstream != null,
                    implementedUserMachineRoutes = dispatcher.ImplementedRoutes.OrderBy(r => r).ToArray(),
                    // This relay's server and no other: the view it reads holds only that one.
                    servers = data.Servers.Select(s => new
                    {
                        s.ServerId,
                        s.ServerName,
                        s.BaseAddress,
                        s.PairedAt,
                        s.LastConnectedAt,
                        // Never the key.
                        s.DeviceId,
                        isActive = true,
                        pathMappings = s.PathMappings
                    }),
                    host = HostName,
                    localName = relay.Navigator.LocalName
                });
            });

        // Every managed server with how it was last seen, as a ManagedServerState number, so
        // the switcher can mark each one without asking anybody: the listing is read from
        // memory, and never waits on the network or the disk. This relay's own server is
        // WrongServer while its address answers as someone else, and otherwise whatever the
        // latest request forwarded to it said — while its page is up that page keeps asking,
        // so a server that has answered through here reads Online — and until something has,
        // what the console last knew. This device carries no state: it is the app itself,
        // not a server it reaches.
        endpoints.MapGet($"{Prefix}/switcher",
            async (HttpContext context, ConsoleRelayContext relay, UpstreamStanding standing,
                UpstreamIdentity identity) =>
            {
                var targets = relay.Navigator.ListTargets();

                await WriteAsync(context, new
                {
                    currentId = relay.ServerId,
                    targets = targets.Select(t =>
                    {
                        var isCurrent = !t.IsLocal && string.Equals(t.Id, relay.ServerId, StringComparison.Ordinal);

                        return new
                        {
                            t.Id,
                            t.Name,
                            t.IsLocal,
                            isCurrent,
                            state = t.IsLocal ? null : (int?) StateOf(t.Id, isCurrent, relay, standing, identity)
                        };
                    })
                });
            });

        endpoints.MapPost($"{Prefix}/switcher/{{id}}/open",
            async (HttpContext context, string id, ConsoleRelayContext relay) =>
            {
                var input = await ReadAsync<OpenInput>(context);
                string? url;

                try
                {
                    url = await relay.Navigator.ResolveUrlAsync(id, input?.Path, context.RequestAborted);
                }
                catch (IOException)
                {
                    // No loopback port for the other server's relay: worth trying again, and
                    // said the way this API says everything else.
                    await RefuseAsync(context, HttpStatusCode.ServiceUnavailable, "RelayUnavailable");
                    return;
                }

                if (url == null)
                {
                    await RefuseAsync(context, HttpStatusCode.NotFound, "UnknownServer");
                    return;
                }

                await WriteAsync(context, new {url});
            });

        endpoints.MapPut($"{Prefix}/servers/{{serverId}}/path-mappings",
            async (HttpContext context, string serverId, ConsoleRelayContext relay, ActiveConnection connection) =>
            {
                // Only this relay's own server. The view behind the connection could not
                // write another server's mappings anyway; this says so instead of
                // answering "unchanged".
                if (!string.Equals(serverId, relay.ServerId, StringComparison.Ordinal))
                {
                    await RefuseAsync(context, HttpStatusCode.NotFound, "UnknownServer");
                    return;
                }

                var input = await ReadAsync<PathMappingsInput>(context);

                await WriteAsync(context, new
                {
                    changed = await connection.SetPathMappingsAsync(serverId, input?.Mappings ?? [],
                        context.RequestAborted)
                });
            });

        foreach (var (method, pattern) in ManagedByHostRoutes)
        {
            endpoints.MapMethods(pattern, [method],
                (HttpContext context) => RefuseAsync(context, HttpStatusCode.Conflict, ManagedByHost));
        }

        // The window's tray icon is the desktop app's own, driven by its own tasks; a
        // server's page has nothing to report to it.
        endpoints.MapPost($"{Prefix}/tray", (HttpContext context) => WriteAsync(context, new {applied = false}));

        endpoints.MapGet(RelayPaths.ConnectPath, (HttpContext context, ConsoleRelayContext relay) =>
            ConsoleUnavailablePage.WriteAsync(context, relay.Navigator.LocalOrigin));

        // Everything else under the prefix is not here, and is never the server's either —
        // /client/log* and /client/app/* included: in this app this device's log and
        // directories are the whole app's, not a relay's.
        endpoints.Map($"{Prefix}/{{**rest}}",
            (HttpContext context) => RefuseAsync(context, HttpStatusCode.NotFound, "NotFound"));
    }

    private static ManagedServerState StateOf(string serverId, bool isCurrent, ConsoleRelayContext relay,
        UpstreamStanding standing, UpstreamIdentity identity)
    {
        if (isCurrent)
        {
            // Ahead of the latest answer: once the address answers as someone else nothing
            // is forwarded, so an Online from before the change would never be corrected.
            if (identity.Latest is {IsMismatch: true})
            {
                return ManagedServerState.WrongServer;
            }

            if (standing.Latest is var seenHere and not ManagedServerState.Unknown)
            {
                return seenHere;
            }
        }

        return relay.Navigator.LastKnownState(serverId);
    }

    private static async Task RefuseAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int) status;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new {code = (int) status, message}, Json),
            context.RequestAborted);
    }

    private static async Task WriteAsync(HttpContext context, object payload)
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new {code = 0, data = payload}, Json),
            context.RequestAborted);
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        if (context.Request.ContentLength == 0)
        {
            return null;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OpenInput(string? Path);

    private sealed record PathMappingsInput(List<ClientPathMapping>? Mappings);
}
