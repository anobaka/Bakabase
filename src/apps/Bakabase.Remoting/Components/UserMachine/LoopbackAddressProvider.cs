namespace Bakabase.Remoting.Components.UserMachine;

/// <summary>
/// Where this client's own forwarding layer is listening, so it can hand a player a URL
/// that comes back through itself.
/// </summary>
public interface ILoopbackAddressProvider
{
    /// <summary>
    /// A URL a player on this machine can open for a file the server holds. It goes
    /// through the forwarding layer, which signs it — so the player needs no credentials
    /// and the device key never leaves this process.
    /// </summary>
    string BuildRawFileUrl(string serverPath);

    /// <summary>A URL on this client for any server path, e.g. a userscript to install.</summary>
    string BuildUrl(string pathAndQuery);
}

/// <summary>
/// Builds URLs that come back through this client's own forwarding layer.
/// </summary>
/// <remarks>
/// Handing a player one of these is what lets an unmapped library play at all: the
/// player opens a plain loopback URL with no credentials, and the forwarding layer signs
/// and relays it. The device key stays in this process, where a player could never have
/// held it anyway.
/// </remarks>
public sealed class LoopbackAddressProvider(int port) : ILoopbackAddressProvider
{
    public string BaseAddress { get; } = $"http://127.0.0.1:{port}";

    public string BuildRawFileUrl(string serverPath) =>
        $"{BaseAddress}/file/raw?fullname={Uri.EscapeDataString(serverPath)}";

    public string BuildUrl(string pathAndQuery) =>
        $"{BaseAddress}/{pathAndQuery.TrimStart('/')}";
}
