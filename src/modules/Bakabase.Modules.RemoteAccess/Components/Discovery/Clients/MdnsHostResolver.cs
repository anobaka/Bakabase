using System.Net;
using System.Net.Sockets;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

/// <summary>
/// Resolves a <c>.local</c> name by asking the network's mDNS responders directly — this
/// computer's own resolver not involved.
/// </summary>
/// <remarks>
/// <para>
/// A proxy in fake-IP or TUN mode (Clash, Mihomo, Surge, sing-box…) answers the system's
/// lookups itself, <c>.local</c> names included, with an address of its own that only leads
/// back into the proxy (<see cref="ProxyFakeAddresses"/>). Asking the LAN's responders over
/// multicast is not something such a proxy intercepts, and it is how a <c>.local</c> name is
/// meant to be resolved anyway.
/// </para>
/// <para>
/// A name answers with its own addresses. Only those that can be connected to from here are
/// kept: never a proxy's (a responder on a proxied machine advertises its TUN adapter's), never
/// a loopback address (from another machine it means the reader), and an IPv6 link-local
/// address only with the interface it came in on as its scope — without one, nothing says
/// which link it is on.
/// </para>
/// <para>
/// A Bakabase server listens on IPv4 only, and some machines answer their own name with IPv6
/// link-local addresses alone. Every Bakabase with remote access on advertises
/// <c>{its machine name}-bakabase.local</c> with its IPv4 addresses, so when the name gives no
/// IPv4 address, that name on the same machine is asked in the same breath and its IPv4
/// addresses go first. It is still only a place to connect: whoever answers there is asked who
/// it is, as at any address.
/// </para>
/// <para>
/// Answers stand for <see cref="AnswerLifetime"/> (or their own TTL, if shorter), silence for
/// <see cref="SilenceLifetime"/>, so a relay asking before every burst of new connections does
/// not multicast each time; lookups of one name at the same moment share one question. Nothing
/// here throws for the network: no interface, no answer, a socket refused — the answer is an
/// empty list, and the caller falls back to the system resolver.
/// </para>
/// </remarks>
public sealed class MdnsHostResolver
{
    /// <summary>How long a question waits for a first answer.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>After a first answer, how long the rest have to arrive: other interfaces, other record types.</summary>
    public static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(150);

    /// <summary>How long an answer stands at most.</summary>
    public static readonly TimeSpan AnswerLifetime = TimeSpan.FromSeconds(10);

    /// <summary>How long no answer stands.</summary>
    public static readonly TimeSpan SilenceLifetime = TimeSpan.FromSeconds(5);

    private const string LocalSuffix = ".local";
    private const string BakabaseSuffix = "-bakabase";

    /// <summary>The longest a DNS label may be; a longer alias would not match the advertisement's, which is cut there.</summary>
    private const int MaxLabelLength = 63;

    public static MdnsHostResolver Default { get; } = new(new MdnsSocketTransport());

    private readonly IMdnsQueryTransport _transport;
    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _settle;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (IReadOnlyList<IPAddress> Addresses, DateTimeOffset Until)> _answers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<IReadOnlyList<IPAddress>>> _asking = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="transport">The network; a test's fake, or <see cref="MdnsSocketTransport"/>.</param>
    /// <param name="time">The clock answers are kept by and questions are timed with.</param>
    /// <param name="timeout">Defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="settle">Defaults to <see cref="DefaultSettle"/>.</param>
    public MdnsHostResolver(IMdnsQueryTransport transport, TimeProvider? time = null, TimeSpan? timeout = null,
        TimeSpan? settle = null)
    {
        _transport = transport;
        _time = time ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
        _settle = settle ?? DefaultSettle;
    }

    /// <summary>Whether <paramref name="host"/> is a name mDNS answers for: one ending in <c>.local</c>.</summary>
    public static bool IsMdnsName(string host)
    {
        var name = host.TrimEnd('.');

        return name.Length > LocalSuffix.Length &&
               name.EndsWith(LocalSuffix, StringComparison.OrdinalIgnoreCase) &&
               name[^(LocalSuffix.Length + 1)] != '.';
    }

    /// <summary>
    /// The name Bakabase itself advertises on the machine <paramref name="host"/> names
    /// (<c>{label}-bakabase.local</c>, see <see cref="MdnsAdvertisement.HostName"/>); null when
    /// <paramref name="host"/> is not a single label under <c>.local</c> or already is one.
    /// </summary>
    public static string? BakabaseNameOf(string host)
    {
        if (!IsMdnsName(host))
        {
            return null;
        }

        var name = host.TrimEnd('.');
        var label = name[..^LocalSuffix.Length];

        return label.Contains('.') || label.EndsWith(BakabaseSuffix, StringComparison.OrdinalIgnoreCase) ||
               System.Text.Encoding.UTF8.GetByteCount(label) + BakabaseSuffix.Length > MaxLabelLength
            ? null
            : $"{label.ToLowerInvariant()}{BakabaseSuffix}{LocalSuffix}";
    }

    /// <summary>
    /// What the LAN's responders answer <paramref name="host"/> with, usable addresses only; empty
    /// when nothing usable answered in time. Throws only when <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// Only the waiting is cancelled by <paramref name="ct"/>: a question already asked runs its
    /// short course for whoever else is waiting on it, and its answer is kept.
    /// </remarks>
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        if (!IsMdnsName(host))
        {
            return [];
        }

        var name = host.TrimEnd('.');
        Task<IReadOnlyList<IPAddress>> asking;

        lock (_gate)
        {
            if (_answers.TryGetValue(name, out var known))
            {
                if (known.Until > _time.GetUtcNow())
                {
                    return known.Addresses;
                }

                _answers.Remove(name);
            }

            if (!_asking.TryGetValue(name, out asking!))
            {
                // Off the gate: the question sends before its first await.
                _asking[name] = asking = Task.Run(() => AskAndKeepAsync(name), CancellationToken.None);
            }
        }

        return await asking.WaitAsync(ct);
    }

    private async Task<IReadOnlyList<IPAddress>> AskAndKeepAsync(string name)
    {
        var answers = new Answers(name, BakabaseNameOf(name));

        try
        {
            await AskAsync(answers);
        }
        catch (Exception)
        {
            // Best effort: whatever arrived before the network failed still counts.
        }

        var addresses = answers.Build();
        var lifetime = addresses.Count == 0
            ? SilenceLifetime
            : answers.Ttl is { } ttl && ttl < AnswerLifetime
                ? ttl
                : AnswerLifetime;

        lock (_gate)
        {
            _asking.Remove(name);

            if (lifetime > TimeSpan.Zero)
            {
                var until = _time.GetUtcNow() + lifetime;

                _answers[name] = (addresses, until);

                // What Bakabase's name on that machine answered holds for that name too: its
                // responder answers at most once a second, and a lookup of it by name right
                // after this one would go unanswered.
                if (answers.BakabaseName is { } bakabase && answers.BakabaseAddresses.Count > 0 &&
                    !_answers.ContainsKey(bakabase))
                {
                    _answers[bakabase] = (answers.BakabaseAddresses, until);
                }
            }
        }

        return addresses;
    }

    private async Task AskAsync(Answers answers)
    {
        // A one-shot query (RFC 6762 §5.1, §6.7): sent from a port of its own, which a responder
        // answers straight back to — repeating this id — with the QU bit asking the same of one
        // that goes by the bit. Responders that only multicast (Bakabase's own) are heard too.
        var id = (ushort) Random.Shared.Next(1, ushort.MaxValue);
        List<byte[]> queries =
        [
            MdnsMessage.BuildQuery(answers.Name, MdnsMessage.TypeA, id, true),
            MdnsMessage.BuildQuery(answers.Name, MdnsMessage.TypeAaaa, id, true)
        ];

        if (answers.BakabaseName is { } bakabase)
        {
            queries.Add(MdnsMessage.BuildQuery(bakabase, MdnsMessage.TypeA, id, true));
        }

        using var window = new CancellationTokenSource(_timeout, _time);
        var deadline = _time.GetUtcNow() + _timeout;
        var settling = false;

        try
        {
            await foreach (var datagram in _transport.ExchangeAsync(queries, window.Token).WithCancellation(window.Token))
            {
                if (!MdnsMessage.TryParseResponse(datagram.Data, out var records) ||
                    !answers.Add(records, datagram.InterfaceIndex) || settling)
                {
                    continue;
                }

                // Something usable is in: the rest of what was asked has a moment to follow,
                // never longer than the question had left.
                settling = true;

                if (deadline - _time.GetUtcNow() > _settle)
                {
                    window.CancelAfter(_settle);
                }
            }
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
            // How this ends.
        }
    }

    /// <summary>
    /// An address as it can be connected to from here, or null: see the remarks on
    /// <see cref="MdnsHostResolver"/>.
    /// </summary>
    /// <param name="interfaceIndex">The interface the answer came in on; 0 when not known.</param>
    public static IPAddress? Usable(IPAddress address, int interfaceIndex)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (ProxyFakeAddresses.Contains(address) || IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.None) ||
            address.IsIPv6Multicast ||
            (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] is >= 224 and <= 239))
        {
            return null;
        }

        if (!address.IsIPv6LinkLocal || address.ScopeId != 0)
        {
            return address;
        }

        return interfaceIndex > 0 ? new IPAddress(address.GetAddressBytes(), interfaceIndex) : null;
    }

    /// <summary>What has come back so far for one name, and for Bakabase's name on the same machine.</summary>
    private sealed class Answers(string name, string? bakabaseName)
    {
        private readonly List<IPAddress> _own = [];
        private readonly List<IPAddress> _bakabase = [];

        public string Name { get; } = name;
        public string? BakabaseName { get; } = bakabaseName;

        /// <summary>The shortest TTL among the addresses kept, once any are.</summary>
        public TimeSpan? Ttl { get; private set; }

        /// <summary>The IPv4 addresses Bakabase's name on the same machine answered with.</summary>
        public IReadOnlyList<IPAddress> BakabaseAddresses => _bakabase.ToList();

        /// <summary>Takes the address records about the names asked; true when one was new and usable.</summary>
        public bool Add(IEnumerable<MdnsMessage.ParsedRecord> records, int interfaceIndex)
        {
            var added = false;

            foreach (var record in records)
            {
                if (record.Type is not (MdnsMessage.TypeA or MdnsMessage.TypeAaaa) || record.Address == null)
                {
                    continue;
                }

                var own = MdnsMessage.NamesEqual(record.Name, Name);

                if (!own && !(BakabaseName != null && MdnsMessage.NamesEqual(record.Name, BakabaseName)))
                {
                    continue;
                }

                var list = own ? _own : _bakabase;
                var address = Usable(record.Address, interfaceIndex);

                if (address == null)
                {
                    continue;
                }

                if (record.Ttl == 0)
                {
                    // A goodbye: the address is going away.
                    list.Remove(address);
                    continue;
                }

                if (list.Contains(address) || (!own && address.AddressFamily != AddressFamily.InterNetwork))
                {
                    continue;
                }

                list.Add(address);
                var ttl = TimeSpan.FromSeconds(record.Ttl);
                Ttl = Ttl is { } shortest && shortest < ttl ? shortest : ttl;
                added = true;
            }

            return added;
        }

        /// <summary>
        /// The name's own addresses; when none of them is IPv4, Bakabase's name's IPv4 addresses
        /// on the same machine first.
        /// </summary>
        public IReadOnlyList<IPAddress> Build() =>
            _own.Any(a => a.AddressFamily == AddressFamily.InterNetwork) ? _own.ToList() : [.._bakabase, .._own];
    }
}

/// <summary>One datagram a question brought back.</summary>
/// <param name="Data">The datagram as it arrived.</param>
/// <param name="InterfaceIndex">The index of the interface it arrived on; 0 when not known.</param>
public readonly record struct MdnsDatagram(byte[] Data, int InterfaceIndex);

/// <summary>The network under <see cref="MdnsHostResolver"/>: a test's, or <see cref="MdnsSocketTransport"/>.</summary>
public interface IMdnsQueryTransport
{
    /// <summary>
    /// Sends each of <paramref name="queries"/> on this machine's LAN interfaces and yields every
    /// datagram that arrives until <paramref name="ct"/> is cancelled — answers to them, and
    /// whatever else is said on the mDNS group meanwhile. Nothing is yielded where nothing can
    /// be sent.
    /// </summary>
    IAsyncEnumerable<MdnsDatagram> ExchangeAsync(IReadOnlyList<byte[]> queries, CancellationToken ct);
}
