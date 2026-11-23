using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionCurrentDirectoryReceipt(int Version, int TaskId, string PartId, int Revision,
    int PreviousRunId, string CaptureId, AcquisitionPlanArtifactInput PriorInput, string Directory,
    string InputFingerprint, string Fingerprint, IReadOnlyList<OwnedWorkspaceDirectory> ExcludedWorkspaces,
    ArtifactDirectoryProvenance? DirectoryProvenance = null);

/// <summary>A new, read-only input snapshot of an explicitly selected task directory. Never edits an old receipt.</summary>
internal static partial class AcquisitionCurrentDirectoryFiles
{
    internal const string Kind = "currentFiles";
    internal const string OperationKind = "files.captureCurrentDirectory";
    internal const int MaximumEntries = 10000;
    private const int MaximumDeliveryEntries = 100000;
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;
    private static string Slot(AcquisitionCurrentDirectoryReceipt receipt) =>
        $"{receipt.PartId}:current-files:{receipt.Revision}:{receipt.CaptureId}";
    private static bool Overlap(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(a, b) ||
        ArtifactCleanupPathReferences.StrictlyUnder(a, b) || ArtifactCleanupPathReferences.StrictlyUnder(b, a);

    internal static async Task<bool> CanReadAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces, int taskId, int runId, string partId,
        ArtifactRecord artifact, CancellationToken ct, int? activeRunId = null)
    {
        try
        {
            await RequireSourceAsync(db, store, storage, workspaces, taskId, runId, partId, artifact, ct, activeRunId: activeRunId);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return false; }
    }

    private static async Task RequireSourceAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces, int taskId, int runId, string partId,
        ArtifactRecord source, CancellationToken ct, string? ignoredArtifactId = null, int? activeRunId = null)
    {
        if (source.Kind is not ("download" or "processedFiles" or "publishedFiles" or Kind) ||
            source.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired)
            throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_UNAVAILABLE");
        _ = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, store, taskId, partId,
            new(taskId, runId, source.Id, source.OperationId, source.Fingerprint), ct, currentDirectorySource: true);
        storage.EnsurePathAllowed(source.RootDirectory);
        ArtifactManifestFiles.RejectLinks(source.RootDirectory);
        if (!File.GetAttributes(source.RootDirectory).HasFlag(FileAttributes.Directory))
            throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_UNAVAILABLE");
        var allowedWaitingRun = activeRunId ?? runId;
        var checkedOperations = new HashSet<string>(StringComparer.Ordinal);
        async Task CheckOperation(string operationId)
        {
            if (!checkedOperations.Add(operationId)) return;
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == operationId).ToListAsync(ct);
            if (bindings.Count == 0) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_SHARED");
            foreach (var binding in bindings)
            {
                if (binding.OwnerKind != "workflow" || !int.TryParse(binding.OwnerId, out var owner) ||
                    !binding.Slot.StartsWith(partId + ":", StringComparison.Ordinal) ||
                    !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, owner, taskId, ct))
                    throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_SHARED");
                var boundRun = await db.WorkflowRuns.AsNoTracking().Where(r => r.Id == owner)
                    .Select(r => new {r.Status, r.PendingSignalJson}).SingleOrDefaultAsync(ct);
                if (boundRun == null || boundRun.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending ||
                    boundRun.Status == WorkflowRunStatus.Waiting && owner != allowedWaitingRun &&
                    (boundRun.PendingSignalJson != null || !await IsSupersededWaitingAsync(db, taskId, partId, owner, allowedWaitingRun, ct)))
                    throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_SHARED");
            }
        }
        await CheckOperation(source.OperationId);
        // Exact task ownership cannot be inferred from a common parent directory. Another
        // registered part/task under or above this root keeps directory discovery disabled.
        var records = await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired &&
            a.Id != source.Id && a.Id != ignoredArtifactId)
            .Select(a => new {a.Id, a.OperationId, a.RootPath, a.RootIsAppData, a.State, a.Fingerprint}).ToListAsync(ct);
        var relocations = workspaces?.ReadRelocations();
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            string directory;
            if (workspaces != null)
            {
                directory = workspaces.ResolveStoredArtifactRoot(record.RootPath, record.RootIsAppData, relocations);
                // A quarantined AppData location no longer identifies an original directory.
                if (record.RootIsAppData && record.State == ArtifactLocationState.Quarantined) continue;
            }
            else if (record.RootIsAppData)
                directory = await store.GetArtifactDirectoryAsync(record.Id, ct)
                    ?? throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_SHARED");
            else directory = Path.GetFullPath(record.RootPath);
            if (!Overlap(source.RootDirectory, directory)) continue;
            if (ArtifactCleanupPathReferences.StrictlyUnder(source.RootDirectory, directory))
            {
                // An artifact's manifest base may be a shared parent (for example, a
                // composition of sibling downloads). It claims its exact registered
                // leaves and intentional empty directories, not every child of that base.
                // Only a complete, structurally valid producer receipt can narrow it.
                // Only an ancestor needs its manifest. Refuse a changed location or receipt
                // between the root scan and the targeted read.
                var manifestJson = await db.Artifacts.AsNoTracking().Where(a => a.Id == record.Id &&
                    a.OperationId == record.OperationId && a.RootPath == record.RootPath && a.RootIsAppData == record.RootIsAppData &&
                    a.Fingerprint == record.Fingerprint && a.State == record.State).Select(a => a.ManifestJson).SingleOrDefaultAsync(ct);
                if (manifestJson == null) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_SHARED");
                var manifest = JsonSerializer.Deserialize<ArtifactManifest>(manifestJson, Json);
                var producer = await store.GetOperationAsync(record.OperationId, ct);
                if (manifest == null || manifest.Fingerprint != record.Fingerprint ||
                    producer is not {State: ArtifactOperationState.Completed} || producer.OutputArtifactId != record.Id)
                    throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_SHARED");
                var claimed = ArtifactManifestFiles.ResolveFiles(directory, manifest)
                    .Concat(ArtifactManifestFiles.ResolveDirectories(directory, manifest));
                if (!claimed.Any(path => Overlap(source.RootDirectory, path))) continue;
            }
            await CheckOperation(record.OperationId);
        }
    }

    private static async Task<bool> IsSupersededWaitingAsync(BakabaseDbContext db, int taskId, string partId,
        int historicalRunId, int currentRunId, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.DeletedAt == null && t.WorkflowRunId == currentRunId)
            .Select(t => new {t.PlanRevision, t.PlanHistoryJson}).SingleOrDefaultAsync(ct);
        if (task?.PlanHistoryJson == null) return false;
        var revisions = await db.Set<Bakabase.Modules.Workflow.Abstractions.Models.Db.WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == historicalRunId &&
                s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == taskId.ToString())
            .Select(s => s.Revision).Distinct().ToArrayAsync(ct);
        if (revisions is not [{ } old] || !int.TryParse(old, out var revision) || revision < 1 || revision >= task.PlanRevision) return false;
        var history = JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(task.PlanHistoryJson, Json);
        return history?.Any(r => r.Revision > revision && r.Revision <= task.PlanRevision && r.PreviousRunId == historicalRunId &&
            r.Plan.Parts.Any(p => p.Id == partId && p.ContinueFrom is { } input && input.TaskId == taskId &&
                input.PreviousRunId == historicalRunId)) == true;
    }

    internal static async Task<ArtifactRecord> CaptureAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces, int taskId, int revision, int runId,
        string partId, ArtifactRecord source, Func<string?, Task>? guardDirectory, CancellationToken ct, int? sourceRunId = null)
    {
        var sourceRun = sourceRunId ?? runId;
        await RequireSourceAsync(db, store, storage, workspaces, taskId, sourceRun, partId, source, ct, activeRunId: runId);
        if (guardDirectory != null) await guardDirectory(source.RootDirectory);
        var exclusions = workspaces == null ? [] : await workspaces.OwnedDirectoriesUnderAsync(source.RootDirectory, ct);
        var selection = await EnumerateAsync(source.RootDirectory, exclusions, workspaces, ct);
        foreach (var path in selection.Files.Concat(selection.Directories)) storage.EnsurePathAllowed(path);
        var captureId = Guid.NewGuid().ToString("N");
        var prior = new AcquisitionPlanArtifactInput(taskId, sourceRun, source.Id, source.OperationId, source.Fingerprint);
        var seed = Seed(taskId, revision, runId, partId, captureId, prior, source.RootDirectory, exclusions);
        var receipt = new AcquisitionCurrentDirectoryReceipt(1, taskId, partId, revision, runId, captureId, prior,
            source.RootDirectory, seed, "", exclusions);
        var provenance = await AcquisitionCurrentDirectoryProvenance.ReadAsync(db, store, taskId, partId, prior, ct);
        var current = await store.AdoptAsync(new(ArtifactManifestFiles.Hash("current-directory:" + seed),
                "acquisition.current-directory:" + seed, OperationKind, seed),
            new(source.RootDirectory, selection.Files, Kind, false, [source.Id], Directories: selection.Directories,
                CreateMetadata: manifest => JsonSerializer.Serialize(receipt with {Fingerprint = manifest.Fingerprint,
                    DirectoryProvenance = AcquisitionCurrentDirectoryProvenance.Project(source.Manifest, provenance, manifest)}, Json)),
            "acquisition.current-directory:" + taskId, ct);
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(), Slot(receipt), current.OperationId, ct))
            throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
        await RequireSourceAsync(db, store, storage, workspaces, taskId, sourceRun, partId, source, ct,
            // The freshly registered view has not yet received its task binding.
            ignoredArtifactId: current.Id, activeRunId: runId);
        await ValidateSelectionAsync(current, workspaces, ct);
        return current;
    }

    internal static async Task<bool> IsOwnedAsync(BakabaseDbContext db, IArtifactOperationStore store,
        ArtifactRecord artifact, int taskId, int runId, string partId, CancellationToken ct)
    {
        if (artifact.Kind != Kind || artifact.Managed || artifact.MetadataJson == null) return false;
        try
        {
            if (JsonSerializer.Deserialize<AcquisitionStageDirectoryCaptureReceipt>(artifact.MetadataJson, Json) is {Version: 2} stage)
                return await IsStageOwnedAsync(db, store, artifact, stage, taskId, runId, partId, ct);
            var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(artifact.MetadataJson, Json);
            if (receipt is not {Version: 1} || receipt.TaskId != taskId || receipt.PartId != partId ||
                receipt.PriorInput == null || receipt.ExcludedWorkspaces == null || receipt.Revision <= 1 || !Guid.TryParseExact(receipt.CaptureId, "N", out _) ||
                receipt.PriorInput.TaskId != taskId ||
                receipt.Fingerprint != artifact.Fingerprint || artifact.Manifest.Fingerprint != artifact.Fingerprint ||
                !ArtifactManifestFiles.PathComparer.Equals(receipt.Directory, artifact.RootDirectory) ||
                !artifact.ParentArtifactIds.SequenceEqual([receipt.PriorInput.ArtifactId]) ||
                !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, receipt.PreviousRunId, taskId, ct) ||
                !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, receipt.PriorInput.PreviousRunId, taskId, ct) ||
                runId != receipt.PreviousRunId && !await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(db, runId, partId, artifact.OperationId, ct) ||
                !await db.ArtifactOperationBindings.AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == receipt.PreviousRunId.ToString() &&
                    b.Slot == Slot(receipt) && b.OperationId == artifact.OperationId, ct)) return false;
            var parent = await store.GetArtifactAsync(receipt.PriorInput.ArtifactId, ct);
            var operation = await store.GetOperationAsync(artifact.OperationId, ct);
            return parent?.OperationId == receipt.PriorInput.OperationId && parent.Fingerprint == receipt.PriorInput.Fingerprint &&
                operation is {Kind: OperationKind, State: ArtifactOperationState.Completed} && operation.OutputArtifactId == artifact.Id &&
                operation.InputFingerprint == receipt.InputFingerprint &&
                receipt.InputFingerprint == Seed(receipt.TaskId, receipt.Revision, receipt.PreviousRunId, receipt.PartId,
                    receipt.CaptureId, receipt.PriorInput, receipt.Directory, receipt.ExcludedWorkspaces);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { return false; }
    }

    private static string Seed(int taskId, int revision, int runId, string partId, string captureId,
        AcquisitionPlanArtifactInput prior, string rootDirectory, IReadOnlyList<OwnedWorkspaceDirectory> exclusions) =>
        ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {taskId, revision, runId, partId, captureId, prior,
            RootDirectory = rootDirectory, exclusions}, Json));

    internal static async Task ValidateSelectionAsync(ArtifactRecord artifact, FileProcessingWorkspaceResolver? workspaces, CancellationToken ct)
    {
        var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(artifact.MetadataJson!, Json)
            ?? throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
        var frozenDirectories = ArtifactManifestFiles.ResolveDirectories(artifact.RootDirectory, artifact.Manifest);
        IReadOnlyList<OwnedWorkspaceDirectory> exclusions = receipt.ExcludedWorkspaces;
        if (workspaces != null)
        {
            // Processing can create its private workspace after this input was captured.
            // Its persisted locator and library owner marker prove that it is system work,
            // not a user-added resource. Never retroactively hide a previously captured path.
            var newlyOwned = await workspaces.OwnedDirectoriesUnderAsync(artifact.RootDirectory, ct);
            exclusions = receipt.ExcludedWorkspaces.Concat(newlyOwned.Where(proof =>
                    !artifact.Files.Concat(frozenDirectories).Any(path => Overlap(path, proof.Directory))))
                .DistinctBy(proof => proof.Directory, ArtifactManifestFiles.PathComparer).ToArray();
        }
        var limit = IsDeliveryOnlyStageCapture(artifact) ? MaximumDeliveryEntries : MaximumEntries;
        var now = await EnumerateAsync(artifact.RootDirectory, exclusions, workspaces, ct, limit);
        if (!now.Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(artifact.Files) ||
            !now.Directories.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(frozenDirectories))
            throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
    }

    private sealed record Selection(IReadOnlyList<string> Files, IReadOnlyList<string> Directories);
    private static async Task<Selection> EnumerateAsync(string root, IReadOnlyList<OwnedWorkspaceDirectory> exclusions,
        FileProcessingWorkspaceResolver? workspaces, CancellationToken ct, int maximumEntries = MaximumEntries)
    {
        if (exclusions == null || exclusions.Count > 10000) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
        var excluded = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var proof in exclusions)
        {
            if (proof == null || !ArtifactCleanupPathReferences.StrictlyUnder(proof.Directory, root))
                throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
            if (!Directory.Exists(proof.Directory)) continue;
            if (workspaces == null || !await workspaces.MatchesOwnedDirectoryAsync(proof, ct))
                throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
            excluded.Add(proof.Directory);
        }
        var files = new List<string>(); var empty = new List<string>();
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0)); var count = 1;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(directory.Path);
            if (directory.Depth > 128) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_TOO_LARGE");
            var hasEntries = false;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                hasEntries = true;
                ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(path);
                if (excluded.Contains(path)) continue;
                if (++count > maximumEntries) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_TOO_LARGE");
                if (File.GetAttributes(path).HasFlag(FileAttributes.Directory)) pending.Push((path, directory.Depth + 1));
                else files.Add(path);
            }
            if (!hasEntries && directory.Path != root) empty.Add(directory.Path);
        }
        if (files.Count == 0) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_EMPTY");
        return new(files.OrderBy(p => p, StringComparer.Ordinal).ToArray(), empty.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }
}
