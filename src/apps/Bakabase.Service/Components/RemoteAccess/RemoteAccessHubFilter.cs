using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Records which hub connections came from outside this machine, so
/// <see cref="RemoteConnectionRegistry"/> can hang up on them later, and which of them may
/// read every options object (<see cref="WebGuiOptionsAudience"/>).
/// </summary>
/// <remarks>
/// A filter rather than an override on the hub itself: the hub lives in the legacy
/// project and knows nothing about remote access, and a connection's standing is
/// decided by the middleware that ran during the handshake — this only reads what it
/// left behind.
/// </remarks>
public sealed class RemoteAccessHubFilter(RemoteConnectionRegistry registry, IRemoteAccessService remoteAccess)
    : IHubFilter
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
        }

        // Every options object for this machine's window and a paired device, as the
        // authorization filter lets them call anything; and for anyone while the server is
        // Unrestricted. Read after tracking, and from the mode as it is now: a mode change
        // hangs up on the unpaired connections tracked before it, so one tracked after it
        // must be judged by the new mode. No context means the middleware did not run, which
        // is not trusted.
        var readsAllOptions = remote is {IsLoopback: true} or {IsPaired: true} ||
                              remote != null && remoteAccess.GetEffectiveMode() == RemoteAccessMode.Unrestricted;
        await WebGuiOptionsAudience.AdmitAsync(context.Context, context.Hub.Groups, readsAllOptions);

        await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        registry.Forget(context.Context.ConnectionId);
        await next(context, exception);
    }
}
