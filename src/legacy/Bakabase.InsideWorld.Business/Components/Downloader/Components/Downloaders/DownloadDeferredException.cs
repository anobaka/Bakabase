using System;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders
{
    /// <summary>
    /// Thrown by a downloader to voluntarily yield its download slot back to the queue without
    /// failing or completing the task. Used by ExHentai torrent-priority: a SingleWork task that
    /// turns out to have no torrent is deferred (and marked) so torrent-bearing tasks are processed
    /// first. <see cref="AbstractDownloader{TEnumTaskType}.Start"/> maps it to a
    /// <see cref="Abstractions.Models.Constants.DownloaderStopBy.Defer"/> stop.
    /// </summary>
    public class DownloadDeferredException : Exception
    {
        public DownloadDeferredException() : base("Download deferred to prioritize tasks with torrents.")
        {
        }
    }
}
