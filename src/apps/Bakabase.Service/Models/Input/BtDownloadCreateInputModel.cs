namespace Bakabase.Service.Models.Input;

/// <summary>Manual downloads cannot claim workflow or downloader ownership.</summary>
public sealed class BtDownloadCreateInputModel
{
    public string? Name { get; set; }
    public string? MagnetUri { get; set; }
    public string? TorrentUrl { get; set; }
}
