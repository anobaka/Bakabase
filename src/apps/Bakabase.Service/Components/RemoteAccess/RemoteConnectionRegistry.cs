using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// The live hub connections that came from outside this machine, and the means to hang
/// up on them.
/// </summary>
/// <remarks>
/// <para>
/// Every other check in the gate is per-request: an HTTP call from a revoked device is
/// refused the next time it is made, because the authenticator looks the device up each
/// time. A hub connection is the exception — it is authorized once, at the handshake,
/// and then keeps receiving pushes (options among them) for as long as it stays open.
/// So revoking a device, or closing the door on unpaired ones, has to reach back into
/// connections that are already established.
/// </para>
/// <para>
/// Loopback connections are never tracked, which is what keeps the all-in-one out of
/// this entirely: on a desktop install this registry stays empty, and nothing here can
/// hang up on the app's own UI.
/// </para>
/// <para>
/// Holds an abort callback rather than SignalR's connection context, so every rule about
/// who gets dropped is decidable without a hub.
/// </para>
/// </remarks>
public sealed class RemoteConnectionRegistry
{
    private sealed record Entry(string? DeviceId, Action Abort);

    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    public int Count => _connections.Count;

    /// <param name="deviceId">Null for a remote caller that signed nothing.</param>
    public void Track(string? deviceId, string connectionId, Action abort) =>
        _connections[connectionId] = new Entry(deviceId, abort);

    public void Forget(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Hangs up on one revoked device.</summary>
    public int AbortDevice(string deviceId) =>
        Abort(e => string.Equals(e.DeviceId, deviceId, StringComparison.Ordinal));

    /// <summary>
    /// Hangs up on everyone who has not paired, for when pairing becomes a requirement or the
    /// mode changes (<see cref="RemoteAccessConnectionMonitor"/>). Leaving them connected
    /// would keep serving them as they were admitted under the old settings.
    /// </summary>
    public int AbortUnpaired() => Abort(e => e.DeviceId == null);

    /// <summary>Hangs up on every remote caller, for when remote access is switched off.</summary>
    public int AbortAll() => Abort(_ => true);

    private int Abort(Func<Entry, bool> predicate)
    {
        var doomed = _connections.Where(kv => predicate(kv.Value)).ToArray();

        foreach (var (connectionId, entry) in doomed)
        {
            // Dropped from the map first: aborting can raise the disconnect callback
            // synchronously, and an entry that outlives its connection would be aborted
            // again on the next sweep.
            _connections.TryRemove(connectionId, out _);

            try
            {
                entry.Abort();
            }
            catch (ObjectDisposedException)
            {
                // The connection went away on its own between the sweep and the abort.
            }
        }

        return doomed.Length;
    }
}
