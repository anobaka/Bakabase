using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Hangs up on the open hub connections that a change of the remote-access settings no longer
/// admits the way they were admitted.
/// </summary>
/// <remarks>
/// <para>
/// A hub connection is judged once, when it opens: the gate lets its handshake in, and the hub
/// filter decides from the mode at that moment what it is sent of the options
/// (<see cref="RemoteAccessHubFilter"/>). So a change that narrows what a caller may do has to
/// reach the connections already open, or they keep receiving pushes. It is the change itself
/// that is watched, not the route that usually makes it: the remote-access settings page, the
/// sharing wizard on the devices page and a reload of the settings file from disk all write
/// these options.
/// </para>
/// <list type="bullet">
/// <item>Remote access off: every remote connection is hung up on.</item>
/// <item>The mode changed otherwise, or pairing became a requirement: every unpaired one is.
/// What such a connection is sent was decided under the old mode — every options object while
/// Unrestricted, only what browsing reads while Enabled. Hung up on, it reconnects and is judged
/// by the new mode, or is refused.</item>
/// </list>
/// <para>
/// A paired device keeps its connection while remote access stays on: nothing about what it may
/// read has changed. A connection whose handshake the gate let in before a change, but which
/// the hub had not opened yet when the change was made, is not in the registry to be hung up
/// on; the hub filter judges it again once it is.
/// </para>
/// </remarks>
public sealed class RemoteAccessConnectionMonitor(AspNetCoreOptionsManager<RemoteAccessOptions> options,
    RemoteAccessDefaults defaults, RemoteConnectionRegistry connections) : IHostedService, IDisposable
{
    private readonly object _lock = new();
    private (RemoteAccessMode Mode, bool RequirePairing) _last;
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // Subscribed before the first read, so no change falls between the two: one made
            // meanwhile waits for the lock and is compared with what was read.
            _subscription = options.OnChange(OnChange);
            _last = Read(options.Value);
        }

        return Task.CompletedTask;
    }

    private void OnChange(RemoteAccessOptions value)
    {
        lock (_lock)
        {
            var before = _last;
            var now = _last = Read(value);

            if (now.Mode == RemoteAccessMode.Disabled)
            {
                connections.AbortAll();
            }
            else if (now.Mode != before.Mode || now.RequirePairing && !before.RequirePairing)
            {
                connections.AbortUnpaired();
            }
        }
    }

    private (RemoteAccessMode Mode, bool RequirePairing) Read(RemoteAccessOptions value) =>
        (value.Mode ?? defaults.Mode, value.RequirePairing);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
