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
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionManualDownloadCaptureReceipt(int Version, int TaskId, string PartId,
    int Revision, string SourceId, string BindingId, string Directory, string Fingerprint,
    IReadOnlyList<OwnedWorkspaceDirectory> ExcludedWorkspaces);

internal static partial class AcquisitionCurrentDirectoryFiles
{
    private const int ManualDownloadReceiptVersion = 3;

    internal static async Task<ArtifactManifest> ReadLocalDownloadManifestAsync(string directory, IUserStoragePolicy storage,
        CancellationToken ct)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
        var selection = await EnumerateAsync(directory, [], null, ct, () => Task.FromResult(new HashSet<string>()));
        foreach (var path in selection.Directories) storage.EnsurePathAllowed(path);
        foreach (var file in selection.Files)
        {
            storage.EnsurePathAllowed(file);
            if (AcquisitionInboxService.HasPartialDownloadExtension(file))
                throw new InvalidOperationException("LOCAL_DOWNLOAD_PARTIAL_FILES");
            ArtifactFileIdentity.RequireRegularFile(file);
        }
        var manifest = await ArtifactManifestFiles.CaptureAsync(directory, selection.Files, ct, selection.Directories);
        var after = await EnumerateAsync(directory, [], null, ct, () => Task.FromResult(new HashSet<string>()));
        if (!selection.Files.SequenceEqual(after.Files) || !selection.Directories.SequenceEqual(after.Directories))
            throw new InvalidOperationException("LOCAL_DOWNLOAD_FILES_CHANGED");
        return manifest;
    }

    internal static async Task<ArtifactRecord> CaptureLocalDownloadAsync(IArtifactOperationStore store, int taskId,
        int revision, string partId, string sourceId, AcquisitionStageDirectory folder, ArtifactManifest manifest, CancellationToken ct)
    {
        var receipt = new AcquisitionManualDownloadCaptureReceipt(ManualDownloadReceiptVersion, taskId, partId,
            revision, sourceId, folder.BindingId!, folder.Directory, manifest.Fingerprint, []);
        var seed = ManualDownloadSeed(receipt);
        var files = ArtifactManifestFiles.ResolveFiles(folder.Directory, manifest);
        foreach (var file in files) ArtifactFileIdentity.RequireRegularFile(file);
        return await store.AdoptAsync(new(ArtifactManifestFiles.Hash("acquisition.manual-download:" + seed),
                "acquisition.manual-download:" + seed, OperationKind, seed),
            new(folder.Directory, files, Kind, false, [], JsonSerializer.Serialize(receipt, Json), manifest.Fingerprint,
                ArtifactManifestFiles.ResolveDirectories(folder.Directory, manifest)), "acquisition.manual-download:" + taskId, ct);
    }

    private static string ManualDownloadSeed(AcquisitionManualDownloadCaptureReceipt receipt) =>
        ArtifactManifestFiles.Hash(JsonSerializer.Serialize(receipt, Json));

    internal static AcquisitionManualDownloadCaptureReceipt? ManualDownloadReceipt(ArtifactRecord artifact) =>
        artifact.Kind == Kind && !artifact.Managed && artifact.MetadataJson != null &&
        JsonSerializer.Deserialize<AcquisitionManualDownloadCaptureReceipt>(artifact.MetadataJson, Json) is
            {Version: ManualDownloadReceiptVersion} receipt ? receipt : null;

    // An interrupted first reception may have recorded a non-managed artifact but not its
    // plan revision. It can be recognized only in its own matching task/part allocation.
    // It cannot be executed until the full approved revision exists in plan history.
    internal static async Task<bool> IsLocalDownloadDirectoryArtifactAsync(IArtifactOperationStore store,
        AcquisitionTaskDbModel row, string partId, string directory, ArtifactRecord artifact, CancellationToken ct)
    {
        if (ManualDownloadReceipt(artifact) is not { } receipt || receipt.TaskId != row.Id || receipt.PartId != partId ||
            row.PlanJson == null || !ArtifactManifestFiles.PathComparer.Equals(receipt.Directory, directory)) return false;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson, Json);
        return ManualDownloadBindingMatches(plan, receipt) && await IsManualDownloadArtifactAsync(store, artifact, receipt, ct);
    }

    private static bool ManualDownloadBindingMatches(AcquisitionPlan? plan, AcquisitionManualDownloadCaptureReceipt receipt) =>
        Entry(plan?.StageDirectories, receipt.PartId, "download") is {Ownership: "exclusive", Allocation: not null} folder &&
        folder.BindingId == receipt.BindingId && ArtifactManifestFiles.PathComparer.Equals(folder.Directory, receipt.Directory);

    private static async Task<bool> IsManualDownloadArtifactAsync(IArtifactOperationStore store, ArtifactRecord artifact,
        AcquisitionManualDownloadCaptureReceipt receipt, CancellationToken ct)
    {
        if (receipt.Revision < 1 || !Guid.TryParseExact(receipt.BindingId, "N", out _) || receipt.ExcludedWorkspaces is not {Count: 0} ||
            artifact.ParentArtifactIds.Count != 0 || artifact.Manifest.Entries.Count == 0 || artifact.Fingerprint != receipt.Fingerprint ||
            artifact.Manifest.Fingerprint != receipt.Fingerprint ||
            !ArtifactManifestFiles.PathComparer.Equals(artifact.RootDirectory, receipt.Directory)) return false;
        var operation = await store.GetOperationAsync(artifact.OperationId, ct);
        return operation is {Kind: OperationKind, State: ArtifactOperationState.Completed} && operation.OutputArtifactId == artifact.Id &&
            operation.InputFingerprint == ManualDownloadSeed(receipt);
    }

    private static async Task<bool> IsManualDownloadOwnedAsync(BakabaseDbContext db, IArtifactOperationStore store,
        ArtifactRecord artifact, AcquisitionManualDownloadCaptureReceipt receipt, int taskId, int runId, string partId, CancellationToken ct)
    {
        if (receipt.TaskId != taskId || receipt.PartId != partId ||
            !await IsManualDownloadArtifactAsync(store, artifact, receipt, ct)) return false;
        if (runId != 0 && (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, runId, taskId, ct) ||
                !await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(db, runId, partId, artifact.OperationId, ct))) return false;
        var history = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.DeletedAt == null)
            .Select(t => t.PlanHistoryJson).SingleOrDefaultAsync(ct);
        if (history == null) return false;
        var input = new AcquisitionPlanArtifactInput(taskId, 0, artifact.Id, artifact.OperationId, artifact.Fingerprint) {ManualDownload = true};
        return (JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(history, Json) ?? []).Any(h =>
            h.Revision == receipt.Revision && h.Reason == "download.receiveLocalFiles" && ManualDownloadBindingMatches(h.Plan, receipt) &&
            h.Plan.Parts.Any(p => p.Id == partId && p.SelectedSourceId == receipt.SourceId && p.ContinueFrom == input));
    }
}
