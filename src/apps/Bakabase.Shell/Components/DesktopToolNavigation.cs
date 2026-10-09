using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Shell.Components;

/// <summary>Keeps an activation through startup; later activations always use the original local server.</summary>
public sealed class DesktopToolNavigation(Action<string> navigate)
{
    private readonly object _gate = new();
    private string? _localAddress;
    private string? _pendingRoute;

    public void Request(string link)
    {
        var route = DesktopToolLink.GetRoute(link);
        if (route == null) return;
        string? address;
        lock (_gate)
        {
            address = _localAddress;
            if (address == null) _pendingRoute = route;
        }
        if (address != null) navigate(DesktopToolLink.LocalPage(address, route));
    }

    public void Ready(string localAddress)
    {
        // Validate the host's address even when there is no pending request.
        _ = DesktopToolLink.LocalPage(localAddress, "/file-processor");
        string? route;
        lock (_gate)
        {
            _localAddress ??= localAddress;
            route = _pendingRoute;
            _pendingRoute = null;
            localAddress = _localAddress;
        }
        if (route != null) navigate(DesktopToolLink.LocalPage(localAddress, route));
    }
}
