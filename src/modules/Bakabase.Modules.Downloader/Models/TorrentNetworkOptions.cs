using System.Net;

namespace Bakabase.Modules.Downloader.Models;

/// <summary>A transfer-scoped snapshot. The default explicitly ignores system proxies.</summary>
public sealed record TorrentNetworkOptions
{
    public bool UseSystemProxy { get; init; }
    public Uri? ProxyUri { get; init; }
    public NetworkCredential? Credentials { get; init; }
}
