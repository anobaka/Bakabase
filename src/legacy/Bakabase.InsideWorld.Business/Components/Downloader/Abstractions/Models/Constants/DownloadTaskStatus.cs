namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants
{
    public enum DownloadTaskStatus
    {
        Idle = 100,
        InQueue = 200,
        Starting = 300,
        Downloading = 400,
        Stopping = 500,
        Complete = 600,
        Failed = 700,
        Disabled = 800
    }
}
