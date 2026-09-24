using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>
/// The first thing this device does with an address it is asked to manage.
/// </summary>
/// <remarks>
/// Also where the clock offset comes from. Every later request carries a timestamp the
/// server checks against a five-minute window, and a machine whose clock is wrong would
/// otherwise have everything refused as expired — a failure that looks exactly like a
/// broken pairing and sends the user to the wrong fix.
/// </remarks>
public sealed class ServerConnector(HttpClient http, ServerClock clock, ClientSelfAddress self)
{
    /// <summary>
    /// The oldest contract this client can talk to. Separate from
    /// <see cref="RemoteAccessProtocol.CurrentVersion"/> so an older server stays usable
    /// instead of being refused for not having caught up.
    /// </summary>
    public const int MinSupportedProtocolVersion = 1;

    public const int MaxSupportedProtocolVersion = RemoteAccessProtocol.CurrentVersion;

    /// <summary>
    /// Asks an address what it is, and decides whether this device can talk to it.
    /// Never throws for an unreachable or unrecognisable server — that is an answer,
    /// not an error.
    /// </summary>
    public async Task<ServerHandshakeResult> HandshakeAsync(string baseAddress, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(Normalize(baseAddress), UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.NotBakabase,
                $"'{baseAddress}' is not an http address.");
        }

        // Before the request rather than after it, because sending it is what makes this
        // unrecoverable: this address answers. A relay's forwarder relays the question
        // upstream and the real server's identity comes back — a handshake that succeeds
        // and pairs this device with itself.
        if (self.Matches(root))
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.SelfAddress,
                $"{root.Authority} is this computer's own address.");
        }

        // The clock's own source, not DateTime.UtcNow: the round trip has to be
        // measured against the same reading the offset is applied to.
        var sentAt = clock.LocalNow;
        HttpResponseMessage response;

        try
        {
            response = await http.GetAsync(new Uri(root, "/remote-access/server-info"), ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.Unreachable, e.Message);
        }

        // The gate answers 403 with a reason header before anything else runs, so a
        // server that is up but switched off is distinguishable from one that is down.
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized &&
            response.Headers.TryGetValues("X-Bakabase-Remote-Access", out var reasons))
        {
            var reason = reasons.FirstOrDefault();
            return ServerHandshakeResult.Failed(
                string.Equals(reason, nameof(RemoteAccessDenialReason.Disabled), StringComparison.Ordinal)
                    ? ServerHandshakeOutcome.RemoteAccessDisabled
                    : ServerHandshakeOutcome.NotBakabase,
                reason);
        }

        if (!response.IsSuccessStatusCode)
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.NotBakabase,
                $"HTTP {(int) response.StatusCode}");
        }

        ServerInfoPayload? payload;
        try
        {
            payload = (await JsonSerializer.DeserializeAsync<Envelope<ServerInfoPayload>>(
                await response.Content.ReadAsStreamAsync(ct), ServerJson.Options, ct))?.Data;
        }
        catch (JsonException e)
        {
            // Something is listening on that port; it just is not this.
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.NotBakabase, e.Message);
        }

        if (payload?.Id == null)
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.NotBakabase, "no server identity in the answer");
        }

        var server = new ServerInfo(payload.Id, payload.Name ?? payload.Id, payload.AppVersion ?? "unknown",
            payload.ProtocolVersion, payload.Mode, payload.PairingSupported, payload.ServerTime);

        if (server.ProtocolVersion > MaxSupportedProtocolVersion)
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.ClientTooOld,
                $"the server speaks protocol {server.ProtocolVersion}; this app knows up to " +
                $"{MaxSupportedProtocolVersion}", server);
        }

        if (server.ProtocolVersion < MinSupportedProtocolVersion)
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.ServerTooOld,
                $"the server speaks protocol {server.ProtocolVersion}; this app needs at least " +
                $"{MinSupportedProtocolVersion}", server);
        }

        // Only after the version check: an answer from something we cannot talk to is
        // not a clock worth trusting.
        if (server.ServerTime.HasValue)
        {
            clock.Synchronize(server.ServerTime.Value, sentAt);
        }

        return server.Mode == RemoteAccessMode.Disabled
            ? ServerHandshakeResult.Failed(ServerHandshakeOutcome.RemoteAccessDisabled, null, server)
            : ServerHandshakeResult.Ok(server);
    }

    /// <summary>
    /// Fills in what a user typing an address by hand leaves out, and drops the trailing
    /// slash so a stored address and a freshly typed one compare equal.
    /// </summary>
    public static string Normalize(string baseAddress)
    {
        var trimmed = (baseAddress ?? string.Empty).Trim().TrimEnd('/');

        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"http://{trimmed}";
    }

    private sealed record Envelope<T>(int Code, string? Message, T? Data);

    private sealed record ServerInfoPayload(
        string? Id,
        string? Name,
        string? AppVersion,
        int ProtocolVersion,
        RemoteAccessMode Mode,
        bool PairingSupported,
        DateTime? ServerTime);
}
