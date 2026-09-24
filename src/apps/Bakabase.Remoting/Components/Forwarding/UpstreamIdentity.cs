using System.Net;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>Who answered at a managed server's address when the relay last asked.</summary>
public enum UpstreamIdentityVerdict
{
    /// <summary>The server this relay is for.</summary>
    Confirmed = 1,

    /// <summary>Another Bakabase server: another install, on this machine or another one.</summary>
    WrongServer = 2,

    /// <summary>This device itself — its own server or one of its relays.</summary>
    ThisDevice = 3,

    /// <summary>
    /// Nobody could be identified: nothing answered, what answered is not Bakabase, or it
    /// refused to say who it is.
    /// </summary>
    Unconfirmed = 4
}

/// <summary>What asking an address "who are you" found, for one managed server.</summary>
/// <param name="ServerId">The server the relay is for, the one the question was about.</param>
/// <param name="Address">The address asked, as stored for that server.</param>
/// <param name="AnsweredById">The identity that answered, when it gave one.</param>
/// <param name="AnsweredByName">What the server that answered calls itself, when it said.</param>
/// <param name="Detail">For <see cref="UpstreamIdentityVerdict.Unconfirmed"/>, why nobody could be identified.</param>
/// <param name="CheckedAt">When the question was asked, not when it was answered.</param>
public sealed record UpstreamIdentityCheck(
    string ServerId,
    string Address,
    UpstreamIdentityVerdict Verdict,
    string? AnsweredById,
    string? AnsweredByName,
    string? Detail,
    DateTimeOffset CheckedAt)
{
    /// <summary>
    /// For <see cref="UpstreamIdentityVerdict.Unconfirmed"/>: what answered is a Bakabase server
    /// with remote access turned off, whose gate refuses to say who it is. Nothing to look for
    /// — the fix is to turn remote access on there, so the user is told that instead.
    /// </summary>
    public bool RemoteAccessDisabled { get; init; }

    public bool IsConfirmed => Verdict == UpstreamIdentityVerdict.Confirmed;

    /// <summary>Somebody answered, and it is not the server this relay is for.</summary>
    public bool IsMismatch => Verdict is UpstreamIdentityVerdict.WrongServer or UpstreamIdentityVerdict.ThisDevice;

    /// <summary>The address as a person would read it: host and port.</summary>
    public string Authority =>
        Uri.TryCreate(Address, UriKind.Absolute, out var uri) ? uri.Authority : Address;

    /// <summary>Whether <paramref name="host"/>:<paramref name="port"/> is the address this answer is about.</summary>
    public bool IsFor(string host, int port) =>
        Uri.TryCreate(Address, UriKind.Absolute, out var uri) && uri.Port == port &&
        string.Equals(uri.IdnHost.Trim('[', ']'), host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A sentence for the user saying why nothing was forwarded, naming the server the window
    /// shows as <paramref name="serverName"/>.
    /// </summary>
    public string Describe(string? serverName)
    {
        var expected = string.IsNullOrWhiteSpace(serverName) ? "the server this window shows" : serverName;

        return Verdict switch
        {
            UpstreamIdentityVerdict.WrongServer =>
                $"{Authority} now answers as another server ({AnsweredByName ?? AnsweredById ?? "unnamed"}), " +
                $"not {expected}. Nothing was sent to it. If {expected} moved to another address, find it " +
                "again on this computer's Devices and sharing page.",
            UpstreamIdentityVerdict.ThisDevice =>
                $"{Authority} now reaches this computer itself, not {expected}. Nothing was sent to it. If " +
                $"{expected} moved to another address, find it again on this computer's Devices and sharing page.",
            UpstreamIdentityVerdict.Confirmed => $"{Authority} answers as {expected}.",
            // Worded as the relay's page words it (ConsoleUnavailablePage).
            _ when RemoteAccessDisabled =>
                $"Remote access is turned off at {Authority}, so it cannot confirm that it is {expected}, and " +
                "nothing was sent to it. Turn it on in Bakabase on that device, under “Let other devices manage " +
                "this device”, then try again.",
            _ =>
                $"{expected} is not answering at {Authority}{(Detail == null ? "" : $" ({Detail})")}. Check " +
                "that it is running and reachable."
        };
    }
}

/// <summary>Asks an address which server answers there.</summary>
public interface IUpstreamIdentityVerifier
{
    /// <summary>
    /// Who answers at <paramref name="address"/>, judged against <paramref name="serverId"/>.
    /// Never throws for an address that does not answer — that is a verdict too; throws
    /// only when <paramref name="ct"/> is cancelled.
    /// </summary>
    Task<UpstreamIdentityCheck> VerifyAsync(string serverId, string address, CancellationToken ct);
}

/// <summary>How often the relay asks who answers, and how long it waits.</summary>
/// <param name="Lifetime">
/// How long a confirmation stands for a request on a connection already open. Past half of
/// it, a request that uses it also starts the next check without waiting for it; past all of
/// it, requests wait for that check.
/// </param>
/// <param name="RetryInterval">How long a failed or mismatched answer stands before it is asked again.</param>
/// <param name="Timeout">How long one check may take before it counts as nobody answering.</param>
/// <param name="ConnectionWindow">
/// How recent a confirmation has to be for the relay to open a new connection to the server
/// on it. A new connection is the one moment the peer can change, so it gets a far shorter
/// window than a request on a connection that is already open.
/// </param>
public sealed record UpstreamIdentityPolicy(
    TimeSpan Lifetime,
    TimeSpan RetryInterval,
    TimeSpan Timeout,
    TimeSpan ConnectionWindow)
{
    public static UpstreamIdentityPolicy Default { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
}

/// <summary>
/// Whether the address this relay forwards to still answers as the server it was paired
/// with. Nothing is forwarded or signed for that server without a current confirmation.
/// </summary>
/// <remarks>
/// <para>
/// An address is not an identity. A desktop app picks its ports at every launch, so after a
/// restart the port a managed server had can belong to another install on the same machine —
/// or to this device itself — and on a LAN a DHCP lease can hand a NAS's address to another
/// NAS. The server that answers then may never look at the device key: a server takes any
/// caller on its own machine as local, and an older or unrestricted one may let anyone in.
/// So the key being accepted says nothing about who accepted it, and without this check the
/// window would show — and write to — whichever library happens to live at the address.
/// </para>
/// <para>
/// The question is the same handshake pairing asks (<c>/remote-access/server-info</c>), which
/// carries the server's install identity. It is not a proof: remote access has no server-side
/// signature, so a server lying about its identity would pass. It catches the address moving
/// to someone else, which is what actually happens.
/// </para>
/// <para>
/// Asked at two levels, both kept off the hot path. Every request reads the last answer from
/// memory and waits only when it is missing, older than <see cref="UpstreamIdentityPolicy.Lifetime"/>,
/// about a different address, or under suspicion; a request that finds it past half its
/// lifetime starts the next question without waiting, so a page in use is re-checked in the
/// background. And every new TCP connection to the server — the only moment the process at
/// the other end can be a different one — needs an answer no older than
/// <see cref="UpstreamIdentityPolicy.ConnectionWindow"/>: a server that restarts drops every
/// connection to it, so whoever holds its port afterwards is asked before a single request
/// reaches it. Connections are pooled, so that costs one question per burst of new
/// connections, not one per request. All waiting callers share one question.
/// </para>
/// <para>
/// Suspicion is raised by <see cref="Suspect"/>: the server stopped answering mid-exchange,
/// which is exactly when its address may be changing hands.
/// </para>
/// </remarks>
public sealed class UpstreamIdentity : IDisposable
{
    private readonly ActiveConnection _connection;
    private readonly IUpstreamIdentityVerifier _verifier;
    private readonly UpstreamIdentityPolicy _policy;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _gate = new();

    private UpstreamIdentityCheck? _latest;
    private Task<UpstreamIdentityCheck>? _pending;
    private (string ServerId, string Address, DateTimeOffset StartedAt)? _pendingFor;
    private DateTimeOffset? _suspectedAt;

    public UpstreamIdentity(ActiveConnection connection, IUpstreamIdentityVerifier verifier,
        UpstreamIdentityPolicy? policy = null, TimeProvider? time = null, ILogger<UpstreamIdentity>? logger = null)
    {
        _connection = connection;
        _verifier = verifier;
        _policy = policy ?? UpstreamIdentityPolicy.Default;
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?) logger ?? NullLogger.Instance;
    }

    public UpstreamIdentityPolicy Policy => _policy;

    /// <summary>
    /// The last answer about the server and address this relay is for now, however old; null
    /// when nothing has been asked since either changed. Never waits.
    /// </summary>
    public UpstreamIdentityCheck? Latest
    {
        get
        {
            var server = _connection.Server;

            lock (_gate)
            {
                return server != null && _latest != null && IsAbout(_latest, server) ? _latest : null;
            }
        }
    }

    /// <summary>
    /// A current answer about who serves this relay's address, for a request about to go
    /// there, asking if there is none. Null when the relay has no server any more.
    /// </summary>
    /// <remarks>
    /// Only the waiting is cancelled by <paramref name="ct"/>: the question goes on for the
    /// next request, which is usually a moment behind.
    /// </remarks>
    public ValueTask<UpstreamIdentityCheck?> EnsureAsync(CancellationToken ct = default) =>
        EnsureAsync(_policy.Lifetime, true, ct);

    /// <summary>
    /// An answer no older than <see cref="UpstreamIdentityPolicy.ConnectionWindow"/>, for a new
    /// connection about to be opened to this relay's address. Null when the relay has no
    /// server any more.
    /// </summary>
    public ValueTask<UpstreamIdentityCheck?> EnsureForConnectionAsync(CancellationToken ct = default) =>
        EnsureAsync(_policy.ConnectionWindow, false, ct);

    /// <summary>
    /// Records an answer someone else got — the console asking the same address while
    /// listing its servers — so the relay acts on it at once. An answer older than the one
    /// already held is ignored.
    /// </summary>
    public void Record(UpstreamIdentityCheck check)
    {
        UpstreamIdentityCheck? before;

        lock (_gate)
        {
            before = _latest;

            if (before != null && before.ServerId == check.ServerId && before.Address == check.Address &&
                before.CheckedAt > check.CheckedAt)
            {
                return;
            }

            _latest = check;

            if (_suspectedAt is { } suspected && check.CheckedAt >= suspected)
            {
                _suspectedAt = null;
            }
        }

        if (before?.Verdict == check.Verdict && before.AnsweredById == check.AnsweredById)
        {
            return;
        }

        if (check.IsMismatch)
        {
            _logger.LogWarning(
                "{Address} answers as {AnsweredByName} ({AnsweredById}{Self}), not {ServerId}: nothing is forwarded to it",
                check.Address, check.AnsweredByName, check.AnsweredById,
                check.Verdict == UpstreamIdentityVerdict.ThisDevice ? ", this device" : "", check.ServerId);
        }
        else if (check.IsConfirmed && before is {IsConfirmed: false})
        {
            _logger.LogInformation("{Address} answers as {ServerId} again", check.Address, check.ServerId);
        }
    }

    /// <summary>
    /// The server stopped answering mid-exchange. Whoever answers there next may be someone
    /// else, so the next request waits for a fresh answer rather than trusting the last one.
    /// </summary>
    public void Suspect()
    {
        lock (_gate)
        {
            _suspectedAt = _time.GetUtcNow();
        }
    }

    /// <remarks>
    /// Cancels a question in flight, and nothing more: the source is left undisposed, so a
    /// check started in the same moment still reads its token rather than throwing.
    /// </remarks>
    public void Dispose() => _lifetime.Cancel();

    private async ValueTask<UpstreamIdentityCheck?> EnsureAsync(TimeSpan maxAge, bool refreshAhead,
        CancellationToken ct)
    {
        var server = _connection.Server;

        if (server == null)
        {
            return null;
        }

        Task<UpstreamIdentityCheck> pending;

        lock (_gate)
        {
            if (TryUse(server, maxAge, out var latest, out var halfway))
            {
                if (refreshAhead && halfway)
                {
                    Start(server, maxAge);
                }

                return latest;
            }

            pending = Start(server, maxAge);
        }

        return await pending.WaitAsync(ct);
    }

    private static bool IsAbout(UpstreamIdentityCheck check, ClientServerConnection server) =>
        string.Equals(check.ServerId, server.ServerId, StringComparison.Ordinal) &&
        string.Equals(check.Address, server.BaseAddress, StringComparison.Ordinal);

    /// <summary>
    /// Whether the last answer can stand, confirmations for up to <paramref name="maxAge"/>,
    /// and whether it is past half of that. Called under the gate.
    /// </summary>
    private bool TryUse(ClientServerConnection server, TimeSpan maxAge, out UpstreamIdentityCheck latest,
        out bool halfway)
    {
        latest = _latest!;
        halfway = false;

        if (_latest == null || !IsAbout(_latest, server) ||
            (_suspectedAt is { } suspected && suspected >= _latest.CheckedAt))
        {
            return false;
        }

        var age = _time.GetUtcNow() - _latest.CheckedAt;

        if (!_latest.IsConfirmed)
        {
            return age < _policy.RetryInterval;
        }

        if (age >= maxAge)
        {
            return false;
        }

        halfway = age >= maxAge / 2;
        return true;
    }

    /// <summary>
    /// The question in flight for this server and address if it is recent enough to stand for
    /// <paramref name="maxAge"/>, or a new one. Called under the gate.
    /// </summary>
    private Task<UpstreamIdentityCheck> Start(ClientServerConnection server, TimeSpan maxAge)
    {
        var now = _time.GetUtcNow();

        // One already under way answers for everybody — unless it was asked before the
        // server was last seen failing, or too long ago for this caller.
        if (_pending is {IsCompleted: false} running &&
            _pendingFor is (string serverId, string address, DateTimeOffset startedAt) &&
            serverId == server.ServerId && address == server.BaseAddress &&
            now - startedAt < maxAge &&
            !(_suspectedAt is { } suspected && suspected >= startedAt))
        {
            return running;
        }

        var id = server.ServerId;
        var at = server.BaseAddress;

        _pendingFor = (id, at, now);

        // Off the gate: the verifier is the console's code, and nothing of it runs under a
        // lock every request takes.
        return _pending = Task.Run(() => AskAsync(id, at, now));
    }

    private async Task<UpstreamIdentityCheck> AskAsync(string serverId, string address, DateTimeOffset startedAt)
    {
        UpstreamIdentityCheck check;

        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
        {
            timeout.CancelAfter(_policy.Timeout);

            try
            {
                check = await _verifier.VerifyAsync(serverId, address, timeout.Token);
            }
            catch (Exception e)
            {
                // Out of time, asking failed outright, or the relay is going away. Nobody was
                // identified, and that is an answer: nothing goes there until somebody is.
                // Never thrown on — a check started ahead of need has nobody to catch it.
                if (e is not OperationCanceledException)
                {
                    _logger.LogDebug(e, "Asking {Address} who it is failed", address);
                }

                check = new UpstreamIdentityCheck(serverId, address, UpstreamIdentityVerdict.Unconfirmed, null, null,
                    e is OperationCanceledException ? "no answer in time" : "the question could not be asked",
                    startedAt);
            }
        }

        // Stamped here, whatever the verifier said: what freshness and suspicion are
        // measured against is when this relay asked.
        check = check with {ServerId = serverId, Address = address, CheckedAt = startedAt};

        Record(check);

        return check;
    }
}

/// <summary>
/// Thrown from the connect step when a new connection to the server's address is refused
/// because that address does not currently answer as the server. The request it was for
/// fails like one whose server could not be reached, and nothing was sent.
/// </summary>
public sealed class UpstreamIdentityRefusedException(UpstreamIdentityCheck? check)
    : IOException(check?.Describe(null) ?? "This relay has no server any more.")
{
    /// <summary>What the relay found at the address; null when it has no server.</summary>
    public UpstreamIdentityCheck? Check { get; } = check;
}

/// <summary>
/// The sockets every connection from a relay to its server goes through: plain TCP, opened
/// only once <see cref="UpstreamIdentity"/> has an answer recent enough that the address is
/// still the server's.
/// </summary>
public static class UpstreamConnections
{
    /// <summary>
    /// A handler for talking to the server — proxies bypassed (a system proxy has no route to
    /// a server on the LAN), redirects, cookies and decompression left to the caller — whose
    /// every new connection is checked first.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(UpstreamIdentity identity, TimeSpan connectTimeout) =>
        new()
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = (context, ct) => ConnectAsync(identity, context.DnsEndPoint, ct)
        };

    /// <summary>Opens a connection to <paramref name="endpoint"/> once the address is confirmed as the server's.</summary>
    public static async ValueTask<Stream> ConnectAsync(UpstreamIdentity identity, DnsEndPoint endpoint,
        CancellationToken ct)
    {
        var check = await identity.EnsureForConnectionAsync(ct);

        // The address confirmed and the one being dialled have to be the same: a store
        // change between the two is caught here rather than trusted.
        if (check is not {IsConfirmed: true} || !check.IsFor(endpoint.Host, endpoint.Port))
        {
            throw new UpstreamIdentityRefusedException(check);
        }

        var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp) {NoDelay = true};

        try
        {
            await socket.ConnectAsync(endpoint, ct);
            return new System.Net.Sockets.NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
