using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed record CloudDownloadTarget
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public List<CloudSource> Sources { get; init; } = [];
    public string? SelectedSourceId { get; init; }
    public string? AccountId { get; init; }
    public CloudRemoteEntry? RemoteEntry { get; init; }
    public int? DownloadTaskId { get; init; }
    public List<string> Files { get; init; } = [];
    public string? Problem { get; init; }
    public bool Excluded { get; init; }
    public string? TransportTaskId { get; init; }
    public CloudSource? SelectedSource => Sources.FirstOrDefault(s => s.Id == SelectedSourceId);
}

public sealed record DownloadWorkset : IWorkflowOutputSummary
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? Title { get; init; }
    public int? PostTaskId { get; init; }
    public int PostRevision { get; init; }
    public string OutputDirectory { get; init; } = "";
    public List<CloudDownloadTarget> Targets { get; init; } = [];
    public object ToWorkflowOutputSummary() => new {Id, Title, PostTaskId, Targets = Targets.Select(t =>
        new {t.Id, t.Title, t.SelectedSourceId, t.DownloadTaskId, FileCount = t.Files.Count, t.Problem})};
}

public sealed record CloudSelectionSignal
{
    public List<string> SourceIds { get; init; } = [];
    public string? AccountId { get; init; }
    public string? OutputDirectory { get; init; }
}
