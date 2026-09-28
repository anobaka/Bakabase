using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// How this device connects to another one by name: IPv4 and IPv6 raced, not tried one after
/// another — the difference between a computer name that connects at once and one that times
/// out while a browser on the same machine opens it fine.
/// </summary>
/// <remarks>
/// The name, the network and the sockets are fakes: a Windows name that resolves IPv6-first to
/// an address whose packets are dropped cannot be staged on a test machine. The clock is real,
/// with bounds far from what the rules decide.
/// </remarks>
[TestClass]
public class DualStackConnectorTests
{
    private static readonly IPAddress LinkLocal = IPAddress.Parse("fe80::1");
    private static readonly IPAddress LinkLocal2 = IPAddress.Parse("fe80::2");
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.5");
    private static readonly IPAddress Lan2 = IPAddress.Parse("192.168.1.6");

    /// <summary>How long a black-holed attempt would take on Windows (SYN retransmits).</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    [TestMethod]
    [DataRow(10, DisplayName = "IPv4 answers at once")]
    [DataRow(600, DisplayName = "IPv4 answers after the IPv6 attempt has started")]
    public async Task An_IPv6_address_that_drops_everything_does_not_hold_up_IPv4(int ipv4AnswersAfterMs)
    {
        // As Windows resolves a computer name: its IPv6 address first. The server listens on
        // IPv4 only and the firewall drops the IPv6 connection without a word.
        var network = new FakeNetwork
        {
            [LinkLocal] = FakeNetwork.BlackHole,
            [Lan] = FakeNetwork.AnswersAfter(TimeSpan.FromMilliseconds(ipv4AnswersAfterMs))
        };
        var connector = network.Connector([LinkLocal, Lan]);

        using var budget = new CancellationTokenSource(Budget);
        var watch = Stopwatch.StartNew();
        await using var stream = await connector.ConnectAsync(new DnsEndPoint("PC1", 34567), budget.Token);

        Assert.AreEqual(new IPEndPoint(Lan, 34567), ((FakeConnection) stream).To);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");

        // IPv4 went first, and an IPv6 attempt started meanwhile was not left running.
        Assert.AreEqual(Lan, network.Tried[0].Address);
        await network.AllSettledAsync();
        Assert.IsTrue(network.Cancelled.SetEquals(network.Tried.Where(t => t.Address.Equals(LinkLocal))));
        if (ipv4AnswersAfterMs > 250)
        {
            Assert.AreEqual(2, network.Tried.Count, "IPv6 was tried alongside once IPv4 had not answered");
        }
    }

    [TestMethod]
    public async Task The_next_address_joins_in_when_the_first_has_not_answered()
    {
        // The other way round: an IPv4 address nobody answers at, and the name also answering
        // on IPv6 — a reverse proxy on a dual-stack host.
        var network = new FakeNetwork
        {
            [Lan] = FakeNetwork.BlackHole,
            [LinkLocal] = FakeNetwork.AnswersAfter(TimeSpan.Zero)
        };
        var connector = network.Connector([LinkLocal, Lan], TimeSpan.FromMilliseconds(100));

        using var budget = new CancellationTokenSource(Budget);
        var watch = Stopwatch.StartNew();
        await using var stream = await connector.ConnectAsync(new DnsEndPoint("nas.local", 8080), budget.Token);

        Assert.AreEqual(new IPEndPoint(LinkLocal, 8080), ((FakeConnection) stream).To);
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(90), $"took {watch.Elapsed}");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");

        await network.AllSettledAsync();
        CollectionAssert.AreEquivalent(new[] {new IPEndPoint(Lan, 8080)}, network.Cancelled.ToArray());
    }

    [TestMethod]
    public async Task A_refused_address_hands_over_without_waiting_out_the_delay()
    {
        var network = new FakeNetwork
        {
            [Lan] = FakeNetwork.Refuses,
            [LinkLocal] = FakeNetwork.AnswersAfter(TimeSpan.Zero)
        };
        var connector = network.Connector([Lan, LinkLocal], TimeSpan.FromSeconds(30));

        using var budget = new CancellationTokenSource(Budget);
        var watch = Stopwatch.StartNew();
        await using var stream = await connector.ConnectAsync(new DnsEndPoint("PC1", 34567), budget.Token);

        Assert.AreEqual(LinkLocal, ((FakeConnection) stream).To.Address);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task Every_address_refusing_is_reported_as_a_refusal_not_as_a_timeout()
    {
        var network = new FakeNetwork
        {
            [Lan] = FakeNetwork.Refuses,
            [Lan2] = FakeNetwork.Refuses,
            [LinkLocal] = FakeNetwork.Refuses
        };
        var connector = network.Connector([LinkLocal, Lan, Lan2]);

        var refused = await Assert.ThrowsExceptionAsync<SocketException>(() =>
            connector.ConnectAsync(new DnsEndPoint("PC1", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual(SocketError.ConnectionRefused, refused.SocketErrorCode);
        Assert.AreEqual(3, network.Tried.Count);
    }

    [TestMethod]
    public async Task The_callers_budget_still_ends_the_wait_and_every_attempt_with_it()
    {
        var network = new FakeNetwork
        {
            [Lan] = FakeNetwork.BlackHole,
            [LinkLocal] = FakeNetwork.BlackHole,
            [LinkLocal2] = FakeNetwork.BlackHole
        };
        var connector = network.Connector([LinkLocal, LinkLocal2, Lan], TimeSpan.FromMilliseconds(50));

        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            connector.ConnectAsync(new DnsEndPoint("PC1", 34567), budget.Token).AsTask());

        await network.AllSettledAsync();
        Assert.AreEqual(3, network.Tried.Count);
        Assert.IsTrue(network.Cancelled.SetEquals(network.Tried));
    }

    [TestMethod]
    public async Task A_connection_made_after_losing_the_race_is_closed()
    {
        // An attempt that does not heed its cancellation — a connect already past the point of
        // no return — must not leave a connection nobody holds.
        var late = new FakeConnection(new IPEndPoint(LinkLocal, 34567));
        var network = new FakeNetwork
        {
            [LinkLocal] = async (to, _) =>
            {
                await Task.Delay(300, CancellationToken.None);
                return late;
            },
            [Lan] = FakeNetwork.AnswersAfter(TimeSpan.FromMilliseconds(100))
        };
        var connector = network.Connector([Lan, LinkLocal], TimeSpan.FromMilliseconds(20));

        await using var stream = await connector.ConnectAsync(new DnsEndPoint("PC1", 34567), CancellationToken.None);

        Assert.AreEqual(Lan, ((FakeConnection) stream).To.Address);
        await network.AllSettledAsync();
        Assert.IsTrue(await late.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(((FakeConnection) stream).Closed.Task.IsCompleted);
    }

    [TestMethod]
    public async Task An_address_needs_no_lookup()
    {
        var network = new FakeNetwork {[Lan] = FakeNetwork.AnswersAfter(TimeSpan.Zero)};
        var connector = network.Connector([]);

        await using var stream = await connector.ConnectAsync(new DnsEndPoint("192.168.1.5", 34567), CancellationToken.None);

        Assert.AreEqual(0, network.Lookups);
        Assert.AreEqual(new IPEndPoint(Lan, 34567), ((FakeConnection) stream).To);
    }

    [TestMethod]
    public void IPv4_goes_first_then_the_families_alternate()
    {
        CollectionAssert.AreEqual(new[] {Lan, LinkLocal, Lan2, LinkLocal2},
            DualStackConnector.Order([LinkLocal, LinkLocal2, Lan, Lan2]).ToArray());
        CollectionAssert.AreEqual(new[] {LinkLocal, LinkLocal2},
            DualStackConnector.Order([LinkLocal, LinkLocal2]).ToArray());
    }

    [TestMethod]
    public async Task A_real_name_connects_to_a_server_listening_on_IPv4_only()
    {
        // What a Bakabase server does: 0.0.0.0, never [::]. "localhost" resolves to ::1 as
        // well, where nothing listens.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var accepted = listener.AcceptSocketAsync();

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var stream = await DualStackConnector.Default.ConnectAsync(new DnsEndPoint("localhost", port),
            budget.Token);
        using var socket = await accepted.WaitAsync(budget.Token);

        Assert.IsInstanceOfType<NetworkStream>(stream);
        Assert.AreEqual(IPAddress.Loopback, ((IPEndPoint) socket.RemoteEndPoint!).Address);
    }

    private sealed class FakeConnection(IPEndPoint to) : MemoryStream
    {
        public IPEndPoint To { get; } = to;
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void Dispose(bool disposing)
        {
            Closed.TrySetResult(true);
            base.Dispose(disposing);
        }
    }

    /// <summary>What each address does with a connection attempt, and what was tried.</summary>
    private sealed class FakeNetwork
    {
        public static readonly Func<IPEndPoint, CancellationToken, Task<Stream>> BlackHole = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };

        public static readonly Func<IPEndPoint, CancellationToken, Task<Stream>> Refuses = async (_, _) =>
        {
            await Task.Yield();
            throw new SocketException((int) SocketError.ConnectionRefused);
        };

        public static Func<IPEndPoint, CancellationToken, Task<Stream>> AnswersAfter(TimeSpan delay) =>
            async (to, ct) =>
            {
                await Task.Delay(delay, ct);
                return new FakeConnection(to);
            };

        private readonly ConcurrentDictionary<IPAddress, Func<IPEndPoint, CancellationToken, Task<Stream>>> _behaviour = new();
        private readonly ConcurrentQueue<Task> _attempts = new();
        private readonly ConcurrentQueue<IPEndPoint> _tried = new();
        private readonly ConcurrentDictionary<IPEndPoint, bool> _cancelled = new();
        private int _lookups;

        public Func<IPEndPoint, CancellationToken, Task<Stream>> this[IPAddress address]
        {
            set => _behaviour[address] = value;
        }

        public List<IPEndPoint> Tried => _tried.ToList();
        public HashSet<IPEndPoint> Cancelled => _cancelled.Keys.ToHashSet();
        public int Lookups => _lookups;

        public DualStackConnector Connector(IPAddress[] resolvesTo, TimeSpan? attemptDelay = null) =>
            new((_, _) =>
            {
                Interlocked.Increment(ref _lookups);
                return Task.FromResult(resolvesTo);
            }, Connect, attemptDelay);

        /// <summary>Every attempt has finished, one way or the other.</summary>
        public Task AllSettledAsync() =>
            Task.WhenAll(_attempts).WaitAsync(TimeSpan.FromSeconds(10));

        private ValueTask<Stream> Connect(IPEndPoint to, CancellationToken ct)
        {
            _tried.Enqueue(to);
            var attempt = _behaviour[to.Address](to, ct);
            _attempts.Enqueue(attempt.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    _cancelled[to] = true;
                }
            }, TaskScheduler.Default));

            return new ValueTask<Stream>(attempt);
        }
    }
}
