namespace Bakabase.Modules.Downloader.Models;

public sealed record HttpTransferProgress
{
    public string Stage { get; init; } = "Downloading";
    public int? Percentage { get; init; }
    public long CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
}
