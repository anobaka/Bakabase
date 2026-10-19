namespace Bakabase.Modules.HttpDownloads.Models;

public enum HttpDownloadStatus
{
    Queued = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Completed = 5,
    Failed = 6
}
