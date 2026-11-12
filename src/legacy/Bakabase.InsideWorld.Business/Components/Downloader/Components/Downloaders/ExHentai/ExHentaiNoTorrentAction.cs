namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

/// <summary>What to do after the gallery confirms it has no torrent. Null retains legacy task behavior.</summary>
public enum ExHentaiNoTorrentAction
{
    DirectDownload = 1,
    Fail = 2
}
