using System.Net;

namespace Bakabase.Modules.Downloader.Models;

/// <summary>A transfer-scoped snapshot. Null proxy means explicit direct access, never system proxy.</summary>
public sealed record TorrentNetworkOptions
{
    public Uri? ProxyUri { get; init; }
    public NetworkCredential? Credentials { get; init; }
}
