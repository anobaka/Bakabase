namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// How Bakabase treats requests that do not come from the loopback interface.
/// Loopback requests (the desktop app's own WebView, a browser on the host) are
/// never affected by this setting.
/// <para>
/// There is no per-device identity behind these modes: anything that can reach
/// the port is treated as trusted. The distinction the modes draw is not who is
/// calling, but whether actions that physically happen on the host machine —
/// launching a player, opening a folder — should be reachable from a device that
/// cannot see the host's screen.
/// </para>
/// </summary>
public enum RemoteAccessMode
{
    /// <summary>
    /// Every non-loopback request is rejected. This is the desktop default: the
    /// HTTP server still binds all interfaces, but nothing on the LAN can reach it.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Other devices may browse the catalog and stream media. Endpoints that act on
    /// the host machine are refused — not as a permission check, but because their
    /// effect would land on a screen the caller cannot see.
    /// </summary>
    Enabled = 1,

    /// <summary>
    /// Everything is reachable, host-acting endpoints included. This is what
    /// Bakabase has always done, and it stays the Docker default: there the browser
    /// is always "remote", and the person using it is the operator.
    /// </summary>
    Unrestricted = 2
}
