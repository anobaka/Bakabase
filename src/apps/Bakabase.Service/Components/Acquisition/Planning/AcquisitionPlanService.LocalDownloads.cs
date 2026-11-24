using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionLocalDownloadsInput(int ExpectedRevision);
public sealed record AcquisitionLocalDownloadsApplyInput(int ExpectedRevision, string Token);
public sealed record AcquisitionLocalDownloadFile(string RelativePath, long Bytes);
public sealed record AcquisitionLocalDownloadPart(string PartId, string? Title, string? Directory,
    int FileCount, long Bytes, string? ReasonCode, IReadOnlyList<AcquisitionLocalDownloadFile> Files, bool FilesTruncated);
public sealed record AcquisitionLocalDownloadsPreview(int Revision, bool CanApply, bool CanPrepare,
    string? ReasonCode, IReadOnlyList<AcquisitionLocalDownloadPart> Parts, string? Token = null);

internal sealed record AcquisitionLocalDownloadIntake(int PreparedFromRevision, int? AppliedFromRevision = null, string? AppliedToken = null);

public sealed partial class AcquisitionPlanService
{
    private const string LocalDownloadUnavailable = "LOCAL_DOWNLOAD_INTAKE_UNAVAILABLE";
    private const string LocalDownloadFilesChanged = "LOCAL_DOWNLOAD_FILES_CHANGED";
    private const int LocalDownloadPreviewLimit = 100;

    private sealed record LocalDownloadReview(AcquisitionLocalDownloadsPreview Preview,
        IReadOnlyDictionary<string, ArtifactManifest> Manifests);

    private async Task<bool> CanReceiveLocalDownloadsAsync(AcquisitionTaskDbModel row, AcquisitionPlan? plan,
        AcquisitionPlanBuilderState state, CancellationToken ct) =>
        artifacts != null && workspaces != null && row.DeletedAt == null && row.BuilderId != null &&
        row.Status == AcquisitionStatus.Waiting && row.Phase is "attention" or "ready" &&
        plan is {Parts.Count: > 0, PublishRetainedFiles: false} &&
        !string.IsNullOrWhiteSpace(plan.OutputDirectory) && Path.IsPathFullyQualified(plan.OutputDirectory) &&
        plan.Parts.All(part => part.Sources.Any(s => s.Id == part.SelectedSourceId) &&
            (part.ContinueFrom == null || part.ContinueFrom.ManualDownload == true && state.LocalDownloadIntake?.AppliedFromRevision != null) &&
            (plan.StageDirectories?.Parts.GetValueOrDefault(part.Id)?.Download is null or {Ownership: "exclusive", Allocation: not null})) &&
        state.RegisteredDownload == null && state.LocalFilesRecovery == null &&
        AcquisitionLocalFilesRecoveryService.CanPrepareWithoutFileOperations(row) &&
        await HasNeverExecutedAsync(row, ct);

    public async Task<AcquisitionLocalDownloadsPreview> PreviewLocalDownloadsAsync(int id, AcquisitionLocalDownloadsInput input,
        Func<string, Task>? guard, CancellationToken ct)
    {
        var row = await LocalDownloadTaskAsync(id, ct);
        Revision(row, input.ExpectedRevision);
        return (await ReviewLocalDownloadsAsync(row, guard, ct)).Preview;
    }

    public async Task<AcquisitionLocalDownloadsPreview> PrepareLocalDownloadsAsync(int id, AcquisitionLocalDownloadsInput input,
        Func<string, Task>? guard, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id);
        await gate.WaitAsync(ct);
        try
        {
            var row = await LocalDownloadTaskAsync(id, ct);
            var state = State(row);
            if (state.LocalDownloadIntake?.PreparedFromRevision == input.ExpectedRevision && row.PlanRevision == input.ExpectedRevision + 1)
                return (await ReviewLocalDownloadsAsync(row, guard, ct)).Preview;
            Revision(row, input.ExpectedRevision);
            var plan = Plan(row);
            if (!await CanReceiveLocalDownloadsAsync(row, plan, state, ct)) throw new InvalidOperationException(LocalDownloadUnavailable);
            var current = await CurrentStageDirectoriesAsync(row, plan, ct);
            var directories = LocalDownloadDirectories(row, plan, current);
            AcquisitionStageDirectoryResolver.Validate(directories, plan);
            if (state.LocalDownloadIntake != null && current.Parts.Values.All(p => p.Download != null))
                return (await ReviewLocalDownloadsAsync(row, guard, ct)).Preview;

            await RequireStageDirectoryInactiveAsync(row, ct);
            foreach (var part in plan.Parts)
            {
                var folder = directories.Parts[part.Id].Download!;
                await RequireLocalDownloadFolderAsync(row, part.Id, folder, guard, ct);
                storage.EnsureTreeMutationAllowed(folder.Directory);
            }
            foreach (var part in plan.Parts)
            {
                var scope = directories.Parts[part.Id].Download!.Allocation!;
                try { await workspaces!.EnsureScopedDirectoryAsync(scope, ct); }
                catch (InvalidDataException) { throw new InvalidOperationException("LOCAL_DOWNLOAD_FOLDER_CONFLICT"); }
            }

            await SaveLocalDownloadPlanAsync(row, plan with {StageDirectories = directories}, state with
                {LocalDownloadIntake = new(row.PlanRevision)}, "download.prepareLocalReception", ct);
            return (await ReviewLocalDownloadsAsync(row, guard, ct)).Preview;
        }
        finally { gate.Release(); }
    }

    public async Task<AcquisitionPlanTaskDetail> ApplyLocalDownloadsAsync(int id, AcquisitionLocalDownloadsApplyInput input,
        Func<string, Task>? guard, CancellationToken ct)
    {
        if (input.Token == null || input.Token.Length != 64 || !input.Token.All(Uri.IsHexDigit))
            throw new ArgumentException(LocalDownloadFilesChanged);
        var gate = AcquisitionTaskGate.For(id);
        await gate.WaitAsync(ct);
        try
        {
            var row = await LocalDownloadTaskAsync(id, ct);
            var state = State(row);
            if (state.LocalDownloadIntake is { } saved && saved.AppliedFromRevision == input.ExpectedRevision &&
                row.PlanRevision == input.ExpectedRevision + 1 && saved.AppliedToken == input.Token)
            {
                if (guard != null)
                    foreach (var folder in Plan(row).StageDirectories!.Parts.Values) await guard(folder.Download!.Directory);
                return await DetailAsync(row, ct);
            }
            Revision(row, input.ExpectedRevision);
            var review = await ReviewLocalDownloadsAsync(row, guard, ct);
            if (!review.Preview.CanApply || review.Preview.Token != input.Token)
                throw new InvalidOperationException(LocalDownloadFilesChanged);
            var plan = Plan(row);
            var parts = new List<AcquisitionPlanPart>();
            foreach (var part in plan.Parts)
            {
                var folder = plan.StageDirectories!.Parts[part.Id].Download!;
                var captured = await AcquisitionCurrentDirectoryFiles.CaptureLocalDownloadAsync(artifacts!, row.Id,
                    row.PlanRevision + 1, part.Id, part.SelectedSourceId!, folder, review.Manifests[part.Id], ct);
                parts.Add(part with {ContinueFrom = new(row.Id, 0, captured.Id, captured.OperationId, captured.Fingerprint)
                    {ManualDownload = true}});
            }
            // AdoptAsync registers each part independently; these first-run records are
            // non-managed and cannot become inputs until the whole plan revision commits.
            // Check exact paths again before that commit to catch a concurrent writer.
            foreach (var part in parts)
            {
                var folder = plan.StageDirectories!.Parts[part.Id].Download!;
                await RequireLocalDownloadFolderAsync(row, part.Id, folder, guard, ct);
                var artifact = await artifacts!.GetArtifactAsync(part.ContinueFrom!.ArtifactId, ct)
                    ?? throw new InvalidOperationException(LocalDownloadFilesChanged);
                await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(artifact, workspaces, ct);
            }
            var intake = state.LocalDownloadIntake ?? throw new InvalidOperationException(LocalDownloadUnavailable);
            // The original remote sources stay in the plan as provenance. Their acquisition
            // prerequisites are superseded by the files; the existing processing plan remains.
            var processingIssues = await FileProcessingDependencyPreflight.InspectAsync(parts.Select(AcquisitionPlanCompiler.Processing),
                processingDependencies ?? [], ct);
            var issues = state.Issues.Where(i => i.Code.StartsWith("PROCESSING_", StringComparison.Ordinal) ||
                i.Code.StartsWith("SOURCE_PROCESSING_", StringComparison.Ordinal))
                .Concat(processingIssues.Select(i => new AcquisitionPlanIssue(i.Code, i.Message))).DistinctBy(i => i.Code).ToList();
            await SaveLocalDownloadPlanAsync(row, plan with {Parts = parts}, state with
            {
                Issues = issues,
                LocalDownloadIntake = intake with {AppliedFromRevision = input.ExpectedRevision, AppliedToken = input.Token}
            }, "download.receiveLocalFiles", ct);
            return await DetailAsync(row, ct);
        }
        finally { gate.Release(); }
    }

    private async Task<AcquisitionTaskDbModel> LocalDownloadTaskAsync(int id, CancellationToken ct) =>
        await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.BuilderId != null && t.DeletedAt == null && t.PlanJson != null, ct)
        ?? throw new InvalidOperationException(LocalDownloadUnavailable);

    private AcquisitionStageDirectories LocalDownloadDirectories(AcquisitionTaskDbModel row, AcquisitionPlan plan,
        AcquisitionStageDirectories current)
    {
        var allocated = current.Parts.Values.Any(p => p.Download == null)
            ? AcquisitionStageDirectoryResolver.Allocate(row.Id, plan, DeliveryDirectories) : null;
        return current with {Parts = current.Parts.ToDictionary(p => p.Key, p => p.Value with
        {
            Download = p.Value.Download ?? allocated!.Parts[p.Key].Download
        }, StringComparer.Ordinal)};
    }

    private async Task SaveLocalDownloadPlanAsync(AcquisitionTaskDbModel row, AcquisitionPlan plan,
        AcquisitionPlanBuilderState state, string reason, CancellationToken ct)
    {
        var original = row.PlanJson;
        var originalState = row.BuilderStateJson;
        var originalRevision = row.PlanRevision;
        row.BuilderStateJson = Serialize(state);
        SavePlan(row, plan, Sources(row), reason, currentDirectoryBindings: plan.StageDirectories);
        var changed = await Tasks.Where(t => t.Id == row.Id && t.DeletedAt == null && t.WorkflowRunId == null &&
                t.Status == AcquisitionStatus.Waiting && t.PlanRevision == originalRevision && t.PlanJson == original &&
                t.BuilderStateJson == originalState)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.PlanRevision, row.PlanRevision)
                .SetProperty(t => t.PlanJson, row.PlanJson).SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson)
                .SetProperty(t => t.BuilderStateJson, row.BuilderStateJson).SetProperty(t => t.Phase, row.Phase)
                .SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
        if (changed != 1) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
    }

    private async Task RequireLocalDownloadFolderAsync(AcquisitionTaskDbModel row, string partId,
        AcquisitionStageDirectory folder, Func<string, Task>? guard, CancellationToken ct)
    {
        if (folder is not {Ownership: "exclusive", Allocation: { } scope} ||
            scope.OwnerKind != "acquisition" || scope.OwnerId != row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            scope.Role != "download-" + partId || !AcquisitionStageDirectoryFileMove.Same(scope.Directory, folder.Directory))
            throw new InvalidOperationException("LOCAL_DOWNLOAD_FOLDER_CONFLICT");
        FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope);
        FileProcessingWorkspaceResolver.ValidateConfiguredRoot(scope.StorageRoot, storage);
        if (guard != null) await guard(folder.Directory);
        await StageDirectoryReferencesAsync(row, [], partId, folder.Directory, ct);
    }

    private async Task<LocalDownloadReview> ReviewLocalDownloadsAsync(AcquisitionTaskDbModel row,
        Func<string, Task>? guard, CancellationToken ct)
    {
        var plan = Plan(row);
        if (!await CanReceiveLocalDownloadsAsync(row, plan, State(row), ct))
            return new(new(row.PlanRevision, false, false, LocalDownloadUnavailable, []),
                new Dictionary<string, ArtifactManifest>());
        var current = await CurrentStageDirectoriesAsync(row, plan, ct);
        var directories = LocalDownloadDirectories(row, plan, current);
        var manifests = new Dictionary<string, ArtifactManifest>(StringComparer.Ordinal);
        var rows = new List<AcquisitionLocalDownloadPart>();
        var needsPreparation = State(row).LocalDownloadIntake == null || current.Parts.Values.Any(p => p.Download == null);
        var canPrepare = needsPreparation;
        foreach (var part in plan.Parts)
        {
            string? problem = null;
            string? visibleDirectory = null;
            ArtifactManifest? manifest = null;
            var folder = directories.Parts[part.Id].Download!;
            try
            {
                await RequireLocalDownloadFolderAsync(row, part.Id, folder, guard, ct);
                visibleDirectory = folder.Directory;
                if (!needsPreparation)
                {
                    if (!await workspaces!.HasScopedDirectoryAsync(folder.Allocation!, ct))
                        throw new InvalidOperationException("LOCAL_DOWNLOAD_FOLDER_UNAVAILABLE");
                    manifest = await AcquisitionCurrentDirectoryFiles.ReadLocalDownloadManifestAsync(folder.Directory, storage, ct);
                    manifests.Add(part.Id, manifest);
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                if (needsPreparation) canPrepare = false;
                problem = error.Message switch
                {
                    "PLAN_CURRENT_DIRECTORY_EMPTY" => "LOCAL_DOWNLOAD_FILES_MISSING",
                    "PLAN_CURRENT_DIRECTORY_TOO_LARGE" => "LOCAL_DOWNLOAD_INPUT_TOO_LARGE",
                    "LOCAL_DOWNLOAD_PARTIAL_FILES" => error.Message,
                    "LOCAL_DOWNLOAD_FILES_CHANGED" => error.Message,
                    "STAGE_DIRECTORY_SHARED" or "STAGE_DIRECTORY_REFERENCED" or "STAGE_DIRECTORY_OWNERSHIP_UNKNOWN" or
                        "LOCAL_DOWNLOAD_FOLDER_CONFLICT" => "LOCAL_DOWNLOAD_FOLDER_CONFLICT",
                    _ when error is DirectoryNotFoundException || error.Message == "LOCAL_DOWNLOAD_FOLDER_UNAVAILABLE" =>
                        "LOCAL_DOWNLOAD_FOLDER_UNAVAILABLE",
                    _ => "LOCAL_DOWNLOAD_FILES_UNREADABLE"
                };
            }
            var entries = manifest?.Entries ?? [];
            rows.Add(new(part.Id, part.Title, visibleDirectory, entries.Count, entries.Sum(e => e.Length), problem,
                entries.Take(LocalDownloadPreviewLimit).Select(e => new AcquisitionLocalDownloadFile(e.RelativePath, e.Length)).ToArray(),
                entries.Count > LocalDownloadPreviewLimit));
        }
        var complete = manifests.Count == plan.Parts.Count;
        var token = complete ? ArtifactManifestFiles.Hash(Serialize(new
        {
            row.Id, row.PlanRevision,
            Parts = plan.Parts.Select(p => new {p.Id, p.SelectedSourceId,
                Folder = directories.Parts[p.Id].Download, manifests[p.Id].Fingerprint}).ToArray()
        })) : null;
        return new(new(row.PlanRevision, complete, canPrepare, rows.FirstOrDefault(r => r.ReasonCode != null)?.ReasonCode, rows, token),
            manifests);
    }
}
