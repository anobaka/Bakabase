using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Records which hub connections came from outside this machine, so
/// <see cref="RemoteConnectionRegistry"/> can hang up on them later, and which of the UI hub's
/// connections may read every options object (<see cref="WebGuiOptionsAudience"/>).
/// </summary>
/// <remarks>
/// A filter rather than an override on the hub itself: the hub lives in the legacy
/// project and knows nothing about remote access, and a connection's standing is
/// decided by the middleware that ran during the handshake — this only reads what it
/// left behind.
/// </remarks>
public sealed class RemoteAccessHubFilter(
    RemoteConnectionRegistry registry,
    IRemoteAccessService remoteAccess,
    IRemoteDeviceService devices) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var remote = context.Context.GetHttpContext()?.GetRemoteAccessContext();

        // Loopback is never tracked, so nothing here can ever hang up on the desktop
        // app's own UI.
        if (remote is {IsLoopback: false})
        {
            var connection = context.Context;
            registry.Track(remote.Device?.Id, connection.ConnectionId, connection.Abort);

            // The gate let in the request that opened this connection, possibly before a change
            // of the settings or the device's revocation: either hangs up on the connections in
            // the registry (RemoteAccessConnectionMonitor, RemoteAccessController.RevokeDevice),
            // and this one was not in it until now. So it is judged again here, after tracking —
            // a change or a revocation made after this check finds it in the registry.
            if (IsRefusedNow(remote))
            {
                registry.Forget(connection.ConnectionId);
                connection.Abort();
                return;
            }
        }

        // Only the UI hub sends options; the progressor hub's connections join no group.
        if (context.Hub is Hub<IWebGuiClient>)
        {
            // Every options object for this machine's window and a paired device, as the
            // authorization filter lets them call anything; and for anyone while the server is
            // Unrestricted. Read after tracking, and from the mode as it is now: a mode change
            // hangs up on the unpaired connections tracked before it, so one tracked after it
            // must be judged by the new mode. No context means the middleware did not run,
            // which is not trusted.
            var readsAllOptions = remote is {IsLoopback: true} or {IsPaired: true} ||
                                  remote != null && remoteAccess.GetEffectiveMode() == RemoteAccessMode.Unrestricted;
            await WebGuiOptionsAudience.AdmitAsync(context.Context, context.Hub.Groups, readsAllOptions);
        }

        await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        registry.Forget(context.Context.ConnectionId);
        await next(context, exception);
    }

    /// <summary>
    /// Whether <see cref="RemoteAccessMiddleware"/> would refuse, under the settings as they are
    /// now, the remote caller it admitted as <paramref name="remote"/>: remote access is off, the
    /// device that signed the request has been revoked since, or the caller has not paired and
    /// pairing is required.
    /// </summary>
    private bool IsRefusedNow(RemoteAccessContext remote)
    {
        var mode = remoteAccess.GetEffectiveMode();
        return mode == RemoteAccessMode.Disabled ||
               remote.Device is { } device && devices.Find(device.Id) == null ||
               mode != RemoteAccessMode.Unrestricted && remoteAccess.GetRequirePairing() && !remote.IsPaired;
    }
}
