using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Controllers;
using DownloadTask = Bakabase.Modules.Downloader.Models.Tasks.DownloadTask;

namespace Bakabase.Service.Components.Workflow;

/// <summary>Inspection-only projection. A directory is not evidence of a completed download.</summary>
public static class WorkflowSourceRunOutputs
{
    public static void EnrichDownloadTasks(WorkflowSourceRunView view, IReadOnlyDictionary<string, DownloadTask> tasks)
    {
        var owned = view.DownloadTaskIds.Where(tasks.ContainsKey).Select(id => tasks[id]).ToList();
        if (owned.Count == 0) return;
        view.DownloadedBytes = owned.Sum(t => t.CompletedBytes ?? 0);
        view.TotalBytes = owned.All(t => t.TotalBytes != null) ? owned.Sum(t => t.TotalBytes!.Value) : null;
        view.SpeedBytesPerSecond = owned.Sum(t => t.SpeedBytesPerSecond ?? 0);
        foreach (var task in owned)
        {
            // A successful artifact reuse can retain an old task reference. Its past failure
            // is still task history, not a current blocker of the completed workflow.
            if (view.Run.Status != WorkflowRunStatus.Success) WorkflowSourceRunProblems.Add(view, task.Error);
            AddLocation(view, "download", task.OutputDirectory);
        }
    }

    public static void AddLocation(WorkflowSourceRunView view, string kind, string? directory, string? artifactId = null)
    {
        if (string.IsNullOrWhiteSpace(directory) && string.IsNullOrWhiteSpace(artifactId)) return;
        var locations = view.OutputLocations ??= [];
        if (locations.Any(l => l.Kind == kind && (artifactId != null
                ? l.ArtifactId == artifactId : l.Directory == directory))) return;
        locations.Add(new() {Kind = kind, Directory = directory, ArtifactId = artifactId, IsPlanned = true});
    }

    public static void CaptureRecordedMetadata(WorkflowSourceRunView view)
    {
        // Only explicitly published blocks, never a recursive walk of checkpoint payloads.
        var latest = view.Run.NodeExecutions.SelectMany(n => n.Outputs.Select(b => (Node: n, Block: b)))
            .OrderByDescending(x => x.Node.Id).GroupBy(x => x.Block.Key).Select(g => g.First().Block);
        foreach (var block in latest)
        {
            if (block.Kind == "artifact" && block.ArtifactId != null)
                AddLocation(view, "working", block.Directory, block.ArtifactId);
            else if (block.Kind == "task" && block.TaskKind == "download" && block.TaskId is { } id &&
                     !view.DownloadTaskIds.Contains(id)) view.DownloadTaskIds.Add(id);
        }
        // Old attempts may have been repaired. Only the current attempt's warnings describe
        // what is blocking this run now, while earlier artifact locations remain inspectable.
        if (view.Run.Status is WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted)
            foreach (var block in view.Run.NodeExecutions
                         .Where(n => view.Run.CurrentStepIndex == null || n.StepIndex == view.Run.CurrentStepIndex)
                         .OrderByDescending(n => n.Id).FirstOrDefault()?.Outputs ?? [])
                if (block.Kind == "warning") WorkflowSourceRunProblems.Add(view, block.Code);
    }

    public static async Task ResolveArtifactsAsync(IEnumerable<WorkflowSourceRunView> views,
        IArtifactOperationStore? artifacts, CancellationToken ct)
    {
        if (artifacts == null) return;
        var cache = new Dictionary<string, ArtifactRecord?>();
        foreach (var view in views)
        {
            var locations = view.OutputLocations ??= [];
            for (var i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location.ArtifactId is not { } id) continue;
                if (!cache.TryGetValue(id, out var artifact))
                {
                    // Get reads the existing ledger and rebases AppData paths. Resolve would
                    // hash files and update state, which a history query must not do.
                    try { artifact = await artifacts.GetArtifactAsync(id, ct); }
                    catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
                    { artifact = null; }
                    cache.Add(id, artifact);
                }
                if (artifact == null) continue;
                locations[i] = location with {Directory = artifact.RootDirectory,
                    Kind = location.Kind != "working" ? location.Kind : artifact.Kind switch
                    {"download" => "download", "processedFiles" => "processed", "publishedFiles" => "published", _ => "working"},
                    State = artifact.State.ToString().ToLowerInvariant(), FileCount = artifact.Manifest.Entries.Count,
                    TotalBytes = artifact.Manifest.Entries.Sum(e => e.Length), IsPlanned = false};
            }
            // A known artifact supersedes the same directory-only hint; unrelated planned
            // locations remain visible, without claiming that they contain any files.
            view.OutputLocations = locations.Where(l => !l.IsPlanned || l.ArtifactId != null ||
                !locations.Any(a => !a.IsPlanned && a.Kind == l.Kind && a.Directory == l.Directory))
                .DistinctBy(l => (l.Kind, l.ArtifactId, l.Directory)).ToList();
        }
    }
}
