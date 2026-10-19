namespace Bakabase.Modules.Downloader.Models;

public sealed record TorrentTransferProgress
{
    public required string Stage { get; init; }
    public int Percentage { get; init; }
    public long? CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public int? Peers { get; init; }
}
