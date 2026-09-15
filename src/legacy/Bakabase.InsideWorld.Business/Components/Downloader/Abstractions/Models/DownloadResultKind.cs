namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;

/// <summary>What a source obtained, before any optional workflow processes it.</summary>
public enum DownloadResultKind
{
    TorrentMetadata = 1,
    LocalFiles = 2
}
