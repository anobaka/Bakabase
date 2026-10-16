using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>Why the client could not get credentials, in terms a user can act on.</summary>
public enum ClientPairingOutcome
{
    Paired = 0,

    /// <summary>The server did not answer.</summary>
    Unreachable = 1,

    /// <summary>Wrong code, expired, or burned by earlier wrong guesses.</summary>
    CodeRejected = 2,

    /// <summary>Nobody has approved this device yet. Normal while waiting.</summary>
    AwaitingApproval = 3,

    /// <summary>The request was rejected, or waited too long and lapsed.</summary>
    RequestRejected = 4,

    /// <summary>The server refused to take any more requests from here for now.</summary>
    TooManyAttempts = 5,

    /// <summary>The server is too old to pair with at all.</summary>
    PairingUnsupported = 6
}

public sealed record ClientPairingResult(ClientPairingOutcome Outcome, ClientCredentials? Credentials, string? Detail)
{
    public bool Succeeded => Outcome == ClientPairingOutcome.Paired;

    public static ClientPairingResult Ok(ClientCredentials credentials) =>
        new(ClientPairingOutcome.Paired, credentials, null);

    public static ClientPairingResult Failed(ClientPairingOutcome outcome, string? detail = null) =>
        new(outcome, null, detail);
}

/// <summary>A filed request, waiting for somebody on the other side to approve it.</summary>
public sealed record ClientPairingTicket(string RequestId, DateTime ExpiresAt);

public sealed record ClientPairingRequestResult(ClientPairingOutcome Outcome, ClientPairingTicket? Ticket,
    string? Detail)
{
    public bool Succeeded => Ticket != null;
}

/// <summary>
/// The client's half of pairing: two ways in, matching the two the server offers.
/// </summary>
/// <remarks>
/// A code is the direct route, for whoever can read the server's screen or logs. The
/// request-and-claim route exists for when nobody can — the device files a request, an
/// already-paired device approves it, and the waiting device collects credentials with
/// the only thing it has, the request id. Waiting is the normal state there, so
/// <see cref="ClientPairingOutcome.AwaitingApproval"/> is an answer rather than an error.
/// </remarks>
public sealed class ClientPairingService(HttpClient http, IClientConnectionStore store)
{
    public async Task<ClientPairingResult> PairWithCodeAsync(string baseAddress, string code,
        CancellationToken ct = default)
    {
        var identity = Identity();

        return await PostForCredentialsAsync(baseAddress, "/remote-access/pair/code",
            new {code, deviceName = identity.Name, platform = identity.Platform.ToString()}, ct);
    }

    public async Task<ClientPairingRequestResult> RequestPairingAsync(string baseAddress,
        CancellationToken ct = default)
    {
        var identity = Identity();

        var response = await SendAsync(baseAddress, "/remote-access/pair/request",
            new {deviceName = identity.Name, platform = identity.Platform.ToString()}, ct);

        if (response == null)
        {
            return new ClientPairingRequestResult(ClientPairingOutcome.Unreachable, null, null);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ClientPairingRequestResult(ClientPairingOutcome.PairingUnsupported, null, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new ClientPairingRequestResult(ClientPairingOutcome.Unreachable, null,
                $"HTTP {(int) response.StatusCode}");
        }

        // RequestId is null when the server declined to file one — it says why through
        // Failure, the same channel the other two endpoints use.
        var payload = await ReadAsync<TicketPayload>(response, ct);

        // AwaitingApproval, not Paired: filing a request is the start of waiting, and
        // nothing has been paired. Reporting it as Paired would tell the caller the device
        // was in — so it would stop polling for the approval, which would then go
        // uncollected however long the user waited at the server.
        return payload?.RequestId == null
            ? new ClientPairingRequestResult(Map(payload?.Failure), null, null)
            : new ClientPairingRequestResult(ClientPairingOutcome.AwaitingApproval,
                new ClientPairingTicket(payload.RequestId, payload.ExpiresAt), null);
    }

    public async Task<ClientPairingResult> ClaimAsync(string baseAddress, string requestId,
        CancellationToken ct = default) =>
        await PostForCredentialsAsync(baseAddress, "/remote-access/pair/claim", new {requestId}, ct);

    private async Task<ClientPairingResult> PostForCredentialsAsync(string baseAddress, string path, object body,
        CancellationToken ct)
    {
        var response = await SendAsync(baseAddress, path, body, ct);

        if (response == null)
        {
            return ClientPairingResult.Failed(ClientPairingOutcome.Unreachable);
        }

        // A server that predates pairing has no such route at all, which is worth saying
        // plainly rather than reporting as a rejected code.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ClientPairingResult.Failed(ClientPairingOutcome.PairingUnsupported);
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            return ClientPairingResult.Failed(ClientPairingOutcome.TooManyAttempts);
        }

        if (!response.IsSuccessStatusCode)
        {
            return ClientPairingResult.Failed(ClientPairingOutcome.Unreachable, $"HTTP {(int) response.StatusCode}");
        }

        var payload = await ReadAsync<PairingResultPayload>(response, ct);

        if (payload?.Credentials is {DeviceId: not null, Key: not null} credentials)
        {
            return ClientPairingResult.Ok(new ClientCredentials(credentials.DeviceId, credentials.Key));
        }

        return ClientPairingResult.Failed(Map(payload?.Failure));
    }

    /// <summary>
    /// The server reports every reason it withheld credentials through one enum, so this
    /// is the single place the client turns that into something a user can act on.
    /// </summary>
    private static ClientPairingOutcome Map(PairingFailure? failure) => failure switch
    {
        PairingFailure.CodeRejected => ClientPairingOutcome.CodeRejected,
        PairingFailure.NotYetApproved => ClientPairingOutcome.AwaitingApproval,
        PairingFailure.RequestRejected => ClientPairingOutcome.RequestRejected,
        PairingFailure.TooManyAttempts => ClientPairingOutcome.TooManyAttempts,
        // A success envelope with neither credentials nor a stated reason is a server
        // this client does not understand; saying so beats inventing a cause.
        _ => ClientPairingOutcome.PairingUnsupported
    };

    private async Task<HttpResponseMessage?> SendAsync(string baseAddress, string path, object body,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(ServerConnector.Normalize(baseAddress), UriKind.Absolute, out var root))
        {
            return null;
        }

        try
        {
            return await http.PostAsJsonAsync(new Uri(root, path), body, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try
        {
            return (await JsonSerializer.DeserializeAsync<Envelope<T>>(
                await response.Content.ReadAsStreamAsync(ct), ServerJson.Options, ct))?.Data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// How this device introduces itself. Stored, so a name the user corrected on one
    /// server is not thrown away the next time they pair with another.
    /// </summary>
    private (string Name, RemoteDevicePlatform Platform) Identity()
    {
        var data = store.Read();

        return (
            string.IsNullOrWhiteSpace(data.DeviceName) ? Environment.MachineName : data.DeviceName,
            data.Platform == RemoteDevicePlatform.Unknown ? CurrentPlatform() : data.Platform);
    }

    public static RemoteDevicePlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return RemoteDevicePlatform.Windows;
        if (OperatingSystem.IsMacOS()) return RemoteDevicePlatform.MacOS;
        if (OperatingSystem.IsLinux()) return RemoteDevicePlatform.Linux;
        if (OperatingSystem.IsAndroid()) return RemoteDevicePlatform.Android;
        if (OperatingSystem.IsIOS()) return RemoteDevicePlatform.IOS;
        return RemoteDevicePlatform.Unknown;
    }

    private sealed record Envelope<T>(int Code, string? Message, T? Data);

    private sealed record TicketPayload(string? RequestId, DateTime ExpiresAt, PairingFailure Failure);

    private sealed record PairingResultPayload(CredentialsPayload? Credentials, PairingFailure Failure);

    private sealed record CredentialsPayload(string? DeviceId, string? Key, string? ServerId);
}
