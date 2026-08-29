namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// The constants a remote client and this server must agree on before either
/// side can say anything else. Changing any of them is a breaking change for
/// every client in the wild.
/// </summary>
public static class RemoteAccessProtocol
{
    /// <summary>
    /// Version of the remote API contract. Bump when a client written against the
    /// previous version would misbehave — not for additive changes.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>DNS-SD service type this server advertises over mDNS.</summary>
    public const string MdnsServiceType = "_bakabase._tcp.local.";

    /// <summary>
    /// UDP port for the plain probe channel — the fallback for networks that drop
    /// multicast, and for platforms without an mDNS API. iOS clients never use it
    /// (raw broadcast needs an entitlement sideloaded apps cannot hold).
    /// </summary>
    public const int ProbePort = 33333;

    /// <summary>What a probing client sends.</summary>
    public const string ProbeRequest = "BAKABASE_DISCOVER_V1";

    /// <summary>Prefix of the reply, followed by a space and the JSON descriptor.</summary>
    public const string ProbeResponsePrefix = "BAKABASE_HERE_V1 ";
}
