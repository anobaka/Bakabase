using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    // File registration alone does not own its containing folder. Only the server's
    // frozen, task-specific allocation, tied to the publication's run and plan revision,
    // authorizes pruning the empty publication root or its incidental Finder metadata.
    private async Task<bool> OwnsDeliveryDirectoryAsync(ArtifactRecord publication,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        if (!authorization.Consent.DeleteDeliveredFiles) return false;
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t =>
            t.Id == authorization.Consent.TaskId, ct);
        if (task == null) return false;
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b =>
            b.OperationId == publication.OperationId && b.OwnerKind == "workflow" &&
            b.Slot.EndsWith(":artifact.publish")).ToListAsync(ct);
        var runs = authorization.Consent.OwnerRunIds.ToArray();
        var sources = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s =>
            runs.Contains(s.WorkflowRunId) && s.Module == "acquisition" && s.EntityKind == "task").ToListAsync(ct);
        var owned = false;
        try
        {
            foreach (var binding in bindings)
            {
                if (!int.TryParse(binding.OwnerId, out var run) || !runs.Contains(run)) return false;
                var claims = sources.Where(s => s.WorkflowRunId == run).Select(s => (s.EntityId, s.Revision)).Distinct().ToArray();
                if (claims.Length != 1 || claims[0].EntityId != task.Id.ToString(CultureInfo.InvariantCulture) ||
                    !int.TryParse(claims[0].Revision, out var revision)) continue;
                AcquisitionPlan? plan;
                if (revision == task.PlanRevision)
                    plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
                else
                {
                    var history = JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(task.PlanHistoryJson ?? "[]", JsonSerializerOptions.Web);
                    var matching = history?.Where(p => p.Revision == revision).ToArray() ?? [];
                    plan = matching.Length == 1 ? matching[0].Plan : null;
                }
                if (plan?.DeliveryLayout is not { } layout || layout.TaskId != task.Id) continue;
                AcquisitionDeliveryDirectoryResolver.ValidateLayout(layout, plan.Parts.Select(p => p.Id).ToArray());
                var partId = binding.Slot[..^":artifact.publish".Length];
                if (!layout.PartDirectories.TryGetValue(partId, out var directory) ||
                    !ArtifactManifestFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(publication.RootDirectory)))) continue;
                if (plan.StageDirectories is { } stages)
                {
                    AcquisitionStageDirectoryResolver.Validate(stages, plan);
                    if (stages.TaskId != task.Id || !stages.Parts.TryGetValue(partId, out var part) ||
                        part.Delivery is not {Ownership: "exclusive", Evidence: "taskLayout"} stage ||
                        !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(stage.Directory), Path.GetFullPath(directory))) continue;
                }
                owned = true;
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException) { return false; }
        if (!owned) return false;

        // An outside registration can own an empty directory even when no file paths
        // intersect. Compare compact root records, not every artifact's large manifest.
        var current = await db.Artifacts.AsNoTracking().Where(a => a.Id == publication.Id)
            .Select(a => new {a.RootPath, a.RootIsAppData}).SingleOrDefaultAsync(ct);
        if (current == null) return false;
        var releasable = (await authorization.ReleasableViewIdsAsync(ct)).ToArray();
        var roots = await db.Artifacts.AsNoTracking().Where(a => a.Id != publication.Id &&
            !releasable.Contains(a.Id) && a.State != ArtifactLocationState.Retired &&
            a.State != ArtifactLocationState.Quarantined && a.RootIsAppData == current.RootIsAppData)
            .Select(a => a.RootPath).ToListAsync(ct);
        var prefix = current.RootPath.TrimEnd('/', '\\');
        // Ancestor roots may register this empty directory without any intersecting
        // file entry. Conservatively retain it instead of loading their manifests.
        return !roots.Any(path => SameOrBelow(path.TrimEnd('/', '\\'), prefix) ||
            SameOrBelow(prefix, path.TrimEnd('/', '\\')));

        static bool SameOrBelow(string path, string parent) => ArtifactManifestFiles.PathComparer.Equals(path, parent) ||
            path.Length > parent.Length && path[parent.Length] is '/' or '\\' &&
            ArtifactManifestFiles.PathComparer.Equals(path[..parent.Length], parent);
    }

    private static IReadOnlyCollection<string> DeliveryDirectories(ArtifactRecord artifact, bool includeRegistered = true)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifact.RootDirectory));
        var directories = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var starts = ArtifactManifestFiles.ResolveFiles(root, artifact.Manifest).Select(Path.GetDirectoryName)
            .Concat(includeRegistered ? ArtifactManifestFiles.ResolveDirectories(root, artifact.Manifest) : []);
        foreach (var start in starts)
            for (var directory = start; directory != null && ArtifactCleanupPathReferences.StrictlyUnder(directory, root);
                 directory = Path.GetDirectoryName(directory)) directories.Add(directory);
        return directories;
    }

    private static bool RemoveEmptyDeliveryDirectories(ArtifactRecord artifact, bool removeOwnedRoot, CancellationToken ct,
        IReadOnlyList<string>? currentDirectories = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifact.RootDirectory));
        if (ArtifactManifestFiles.PathComparer.Equals(root, Path.GetPathRoot(root))) return !removeOwnedRoot;
        var directories = DeliveryDirectories(artifact, includeRegistered: removeOwnedRoot)
            .Concat(removeOwnedRoot ? currentDirectories ?? [] : []).Distinct(ArtifactManifestFiles.PathComparer)
            .Where(path => ArtifactCleanupPathReferences.StrictlyUnder(path, root)).ToArray();
        foreach (var directory in (removeOwnedRoot ? directories.Append(root) : directories).OrderByDescending(p => p.Length))
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(directory);
            try
            {
                // Never discover or recursively delete unknown children. rmdir also
                // refuses additions racing this observation, and never reaches Acquired.
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, recursive: false);
            }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }
        return !removeOwnedRoot || Path.GetDirectoryName(root) is { } parent &&
            ArtifactCleanupAbsence.Probe(parent, root, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent;
    }
}
