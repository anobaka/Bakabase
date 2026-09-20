using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Client.Remoting.Abstractions.Models;
using Bakabase.Client.Remoting.Components.Connection;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Client.Remoting.Components.UserMachine;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bakabase.Client.Remoting.Components.Forwarding;

/// <summary>
/// The client's own API, under <c>/client/</c>.
/// </summary>
/// <remarks>
/// <para>
/// A prefix the server does not use, so nothing here can shadow one of its routes or be
/// shadowed by one later. Everything under it is answered locally and never forwarded:
/// these are questions about this machine — which server it points at, where that
/// server's libraries are here — that the server has no way to answer.
/// </para>
/// <para>
/// The loopback guard already stands in front of them, which is what makes it safe for
/// these to have no authentication of their own: they are reachable only from this
/// client's own window.
/// </para>
/// </remarks>
public static class ClientApiEndpoints
{
    public const string Prefix = "/client";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Where a client with no server sends the window.
    /// </summary>
    /// <remarks>
    /// A path of its own rather than <c>/</c>, so the root always means one thing: hand
    /// this request to the server. There is simply nothing to hand it to yet, and the
    /// forwarder redirects here instead of answering a browser with a JSON refusal.
    /// </remarks>
    public const string ConnectPath = Prefix + "/connect-page";

    public static void Map(IEndpointRouteBuilder endpoints, string clientVersion)
    {
        endpoints.MapGet($"{Prefix}/migration-hints", async (HttpContext context, IClientConnectionStore store) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await WriteAsync(context, new
            {
                format = "bakabase-client-connection-hints",
                version = 1,
                servers = store.Read().Servers.Select(server => new
                {
                    name = server.ServerName,
                    address = MigrationAddressHint(server.BaseAddress),
                    pathMappings = server.PathMappings.Select(mapping => new
                        { serverPath = mapping.ServerPath, localPath = mapping.LocalPath }).ToArray()
                }).Where(server => server.address != null).ToArray()
            });
        });

        // The one page this client serves itself. It is not the web frontend — that
        // still comes from the server, as it must — but the client cannot ask for a
        // server address without somewhere to ask it.
        endpoints.MapGet(ConnectPath, async (HttpContext context) =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";

            // Never cached: it hands over to the real frontend the moment a server is
            // attached, and a cached copy would keep showing a setup screen for a
            // client that is already set up.
            context.Response.Headers.CacheControl = "no-store";

            await context.Response.WriteAsync(ConnectPage.Html, context.RequestAborted);
        });

        endpoints.MapGet($"{Prefix}/discover",
            async (HttpContext context, IServerDiscovery discovery) =>
            {
                // Bounded rather than open-ended: this runs while someone watches a
                // spinner, and a server that has not answered in three seconds is not
                // going to.
                var servers = await discovery.DiscoverAsync(TimeSpan.FromSeconds(3), context.RequestAborted);

                await WriteAsync(context, servers);
            });

        endpoints.MapGet($"{Prefix}/status",
            async (HttpContext context, IClientConnectionStore store, ActiveConnection connection,
                IUpstreamContextProbe probe, UserMachineDispatcher dispatcher) =>
            {
                var data = store.Read();
                var upstream = await probe.ReadAsync(context.RequestAborted);

                await WriteAsync(context, new
                {
                    clientVersion,
                    deviceName = data.DeviceName ?? Environment.MachineName,
                    platform = data.Platform == RemoteDevicePlatform.Unknown
                        ? ClientPairingService.CurrentPlatform()
                        : data.Platform,
                    activeServerId = data.ActiveServerId,
                    serverReachable = upstream != null,
                    // What this build can run here. The settings page uses it to say
                    // which actions still need a newer client rather than letting the
                    // user discover it by clicking.
                    implementedUserMachineRoutes = dispatcher.ImplementedRoutes.OrderBy(r => r).ToArray(),
                    servers = data.Servers.Select(s => new
                    {
                        s.ServerId,
                        s.ServerName,
                        s.BaseAddress,
                        s.PairedAt,
                        s.LastConnectedAt,
                        // Never the key. It exists in this process to sign with, and
                        // there is no reason for a page to hold it.
                        s.DeviceId,
                        isActive = s.ServerId == data.ActiveServerId,
                        pathMappings = s.PathMappings
                    })
                });
            });

        endpoints.MapPost($"{Prefix}/connect",
            async (HttpContext context, IServerConnector connector) =>
            {
                var input = await ReadAsync<AddressInput>(context);
                var result = await connector.HandshakeAsync(input?.Address ?? string.Empty, context.RequestAborted);

                await WriteAsync(context, new
                {
                    outcome = result.Outcome,
                    detail = result.Detail,
                    server = result.Server == null
                        ? null
                        : new
                        {
                            result.Server.Id,
                            result.Server.Name,
                            result.Server.AppVersion,
                            result.Server.Mode,
                            result.Server.PairingSupported
                        }
                });
            });

        endpoints.MapPost($"{Prefix}/pair/code",
            async (HttpContext context, IServerConnector connector, IClientPairingService pairing,
                ActiveConnection connection) =>
            {
                var input = await ReadAsync<PairWithCodeInput>(context);
                var address = input?.Address ?? string.Empty;

                var handshake = await connector.HandshakeAsync(address, context.RequestAborted);

                if (!handshake.Succeeded)
                {
                    await WriteAsync(context, new {outcome = ClientPairingOutcome.Unreachable, handshake.Detail});
                    return;
                }

                var result = await pairing.PairWithCodeAsync(address, input?.Code ?? string.Empty,
                    context.RequestAborted);

                await SaveIfPairedAsync(context, connection, handshake, result, address);
            });

        endpoints.MapPost($"{Prefix}/pair/request",
            async (HttpContext context, IClientPairingService pairing) =>
            {
                var input = await ReadAsync<AddressInput>(context);
                var result = await pairing.RequestPairingAsync(input?.Address ?? string.Empty,
                    context.RequestAborted);

                await WriteAsync(context, new
                {
                    outcome = result.Outcome,
                    requestId = result.Ticket?.RequestId,
                    expiresAt = result.Ticket?.ExpiresAt
                });
            });

        endpoints.MapPost($"{Prefix}/pair/claim",
            async (HttpContext context, IServerConnector connector, IClientPairingService pairing,
                ActiveConnection connection) =>
            {
                var input = await ReadAsync<ClaimInput>(context);
                var address = input?.Address ?? string.Empty;

                var handshake = await connector.HandshakeAsync(address, context.RequestAborted);

                if (!handshake.Succeeded)
                {
                    await WriteAsync(context, new {outcome = ClientPairingOutcome.Unreachable, handshake.Detail});
                    return;
                }

                var result = await pairing.ClaimAsync(address, input?.RequestId ?? string.Empty,
                    context.RequestAborted);

                await SaveIfPairedAsync(context, connection, handshake, result, address);
            });

        endpoints.MapPost($"{Prefix}/servers/{{serverId}}/activate",
            async (HttpContext context, string serverId, ActiveConnection connection) =>
                await WriteAsync(context,
                    new {changed = await connection.ActivateAsync(serverId, context.RequestAborted)}));

        endpoints.MapDelete($"{Prefix}/servers/{{serverId}}",
            async (HttpContext context, string serverId, ActiveConnection connection) =>
                await WriteAsync(context,
                    new {changed = await connection.ForgetAsync(serverId, context.RequestAborted)}));

        endpoints.MapPut($"{Prefix}/servers/{{serverId}}/path-mappings",
            async (HttpContext context, string serverId, ActiveConnection connection) =>
            {
                var input = await ReadAsync<PathMappingsInput>(context);

                await WriteAsync(context, new
                {
                    changed = await connection.SetPathMappingsAsync(serverId, input?.Mappings ?? [],
                        context.RequestAborted)
                });
            });
    }

    private static string? MigrationAddressHint(string? address)
    {
        if (!Uri.TryCreate(ServerConnector.Normalize(address ?? string.Empty), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https")) return null;
        // Legacy addresses were not restricted to origins. Build from safe URI
        // components so userinfo, token-bearing paths, query and fragment cannot
        // become part of a portable migration file. The saved connection is untouched.
        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port)
            .Uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Records credentials only when they were actually issued, and only against the
    /// identity the handshake reported — an address can be reused by a different install,
    /// and storing a key under the wrong server id would produce refusals nobody could
    /// explain.
    /// </summary>
    private static async Task SaveIfPairedAsync(HttpContext context, ActiveConnection connection,
        ServerHandshakeResult handshake, ClientPairingResult result, string address)
    {
        if (result.Succeeded)
        {
            await connection.SaveAsync(handshake.Server!.Id, handshake.Server.Name, address, result.Credentials!,
                DateTime.UtcNow, context.RequestAborted);
        }

        await WriteAsync(context, new
        {
            outcome = result.Outcome,
            serverId = result.Succeeded ? handshake.Server!.Id : null,
            serverName = result.Succeeded ? handshake.Server!.Name : null,
            result.Detail
        });
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(HttpContext context, object payload)
    {
        context.Response.ContentType = "application/json";

        // The same envelope everything else in this UI reads, so one client of the API
        // does not need a second shape for these.
        await context.Response.WriteAsync(JsonSerializer.Serialize(new {code = 0, data = payload}, Json),
            context.RequestAborted);
    }

    private sealed record AddressInput(string? Address);

    private sealed record PairWithCodeInput(string? Address, string? Code);

    private sealed record ClaimInput(string? Address, string? RequestId);

    private sealed record PathMappingsInput(List<ClientPathMapping>? Mappings);
}
