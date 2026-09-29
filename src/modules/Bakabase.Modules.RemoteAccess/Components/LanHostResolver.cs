using System.Net;
using System.Net.Sockets;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// What a name that another device goes by resolves to: a <c>.local</c> name over mDNS
/// (<see cref="MdnsHostResolver"/>) and the system resolver side by side, every other name the
/// system resolver's alone.
/// </summary>
/// <remarks>
/// <para>
/// For a <c>.local</c> name both are asked at once, and whichever settles it first wins:
/// </para>
/// <list type="bullet">
/// <item>
/// The system's answer when it comes first and is direct — its first address not a proxy's
/// (<see cref="ProxyFakeAddresses"/>), and at least one IPv4 address, which is what a Bakabase
/// server listens on. A name the hosts file, the router's DNS or a domain's DNS knows
/// (<c>fileserver.corp.local</c>) then costs no wait for mDNS; the question goes on in the
/// background and its answer is kept for next time.
/// </item>
/// <item>
/// Otherwise mDNS's, when it has an IPv4 address. A proxy in fake-IP or TUN mode answers the
/// system at once with an address of its own, which is never taken early, so mDNS decides.
/// </item>
/// <item>
/// When nothing on the LAN answered, the system's, whatever it is.
/// </item>
/// <item>
/// When mDNS answered with IPv6 addresses alone (a Mac answering its own name with link-local
/// addresses), those together with the system's direct IPv4 addresses; and when the system
/// answered with a proxy's address instead, the mDNS addresses with that proxy's address
/// beside them (<see cref="LanHostResolution.ProxyAddress"/>), so that a connection that fails
/// at all of them says a proxy took the name over rather than that nothing answered.
/// </item>
/// </list>
/// <para>
/// What comes back is screened by the caller either way
/// (<see cref="ProxyFakeAddresses.Screen(string, IReadOnlyList{IPAddress}, out IPAddress?)"/>): a
/// system answer can still be a proxy's.
/// </para>
/// <para>
/// The default resolver of <see cref="DualStackConnector"/>, and so of every connection the
/// desktop app makes to another device.
/// </para>
/// </remarks>
public sealed class LanHostResolver
{
    public static LanHostResolver Default { get; } = new(MdnsHostResolver.Default, Dns.GetHostAddressesAsync);

    private readonly MdnsHostResolver? _mdns;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _system;

    /// <param name="mdns">Null to leave every name to <paramref name="system"/>.</param>
    /// <param name="system">The system resolver; injected for tests.</param>
    public LanHostResolver(MdnsHostResolver? mdns, Func<string, CancellationToken, Task<IPAddress[]>> system)
    {
        _mdns = mdns;
        _system = system;
    }

    /// <summary>The addresses of <see cref="ResolveDetailedAsync"/>.</summary>
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        (await ResolveDetailedAsync(host, ct)).Addresses;

    /// <summary>Where <paramref name="host"/> can be connected to: see the remarks on <see cref="LanHostResolver"/>.</summary>
    public async Task<LanHostResolution> ResolveDetailedAsync(string host, CancellationToken ct)
    {
        if (_mdns == null || !MdnsHostResolver.IsMdnsName(host))
        {
            return new LanHostResolution(await _system(host, ct));
        }

        using var systemBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var system = Start(() => _system(host, systemBudget.Token));
        var mdns = _mdns.ResolveAsync(host, ct);

        if (await Task.WhenAny(mdns, system) == system && IsDirect(system))
        {
            // The mDNS question runs its short course on its own and keeps its answer.
            Forget(mdns);
            return new LanHostResolution(system.Result);
        }

        IReadOnlyList<IPAddress> answered;

        try
        {
            answered = await mdns;
        }
        catch
        {
            await systemBudget.CancelAsync();
            Forget(system);
            throw;
        }

        if (answered.Count == 0)
        {
            return new LanHostResolution(await system);
        }

        if (answered.Any(IsIPv4))
        {
            await systemBudget.CancelAsync();
            Forget(system);
            return new LanHostResolution(answered.ToArray());
        }

        // IPv6 alone, which a Bakabase server does not listen on: what the system says still
        // counts — an IPv4 address it knows, or that a proxy took the name over.
        IPAddress[] fromSystem;

        try
        {
            fromSystem = await system;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            fromSystem = [];
        }

        if (fromSystem.Length > 0 && ProxyFakeAddresses.Contains(fromSystem[0]))
        {
            return new LanHostResolution(answered.ToArray(), fromSystem[0]);
        }

        return new LanHostResolution([
            ..fromSystem.Where(a => IsIPv4(a) && !ProxyFakeAddresses.Contains(a) && !answered.Contains(a)).Distinct(),
            ..answered
        ]);
    }

    /// <summary>A system answer that needs no word from mDNS: see the remarks on <see cref="LanHostResolver"/>.</summary>
    private static bool IsDirect(Task<IPAddress[]> system) =>
        system.IsCompletedSuccessfully && system.Result is {Length: > 0} addresses &&
        !ProxyFakeAddresses.Contains(addresses[0]) &&
        addresses.Any(a => IsIPv4(a) && !ProxyFakeAddresses.Contains(a));

    private static bool IsIPv4(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork || address.IsIPv4MappedToIPv6;

    /// <summary>A lookup as a task, a synchronous throw included.</summary>
    private static Task<IPAddress[]> Start(Func<Task<IPAddress[]>> lookup)
    {
        try
        {
            return lookup();
        }
        catch (Exception e)
        {
            return Task.FromException<IPAddress[]>(e);
        }
    }

    /// <summary>Lets a lookup whose answer is not needed end however it ends.</summary>
    private static void Forget(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}

/// <summary>What <see cref="LanHostResolver"/> found for a name.</summary>
/// <param name="Addresses">
/// Where to connect, in the resolver's order; still to be screened
/// (<see cref="ProxyFakeAddresses.Screen(string, IReadOnlyList{IPAddress}, out IPAddress?)"/>).
/// </param>
/// <param name="ProxyAddress">
/// The proxy's address the system resolver answered the name with first, when the LAN gave
/// <paramref name="Addresses"/> instead but none a Bakabase server listens on (IPv6 alone): once
/// none of them connects, a <see cref="ProxyFakeAddressException"/> says so. Null otherwise.
/// </param>
public readonly record struct LanHostResolution(IPAddress[] Addresses, IPAddress? ProxyAddress = null);
