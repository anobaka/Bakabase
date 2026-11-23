using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

// A stage binding authorizes reading its current contents. It deliberately grants no
// exclusive ownership or automatic deletion rights to files supplied by the user.
internal sealed record AcquisitionStageDirectoryCaptureReceipt(int Version, int TaskId, string PartId,
    int Revision, int PreviousRunId, string CaptureId, string Stage, string BindingId, string Directory,
    string InputFingerprint, string Fingerprint, IReadOnlyList<OwnedWorkspaceDirectory> ExcludedWorkspaces,
    ArtifactDirectoryProvenance? DirectoryProvenance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool DeliveryOnly = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool VerifiedSmbFileNames = false);

internal static partial class AcquisitionCurrentDirectoryFiles
{
    internal static bool ValidStage(string? stage) => stage is "download" or "processing" or "delivery";
    internal static AcquisitionStageDirectory? Entry(AcquisitionStageDirectories? directories, string partId, string stage)
    {
        var part = directories?.Parts.GetValueOrDefault(partId);
        return stage switch {"download" => part?.Download, "processing" => part?.Processing, "delivery" => part?.Delivery, _ => null};
    }

    internal static async Task<ArtifactRecord> CaptureStageAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces, int taskId, int revision,
        int runId, string partId, string stage, AcquisitionStageDirectory binding, Func<Task> guard, CancellationToken ct,
        bool deliveryOnly = false)
    {
        if (!ValidStage(stage) || !Guid.TryParseExact(binding.BindingId, "N", out _) || revision < 1 ||
            deliveryOnly && stage != "delivery")
            throw new ArgumentException("PLAN_STAGE_DIRECTORY_INVALID");
        await guard();
        storage.EnsurePathAllowed(binding.Directory); ArtifactManifestFiles.RejectLinks(binding.Directory);
        if (!Directory.Exists(binding.Directory)) throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_UNAVAILABLE");
        var excluded = workspaces == null ? [] : await workspaces.OwnedDirectoriesUnderAsync(binding.Directory, ct);
        var selection = await EnumerateAsync(binding.Directory, excluded, workspaces, ct,
            () => ReadDownloadMarkerFilesAsync(db, store, taskId, partId, binding.Directory, ct),
            deliveryOnly ? MaximumDeliveryEntries : MaximumEntries);
        foreach (var file in selection.Files.Concat(selection.Directories)) storage.EnsurePathAllowed(file);
        var provenance = await AcquisitionStageDirectoryProvenance.ReadAsync(db, store, taskId, partId, binding.Directory, ct);
        var receipt = new AcquisitionStageDirectoryCaptureReceipt(2, taskId, partId, revision, runId,
            Guid.NewGuid().ToString("N"), stage, binding.BindingId!, binding.Directory, "", "", excluded,
            DeliveryOnly: deliveryOnly, VerifiedSmbFileNames: selection.HasRecordedMarker);
        receipt = receipt with {InputFingerprint = StageSeed(receipt)};
        var captured = await store.AdoptAsync(new(ArtifactManifestFiles.Hash("current-stage:" + receipt.InputFingerprint),
                "acquisition.current-stage:" + receipt.InputFingerprint, OperationKind, receipt.InputFingerprint),
            new(binding.Directory, selection.Files, Kind, false, [], Directories: selection.Directories,
                CreateMetadata: manifest => JsonSerializer.Serialize(receipt with {Fingerprint = manifest.Fingerprint,
                    DirectoryProvenance = AcquisitionStageDirectoryProvenance.Project(provenance, manifest)}, Json)),
            "acquisition.current-stage:" + taskId, ct);
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(), StageSlot(receipt), captured.OperationId, ct))
            throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
        await guard();
        await ValidateSelectionAsync(captured, workspaces, ct);
        return captured;
    }

    private static string StageSlot(AcquisitionStageDirectoryCaptureReceipt receipt) =>
        $"{receipt.PartId}:current-files:{receipt.Revision}:{receipt.CaptureId}";
    internal static bool IsDeliveryOnlyStageCapture(ArtifactRecord? artifact, AcquisitionStageDirectory? binding = null) =>
        artifact?.Kind == Kind &&
        JsonSerializer.Deserialize<AcquisitionStageDirectoryCaptureReceipt>(artifact.MetadataJson ?? "null", Json)
            is {Version: 2, Stage: "delivery", DeliveryOnly: true} receipt &&
        (binding == null || binding.BindingId == receipt.BindingId &&
            ArtifactManifestFiles.PathComparer.Equals(binding.Directory, receipt.Directory));

    private static string StageSeed(AcquisitionStageDirectoryCaptureReceipt receipt)
    {
        var identity = JsonSerializer.Serialize(new {receipt.Version, receipt.TaskId, receipt.PartId, receipt.Revision,
            receipt.PreviousRunId, receipt.CaptureId, receipt.Stage, receipt.BindingId, receipt.Directory,
            receipt.ExcludedWorkspaces}, Json);
        // Existing capture receipts must retain their original fingerprints.
        if (receipt.DeliveryOnly) identity += "\ndelivery-only";
        if (receipt.VerifiedSmbFileNames) identity += "\nverified-smb-file-names";
        return ArtifactManifestFiles.Hash(identity);
    }

    private static async Task<bool> IsStageOwnedAsync(BakabaseDbContext db, IArtifactOperationStore store,
        ArtifactRecord artifact, AcquisitionStageDirectoryCaptureReceipt receipt, int taskId, int runId,
        string partId, CancellationToken ct)
    {
        if (receipt.Version != 2 || receipt.TaskId != taskId || receipt.PartId != partId || receipt.Revision < 1 ||
            !ValidStage(receipt.Stage) || receipt.DeliveryOnly && receipt.Stage != "delivery" ||
            !Guid.TryParseExact(receipt.BindingId, "N", out _) ||
            !Guid.TryParseExact(receipt.CaptureId, "N", out _) || receipt.ExcludedWorkspaces == null ||
            artifact.ParentArtifactIds.Count != 0 || artifact.Fingerprint != receipt.Fingerprint ||
            artifact.Manifest.Fingerprint != artifact.Fingerprint ||
            !ArtifactManifestFiles.PathComparer.Equals(receipt.Directory, artifact.RootDirectory) ||
            !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, receipt.PreviousRunId, taskId, ct) ||
            runId != receipt.PreviousRunId && !await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(db, runId, partId, artifact.OperationId, ct) ||
            !await db.ArtifactOperationBindings.AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == receipt.PreviousRunId.ToString() &&
                b.Slot == StageSlot(receipt) && b.OperationId == artifact.OperationId, ct)) return false;
        var task = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId)
            .Select(t => new {t.PlanJson, t.PlanHistoryJson}).SingleOrDefaultAsync(ct);
        if (task == null) return false;
        var plan = task.PlanJson == null ? null : JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, Json);
        var history = task.PlanHistoryJson == null ? [] : JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(task.PlanHistoryJson, Json) ?? [];
        bool Matches(AcquisitionPlan? candidate) => Entry(candidate?.StageDirectories, partId, receipt.Stage) is {} entry &&
            entry.BindingId == receipt.BindingId && ArtifactManifestFiles.PathComparer.Equals(entry.Directory, receipt.Directory);
        if (!Matches(plan) && !history.Any(h => h.Revision <= receipt.Revision && Matches(h.Plan))) return false;
        var operation = await store.GetOperationAsync(artifact.OperationId, ct);
        return operation is {Kind: OperationKind, State: ArtifactOperationState.Completed} && operation.OutputArtifactId == artifact.Id &&
            operation.InputFingerprint == receipt.InputFingerprint && receipt.InputFingerprint == StageSeed(receipt);
    }
}
