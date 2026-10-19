namespace Bakabase.Modules.Bittorrent.Models;

public enum BtDownloadStatus
{
    Queued = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Completed = 5,
    Failed = 6
}
