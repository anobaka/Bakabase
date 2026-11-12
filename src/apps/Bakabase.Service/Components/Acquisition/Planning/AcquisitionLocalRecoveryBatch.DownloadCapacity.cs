using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionLocalRecoveryBatchService
{
    private async Task<string[]> DownloadDirectoriesAsync(WorkflowRunDbModel run, CancellationToken ct)
    {
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)
            ?? throw new ArgumentException("RECOVERY_CHECKPOINT_UNSUPPORTED");
        var targets = workset.Targets.Where(t => !t.Excluded).ToArray();
        var ids = targets.Where(t => t.DownloadTaskId != null).Select(t => t.DownloadTaskId!.Value).Distinct().ToArray();
        var rows = await db.DownloadTasks.AsNoTracking().Where(d => ids.Contains(d.Id))
            .Select(d => new {d.Id, d.DownloadPath}).ToDictionaryAsync(d => d.Id, ct);
        var result = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var target in targets)
        {
            // A retained download's actual destination wins over an old workset/prompt root.
            // A missing referenced task is uncertain; do not invent a replacement path.
            var path = target.DownloadTaskId is { } id
                ? rows.GetValueOrDefault(id)?.DownloadPath
                : target.DownloadDirectory ?? workset.OutputDirectory;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("RECOVERY_LOCAL_STORAGE_REQUIRED");
            result.Add(path);
        }
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(workset.OutputDirectory) && Path.IsPathFullyQualified(workset.OutputDirectory))
            result.Add(workset.OutputDirectory);
        if (result.Count == 0) throw new ArgumentException("RECOVERY_LOCAL_STORAGE_REQUIRED");
        return result.ToArray();
    }

    private async Task<Checked> DownloadCapacityAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel run,
        AcquisitionSizeEstimate estimate, CancellationToken ct, Func<string?, Task>? guard)
    {
        try
        {
            var paths = await DownloadDirectoriesAsync(run, ct);
            foreach (var path in paths)
            {
                storage.EnsurePathAllowed(path); ArtifactManifestFiles.RejectLinks(path);
                if (guard != null) await guard(path);
            }
            // This is a read-only current-stage capacity display, not recovery eligibility.
            // Do not add historical/future publication directories to the download volume.
            return new(new(task.Id, task.Title, run.Id, false, "LOCAL_BATCH_READY", estimate.Bytes, true,
                    "workflow", paths.First(), null, CanOverrideStorage: true),
                new(AcquisitionRecoveryService.Version(task, run), "{}", "workflow", "download-capacity",
                    paths, [], true, CanOverrideStorage: true));
        }
        catch (UnauthorizedAccessException)
        {return new(new(task.Id, task.Title, run.Id, false, "LOCAL_BATCH_PATH_FORBIDDEN", estimate.Bytes, true, null, null, null));}
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
        {return new(new(task.Id, task.Title, run.Id, false, "LOCAL_BATCH_CAPACITY_UNKNOWN", estimate.Bytes, true, null, null, null));}
    }
}
