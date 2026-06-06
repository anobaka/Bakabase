namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants
{
    public enum DownloaderStopBy
    {
        ManuallyStop = 1,
        AppendToTheQueue = 2,

        /// <summary>
        /// The task voluntarily yielded its slot back to the queue without failing or completing
        /// (e.g. ExHentai torrent-priority deferring a no-torrent task). Like
        /// <see cref="AppendToTheQueue"/> it stays eligible, but it also triggers the scheduler to
        /// immediately pick the next task.
        /// </summary>
        Defer = 3
    }
}