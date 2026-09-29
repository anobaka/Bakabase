using System.Net;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// What a name that another device goes by resolves to: a <c>.local</c> name over mDNS first
/// (<see cref="MdnsHostResolver"/>), and the system resolver for every other name — and for a
/// <c>.local</c> name nothing on the LAN answered for.
/// </summary>
/// <remarks>
/// <para>
/// The system resolver is asked alongside mDNS rather than after it, so a name mDNS has no
/// answer for costs no more than the longer of the two, not both; its answer is dropped when
/// mDNS has one. What comes back is screened by the caller either way
/// (<see cref="ProxyFakeAddresses.Screen"/>): a system answer can still be a proxy's.
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

    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (_mdns == null || !MdnsHostResolver.IsMdnsName(host))
        {
            return await _system(host, ct);
        }

        using var systemBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<IPAddress[]> system;

        try
        {
            system = _system(host, systemBudget.Token);
        }
        catch (Exception e)
        {
            system = Task.FromException<IPAddress[]>(e);
        }

        IReadOnlyList<IPAddress> answered;

        try
        {
            answered = await _mdns.ResolveAsync(host, ct);
        }
        catch
        {
            Forget(system);
            throw;
        }

        if (answered.Count == 0)
        {
            return await system;
        }

        await systemBudget.CancelAsync();
        Forget(system);

        return answered.ToArray();
    }

    /// <summary>Lets a lookup whose answer is not needed end however it ends.</summary>
    private static void Forget(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
