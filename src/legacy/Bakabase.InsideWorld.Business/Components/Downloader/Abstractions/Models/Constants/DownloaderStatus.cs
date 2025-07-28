namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants
{
    public enum DownloaderStatus
    {
        JustCreated = 0,
        Starting = 100,
        Downloading = 200,
        Complete = 300,
        Failed = 400,
        Stopping = 500,
        Stopped = 600
    }
}
