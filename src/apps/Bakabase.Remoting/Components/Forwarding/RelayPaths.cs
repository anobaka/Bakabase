namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Paths a relay answers itself, under a prefix the server does not use.
/// </summary>
/// <remarks>
/// Nothing under <see cref="Prefix"/> is ever forwarded, so none of it can shadow one of
/// the server's routes or be shadowed by one later. What is mapped there depends on who
/// composed the relay: the legacy thin client answers its connect and pairing API, the
/// all-in-one answers the server switcher's.
/// </remarks>
public static class RelayPaths
{
    public const string Prefix = "/client";

    /// <summary>
    /// Where a relay with no usable server sends a window that asked for a page.
    /// </summary>
    /// <remarks>
    /// A path of its own rather than <c>/</c>, so the root always means one thing: hand
    /// this request to the server. There is simply nothing to hand it to, and the
    /// forwarder redirects here instead of answering a browser with a JSON refusal.
    /// </remarks>
    public const string ConnectPath = Prefix + "/connect-page";
}
