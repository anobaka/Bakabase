using System;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Bittorrent.Models;

namespace Bakabase.Service.Models.View;

public sealed record DownloadResultViewModel
{
    public int Id { get; init; }
    public int DownloadTaskId { get; init; }
    public string SourceKey { get; init; } = "";
    public string Name { get; init; } = "";
    public DownloadResultKind Kind { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public int? WorkflowRunId { get; init; }
    public WorkflowRunStatus? WorkflowStatus { get; init; }
    public string? WorkflowName { get; init; }
    public bool WorkflowIsBuiltin { get; init; }
    public int? AcquisitionTaskId { get; init; }
    public bool ContentsReady { get; init; }
    public string? ContentsDirectory { get; init; }
    public int? ResourceId { get; init; }
    public string? Error { get; init; }
    public bool FilterDidNotMatch { get; init; }
    public bool CanRetry { get; init; }
    public bool RequiresContentsDownload { get; init; }
    public string? BtDownloadId { get; init; }
    public BtDownloadStatus? BtStatus { get; init; }
    public string? BtStage { get; init; }
    public int? BtPercentage { get; init; }
    public long? BtCompletedBytes { get; init; }
    public long? BtTotalBytes { get; init; }
    public double? BtSpeedBytesPerSecond { get; init; }
    public int? WorkflowProgress { get; init; }
    public string? WorkflowProcess { get; init; }
    public bool CanStop { get; init; }
}
