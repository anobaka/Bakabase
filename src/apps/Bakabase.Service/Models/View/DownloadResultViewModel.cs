using System;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

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
}
