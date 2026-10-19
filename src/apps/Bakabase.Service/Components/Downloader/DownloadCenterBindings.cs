using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.HttpDownloads.Models;

namespace Bakabase.Service.Components.Downloader;

public static class DownloadCenterBindings
{
    public static DownloadTaskBinding For(BtDownloadJob job) => new()
    {
        Provider = "bt", ProviderTaskId = job.Id, ParentId = job.DownloadTaskId is { } parent ? $"site:{parent}" : null,
        SiteTaskId = job.DownloadTaskId, OriginKind = job.SourceKind, OriginId = job.SourceId,
        WorkflowRunId = job.WorkflowRunId, WorkflowDefinitionId = job.WorkflowDefinitionId,
        DownloadResultId = job.DownloadResultId, CreatedAt = job.CreatedAt
    };

    public static DownloadTaskBinding For(HttpDownloadJob job) => new()
    {
        Provider = "http", ProviderTaskId = job.Id, ParentId = job.DownloadTaskId is { } parent ? $"site:{parent}" : null,
        SiteTaskId = job.DownloadTaskId, OriginKind = job.SourceKind, OriginId = job.SourceId,
        WorkflowRunId = job.WorkflowRunId, WorkflowDefinitionId = job.WorkflowDefinitionId,
        DownloadResultId = job.DownloadResultId, CreatedAt = job.CreatedAt
    };
}
