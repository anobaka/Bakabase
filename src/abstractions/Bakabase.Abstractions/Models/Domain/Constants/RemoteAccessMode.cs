namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// How Bakabase treats requests that do not come from the loopback interface.
/// Loopback requests (the desktop app's own WebView, a browser on the host) are
/// never affected by this setting.
/// </summary>
public enum RemoteAccessMode
{
    /// <summary>
    /// Every non-loopback request is rejected. This is the desktop default: the
    /// HTTP server still binds all interfaces, but nothing on the LAN can reach it.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Non-loopback requests must present a paired device's token, and may only
    /// reach endpoints explicitly marked as remote-accessible.
    /// </summary>
    Authenticated = 1,

    /// <summary>
    /// Non-loopback requests are passed through unchecked — the whole API is open
    /// to anyone who can reach the port. This is what Bakabase has always done, and
    /// it stays the Docker default so containerized installs keep working; it is not
    /// safe on an untrusted network.
    /// </summary>
    Open = 2
}
