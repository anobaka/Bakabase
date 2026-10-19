namespace Bakabase.Modules.Downloader.Models;

/// <summary>Exactly one source is required. The caller owns destination and retry identity.</summary>
public sealed record TorrentDownloadRequest
{
    public string? MagnetUri { get; init; }
    public byte[]? Metadata { get; init; }
    public string? TorrentUrl { get; init; }
    public required string WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(4);
    /// <summary>Legacy workflows use a torrent-data child; managed transfers supply their final containing directory.</summary>
    public bool UseWorkingDirectoryAsDestination { get; init; }
}
