namespace Bakabase.Modules.Downloader.Models;

public sealed record HttpDownloadRequest(string Url, string Directory)
{
    /// <summary>A single file name; when omitted, use the server name or the URL's last segment.</summary>
    public string? FileName { get; init; }
    /// <summary>Optional host-configured client for authentication, proxies or request headers.</summary>
    public string? HttpClientName { get; init; }
    /// <summary>Maximum simultaneous HTTP range connections for one file.</summary>
    public int ParallelConnections { get; init; } = 4;
    public int MaxRetries { get; init; } = 3;
    /// <summary>Per-transfer speed limit in bytes per second; zero means unlimited.</summary>
    public long MaximumBytesPerSecond { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(4);
}
