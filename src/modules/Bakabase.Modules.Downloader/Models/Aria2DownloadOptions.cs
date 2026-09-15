namespace Bakabase.Modules.Downloader.Models;

public sealed record Aria2DownloadOptions
{
    public string RpcUrl { get; init; } = "http://127.0.0.1:6800/jsonrpc";
    public string? Secret { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(4);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
}
