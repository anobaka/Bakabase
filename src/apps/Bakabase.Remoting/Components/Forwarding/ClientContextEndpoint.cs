using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Components.Connection;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>What the server said about itself, or null when it is not answering.</summary>
public sealed record UpstreamContext(RemoteAccessMode Mode, bool Paired);

/// <summary>Reads the server's own answer to <c>/remote-access/context</c>.</summary>
public interface IUpstreamContextProbe
{
    Task<UpstreamContext?> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Answers <c>/remote-access/context</c> locally instead of forwarding it.
/// </summary>
/// <remarks>
/// <para>
/// The server's answer is about the server; this question is about the client. Only the
/// forwarding layer knows it is a forwarding layer, that it can run a player on this
/// machine, and which server it is pointed at — so it answers, folding in what the
/// server said about itself.
/// </para>
/// <para>
/// <c>isLocal</c> stays false, deliberately. It means "the caller is on the machine
/// running the server", and here it is not. The frontend keys real behaviour off it:
/// covers would start being fetched from the server's own listening ports rather than
/// through this origin, and on a remote server those addresses mean nothing here. What
/// the UI actually needs is <c>clientMode</c>, which says where user-side actions run
/// without claiming anything about where the files are.
/// </para>
/// <para>
/// A server that is not answering does not make this fail. The UI has to render
/// something during a reconnect, and a context call that errors would leave it with
/// nothing to render — so the answer says the server is unreachable and the client
/// fields stay true regardless.
/// </para>
/// </remarks>
public sealed class ClientContextEndpoint(
    ActiveConnection connection,
    IUpstreamContextProbe probe,
    string clientVersion)
{
    public const string Path = "/remote-access/context";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task WriteAsync(HttpContext context)
    {
        var server = connection.Server;
        var upstream = await probe.ReadAsync(context.RequestAborted);

        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = 0,
            data = new ClientContextPayload
            {
                // Not the machine running the server, and saying otherwise would send
                // the UI looking for files that are not here.
                IsLocal = false,
                // Nothing is known about the server's mode while it is unreachable, and
                // Disabled is the reading that cannot mislead anyone into trying.
                Mode = upstream?.Mode ?? RemoteAccessMode.Disabled,
                Paired = upstream?.Paired ?? false,
                ServerReachable = upstream != null,
                ClientMode = ClientMode.PureClient,
                ClientVersion = clientVersion,
                ServerId = server?.ServerId,
                ServerName = server?.ServerName,
                ServerAddress = server?.BaseAddress,
                // The embedded browser is in this process, so a login capture runs here
                // with this machine's user agent and this user's cookies.
                CookieCaptureAvailable = true
            }
        }, Json), context.RequestAborted);
    }

    private sealed record ClientContextPayload
    {
        public bool IsLocal { get; init; }
        public RemoteAccessMode Mode { get; init; }
        public bool Paired { get; init; }
        public bool ServerReachable { get; init; }
        public ClientMode ClientMode { get; init; }
        public string? ClientVersion { get; init; }
        public string? ServerId { get; init; }
        public string? ServerName { get; init; }
        public string? ServerAddress { get; init; }
        public bool CookieCaptureAvailable { get; init; }
    }
}
