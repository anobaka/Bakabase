using System.Net;
using System.Net.Sockets;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Opens a TCP connection to another device the way a browser does: every address its name
/// resolves to is a candidate, IPv4 first, the next one tried alongside whenever the one
/// before has not answered within <see cref="DefaultAttemptDelay"/>, and the first to connect
/// wins (Happy Eyeballs, RFC 8305).
/// </summary>
/// <remarks>
/// <para>
/// .NET's own <see cref="Socket.ConnectAsync(EndPoint, CancellationToken)"/> for a name tries
/// the addresses one after another, each until it fails. Windows usually resolves a computer
/// name to its IPv6 addresses first, while a Bakabase server listens on IPv4 only; where the
/// other device's firewall drops that IPv6 connection silently, the whole connect budget went
/// on it and IPv4 was never tried, although a browser on the same machine connected at once.
/// IPv4 goes first here for that reason: it is the family a Bakabase server answers on. IPv6
/// still gets its turn, so a name that only answers there (a reverse proxy) still connects.
/// </para>
/// <para>
/// An attempt is never cut short while the caller's budget lasts — the delay only decides when
/// the next one starts beside it — so a single slow but working address still connects. Every
/// attempt that loses the race is cancelled, and a connection it made anyway is closed.
/// </para>
/// <para>
/// Every outbound connection to another device goes through here: the desktop app's relays and
/// its probes of the servers it manages, and multi-device sharing's peer requests and
/// discovery. Each takes the instance it uses from its composition —
/// <c>RemoteConsoleOptions.Connector</c>, or a <see cref="DualStackConnector"/> service for
/// library sharing, <see cref="Default"/> without one — so tests can put a network under the
/// real handlers.
/// </para>
/// </remarks>
public sealed class DualStackConnector
{
    /// <summary>How long one address has on its own before the next is tried alongside it.</summary>
    public static readonly TimeSpan DefaultAttemptDelay = TimeSpan.FromMilliseconds(250);

    public static DualStackConnector Default { get; } = new();

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPEndPoint, CancellationToken, ValueTask<Stream>> _connect;
    private readonly TimeSpan _attemptDelay;

    /// <param name="resolve">
    /// Injected for tests; defaults to <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>.
    /// What it returns is screened for a proxy's addresses either way (<see cref="ProxyFakeAddresses"/>).
    /// </param>
    /// <param name="connect">Injected for tests; defaults to a plain TCP socket with Nagle off.</param>
    /// <param name="attemptDelay">Defaults to <see cref="DefaultAttemptDelay"/>.</param>
    public DualStackConnector(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>>? connect = null, TimeSpan? attemptDelay = null)
    {
        _resolve = resolve ?? Dns.GetHostAddressesAsync;
        _connect = connect ?? ConnectSocketAsync;
        _attemptDelay = attemptDelay ?? DefaultAttemptDelay;
    }

    /// <summary>For <see cref="SocketsHttpHandler.ConnectCallback"/>, which still bounds it with its <c>ConnectTimeout</c>.</summary>
    public ValueTask<Stream> ConnectCallback(SocketsHttpConnectionContext context, CancellationToken ct) =>
        ConnectAsync(context.DnsEndPoint, ct);

    public ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken ct) =>
        ConnectAsync(endpoint, null, ct);

    /// <param name="endpoint">Where to connect.</param>
    /// <param name="connectTimeout">
    /// How long connecting may take once the name is resolved, after which it fails as timed out
    /// (<see cref="SocketError.TimedOut"/>); null leaves it to <paramref name="ct"/>. The lookup
    /// is not counted: an address needs none, and a name that is slow to resolve — a Windows
    /// computer name over LLMNR or NetBIOS — then still gets the whole of it to connect.
    /// </param>
    /// <param name="ct">The caller's budget, lookup included.</param>
    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, TimeSpan? connectTimeout, CancellationToken ct) =>
        (await ConnectReachingAsync(endpoint, connectTimeout, ct)).Stream;

    /// <summary>
    /// Connects as <see cref="ConnectAsync(DnsEndPoint, TimeSpan?, CancellationToken)"/> does, and says which of
    /// the addresses answered: for a caller that has to go back to that very one.
    /// </summary>
    public async ValueTask<(Stream Stream, IPEndPoint Reached)> ConnectReachingAsync(DnsEndPoint endpoint,
        TimeSpan? connectTimeout, CancellationToken ct)
    {
        // A proxy's own address (198.18.0.0/15) is never dialled: typed, or what a proxy on this
        // computer answered the name with first, it is refused as that; behind a real answer
        // it is only left out.
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? ProxyFakeAddresses.Screen(endpoint.Host, [literal])
            : Order(ProxyFakeAddresses.Screen(endpoint.Host, await _resolve(endpoint.Host, ct)));

        if (addresses.Count == 0)
        {
            throw new SocketException((int) SocketError.HostNotFound);
        }

        using var clock = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (connectTimeout is { } limit)
        {
            clock.CancelAfter(limit);
        }

        try
        {
            return await RaceAsync(addresses, endpoint.Port, clock.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && clock.IsCancellationRequested)
        {
            throw new SocketException((int) SocketError.TimedOut);
        }
    }

    private async ValueTask<(Stream Stream, IPEndPoint Reached)> RaceAsync(IReadOnlyList<IPAddress> addresses,
        int port, CancellationToken ct)
    {
        if (addresses.Count == 1)
        {
            var only = new IPEndPoint(addresses[0], port);
            return (await _connect(only, ct), only);
        }

        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var attempts = new List<Task<(Stream Stream, IPEndPoint Reached)>>();
        var next = 0;

        async Task<(Stream Stream, IPEndPoint Reached)> Attempt(IPEndPoint to) => (await _connect(to, race.Token), to);

        Task<(Stream Stream, IPEndPoint Reached)> Start() => Attempt(new IPEndPoint(addresses[next++], port));

        try
        {
            attempts.Add(Start());

            while (true)
            {
                List<Task> waiting = [..attempts];

                if (next < addresses.Count)
                {
                    waiting.Add(Task.Delay(_attemptDelay, race.Token));
                }

                var done = await Task.WhenAny(waiting);

                ct.ThrowIfCancellationRequested();

                if (done is not Task<(Stream Stream, IPEndPoint Reached)> attempt)
                {
                    // The delay ran out with nothing answered yet: the next address joins in.
                    attempts.Add(Start());
                    continue;
                }

                attempts.Remove(attempt);

                if (attempt.IsCompletedSuccessfully)
                {
                    return attempt.Result;
                }

                if (attempts.Count == 0 && next == addresses.Count)
                {
                    // Every address failed. The last failure is the one reported, as .NET's
                    // own sequential connect does.
                    return await attempt;
                }

                _ = attempt.Exception;

                if (next < addresses.Count)
                {
                    // A refusal is an answer: the next address need not wait out the delay.
                    attempts.Add(Start());
                }
            }
        }
        finally
        {
            race.Cancel();

            // The losers: one may connect after all before it sees the cancellation.
            foreach (var attempt in attempts)
            {
                _ = attempt.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        t.Result.Stream.Dispose();
                    }
                    else
                    {
                        _ = t.Exception;
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// IPv4 first, then the families alternating, each family in the order the resolver gave.
    /// </summary>
    public static IReadOnlyList<IPAddress> Order(IEnumerable<IPAddress> addresses)
    {
        var all = addresses.ToList();
        var v4 = all.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
        var v6 = all.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();
        var ordered = new List<IPAddress>(v4.Count + v6.Count);

        for (var i = 0; i < Math.Max(v4.Count, v6.Count); i++)
        {
            if (i < v4.Count)
            {
                ordered.Add(v4[i]);
            }

            if (i < v6.Count)
            {
                ordered.Add(v6[i]);
            }
        }

        return ordered;
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) {NoDelay = true};

        try
        {
            await socket.ConnectAsync(endpoint, ct);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
