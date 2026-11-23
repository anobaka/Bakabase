using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Narrows a complete, server-owned history preview. Client paths only choose an existing
/// group. Mutation still uses the original receipts, manifests, policies, ownership and reference guards.</summary>
internal static class AcquisitionHistoryCleanupSelection
{
    internal const string Invalid = "ACQUISITION_DELETE_DIRECTORY_SELECTION_INVALID";
    internal const string SkippedTask = "CLEANUP_DIRECTORY_NOT_SELECTED";

    internal static (string Fingerprint, Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> Plans) Choose(
        AcquisitionTaskDeletePreviewStore.Entry entry, IReadOnlyDictionary<int, IReadOnlyList<string>> directories)
    {
        if (!entry.Preview.RetryRetainedFiles || !entry.Preview.CleanupFiles && !entry.Preview.DeleteDeliveredFiles ||
            entry.CleanupPlans == null || directories.Count == 0 || directories.Count > entry.Preview.Rows.Count)
            throw new ArgumentException(Invalid);
        var selected = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>();
        foreach (var (task, paths) in directories)
        {
            var row = entry.Preview.Rows.SingleOrDefault(r => r.TaskId == task);
            if (row == null || paths == null) throw new ArgumentException(Invalid);
            if (paths.Count == 0) continue;
            if (!row.CanDelete || !entry.CleanupPlans.TryGetValue(task, out var plan) || !plan.RetryRetainedFiles ||
                !paths.All(path => path != null && Path.IsPathFullyQualified(path)) ||
                paths.Count != paths.Distinct(StringComparer.Ordinal).Count()) throw new ArgumentException(Invalid);
            selected.Add(task, Narrow(plan, paths));
        }
        if (selected.Count == 0) throw new ArgumentException(Invalid);
        var fingerprint = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(directories.Where(p => p.Value.Count > 0)
            .OrderBy(p => p.Key).Select(p => new {p.Key, Paths = p.Value.Order(StringComparer.Ordinal).ToArray()})));
        return (fingerprint, selected);
    }

    private static AcquisitionTaskDeletionCleanupService.Plan Narrow(AcquisitionTaskDeletionCleanupService.Plan plan,
        IReadOnlyList<string> requested)
    {
        var paths = requested.ToHashSet(StringComparer.Ordinal);
        var groups = plan.Summary.Groups.Where(g => g.Eligible && paths.Contains(g.Directory)).ToArray();
        if (!paths.SetEquals(groups.Select(g => g.Directory))) throw new ArgumentException(Invalid);
        var ids = groups.Select(g => g.ArtifactId).ToHashSet(StringComparer.Ordinal);
        var other = plan.Summary.Groups.Except(groups).ToArray();
        var remaining = other.Where(g => g.ReasonCode != "AlreadyAbsent").ToArray();
        bool Covers(string path) => requested.Any(root => ArtifactManifestFiles.PathComparer.Equals(path, root) ||
            ArtifactCleanupPathReferences.StrictlyUnder(path, root));
        // A wide stage and a private child of another group can identify the same physical files.
        // Never express a partial selection by deleting the parent of an unselected directory.
        if (other.Any(g => ids.Contains(g.ArtifactId)) ||
            other.Any(g => !string.IsNullOrEmpty(g.Directory) && (Covers(g.Directory) ||
                requested.Any(root => ArtifactCleanupPathReferences.StrictlyUnder(root, g.Directory)))) ||
            groups.SelectMany(g => g.Files).Any(f => !Covers(f.Path)) ||
            other.SelectMany(g => g.Files).Any(f => Covers(f.Path)))
            throw new ArgumentException(Invalid);

        var files = AcquisitionTaskDeletionCleanupService.Unique(groups.SelectMany(g => g.Files));
        return plan with
        {
            Summary = plan.Summary with {Groups = groups, FileCount = files.Count, Bytes = files.Sum(f => f.Bytes),
                IdentifiedFileCount = files.Count, IdentifiedBytes = files.Sum(f => f.Bytes), SizeIncomplete = groups.Any(g => g.SizeUnknown)},
            Candidates = plan.Candidates.Where(c => ids.Contains(c.ArtifactId)).ToArray(),
            AlreadyAbsent = plan.AlreadyAbsent?.Where(c => ids.Contains(c.ArtifactId)).ToArray(),
            DeliveredFiles = plan.DeliveredFiles?.Where(c => ids.Contains(c.ArtifactId)).ToArray(),
            AbandonedInputs = plan.AbandonedInputs?.Where(c => ids.Contains(c.Id)).ToArray(),
            CapturedInputs = plan.CapturedInputs?.Where(c => ids.Contains(c.ArtifactId)).ToArray(),
            StageDirectories = plan.StageDirectories?.Where(c => ids.Contains(c.Id)).ToArray(),
            UnselectedDirectories = remaining.Select(g => g.Directory).Where(d => !string.IsNullOrEmpty(d))
                .Distinct(ArtifactManifestFiles.PathComparer).ToArray(),
            UnselectedScopes = remaining.Aggregate(AcquisitionCleanupScopes.None, (scope, g) => scope | g.Scopes)
        };
    }
}
