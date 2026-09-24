using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// The origin this device's own UI is served from — where "this device" in the server
/// switcher sends the window.
/// </summary>
/// <remarks>
/// <para>
/// It has to be exactly the origin the main window opened at start, not merely an address
/// the server answers on. The browser keys localStorage, IndexedDB and its cache to the
/// origin, and <c>localhost:34567</c> and <c>127.0.0.1:34567</c> are two different origins:
/// switching back to the second would look to the user like the app had forgotten every
/// setting it keeps in the browser. In a debug build it is not even the server's port at
/// all but the frontend dev server's.
/// </para>
/// <para>
/// So the host that opens the window reports what it opened (<see cref="Set"/>), and until
/// it has — a headless test host never does — the first address the server reported is
/// the best remaining guess, which is also what the host itself would have picked.
/// </para>
/// </remarks>
public sealed class RemoteConsoleLocalOrigin(AppContext? appContext = null)
{
    private volatile string? _origin;

    /// <summary>Records the address the main window was opened at. Any path or fragment is dropped.</summary>
    public void Set(string? windowAddress)
    {
        if (TryOrigin(windowAddress) is { } origin)
        {
            _origin = origin;
        }
    }

    /// <summary><c>scheme://host:port</c>, or null when nothing is known yet.</summary>
    public string? Origin => _origin ?? TryOrigin(appContext?.ApiEndpoint) ??
        TryOrigin(appContext?.ApiEndpoints.FirstOrDefault());

    /// <summary>
    /// Where the window should go to show this device, optionally at a path of its UI.
    /// Null when the origin is not known.
    /// </summary>
    public string? BuildUrl(string? path) =>
        Origin is { } origin ? origin + RelayUrls.NormalizePath(path) : null;

    private static string? TryOrigin(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        Uri.TryCreate(address.Replace("0.0.0.0", "localhost", StringComparison.Ordinal), UriKind.Absolute,
            out var uri) &&
        uri.Scheme is "http" or "https"
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}
