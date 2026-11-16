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
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private sealed record RegisteredChoice(AcquisitionRegisteredDownloadCandidate Candidate, AcquisitionRegisteredDownload Receipt,
        AcquisitionPlanSource Source, ArtifactRecord Artifact, DateTime AttemptAt);

    public async Task<AcquisitionRegisteredDownloadsPreview> PreviewRegisteredDownloadsAsync(int id,
        AcquisitionRegisteredDownloadsInput input, CancellationToken ct, Func<string?, Task>? guardDirectory = null)
    {
        var task = await Editable(id, ct);
        Revision(task, input.ExpectedRevision);
        var choices = await RegisteredChoicesAsync(task, ct);
        var visible = new List<AcquisitionRegisteredDownloadCandidate>();
        foreach (var choice in choices)
        {
            try
            {
                await GuardRegisteredArtifactAsync(choice.Artifact, guardDirectory);
                // Apply and workflow start independently verify the full pinned manifest.
                if (choice.Artifact.Manifest.Entries.All(file =>
                {
                    var info = new FileInfo(Path.Combine(choice.Artifact.RootDirectory, file.RelativePath));
                    return info.Exists && info.Length == file.Length;
                })) visible.Add(choice.Candidate);
            }
            // An inaccessible old workspace does not invalidate an independently registered delivery.
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return new(visible);
    }

    private async Task GuardRegisteredArtifactAsync(ArtifactRecord artifact, Func<string?, Task>? guard)
    {
        if (guard != null) await guard(artifact.RootDirectory);
        storage.EnsurePathAllowed(artifact.RootDirectory);
        ArtifactManifestFiles.RejectLinks(artifact.RootDirectory);
        foreach (var file in ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest))
        {
            storage.EnsurePathAllowed(file);
            ArtifactManifestFiles.RejectLinks(file);
        }
    }

    private async Task<List<RegisteredChoice>> RegisteredChoicesAsync(AcquisitionTaskDbModel task, CancellationToken ct)
    {
        var state = State(task);
        if (artifacts == null || task.DeletedAt != null || task.Status != AcquisitionStatus.Waiting ||
            !await HasNeverExecutedAsync(task, ct) || state.RegisteredDownload != null ||
            !AcquisitionLocalFilesRecoveryService.CanPrepareWithoutFileOperations(task)) return [];
        var plan = task.PlanJson == null ? null : Plan(task);
        if (plan != null && (plan.Parts.Count != 1 || plan.Parts[0].ContinueFrom != null)) return [];
        var required = plan?.Parts.Single();
        var sources = required?.Sources ?? state.DraftSources.Select(s => new AcquisitionPlanSource
        {
            Id = s.Id, Url = s.Url, Kind = s.Kind, ProviderId = s.ProviderId, MethodId = s.MethodId,
            AccountId = s.AccountId, AccessCode = s.AccessCode, ArchivePassword = s.ArchivePassword, Processing = s.Processing
        }).ToList();
        var matches = new List<RegisteredChoice>();
        var placement = new { Output = outputDirectories?.ResolveNew(null) ?? task.TargetDirectory,
            Storage = CaptureStorage(), Delivery = DeliveryDirectories.Capture() };
        foreach (var reference in Sources(task).Where(r => r.Module != "acquisition" &&
                     !string.IsNullOrWhiteSpace(r.Revision) && !string.IsNullOrWhiteSpace(r.ResultKey)).Distinct())
        {
            var ids = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == reference.Module &&
                s.EntityKind == reference.EntityKind && s.EntityId == reference.EntityId && s.Revision == reference.Revision &&
                s.ResultKey == reference.ResultKey).Select(s => s.WorkflowRunId).Distinct().ToListAsync(ct);
            var runs = await db.WorkflowRuns.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
            foreach (var run in runs.Where(AcquisitionRegisteredDownload.Stopped))
            {
                foreach (var target in await AcquisitionRegisteredDownload.TargetsAsync(db, artifacts, run, ct))
                {
                    if (target.SelectedSource is not {} remote || remote.Id != reference.ResultKey || target.DownloadTaskId is not {} native)
                        continue;
                    var candidates = sources.Where(s => (s.Id == reference.ResultKey || s.ProcessingSource == reference) &&
                        (s.Url == remote.Url || s.Url == remote.OriginalUrl) &&
                        (s.AccountId == null || s.AccountId == (target.AccountId ?? remote.AccountId)) &&
                        (s.ProviderId == null || s.ProviderId == remote.ProviderId) &&
                        (s.MethodId == null || s.MethodId == remote.MethodId)).ToArray();
                    if (candidates is not [var source]) continue;
                    var artifact = await AcquisitionRegisteredDownload.ReadAsync(db, artifacts, task.Id, run, reference, target, source.Url, ct);
                    if (artifact == null) continue;
                    var results = await db.DownloadResults.AsNoTracking().Where(d => d.DownloadTaskId == native &&
                        d.Fingerprint == artifact.Fingerprint && d.DownloadDirectory == artifact.RootDirectory).ToListAsync(ct);
                    if (results.Count != 1 || !artifact.Files.ToHashSet(ArtifactManifestFiles.PathComparer)
                        .SetEquals(AcquisitionRegisteredDownload.Read<string[]>(results[0].FilesJson) ?? [])) continue;
                    var partId = required?.Id ?? source.Id;
                    var title = state.DraftSources.SingleOrDefault(s => s.Id == source.Id)?.Title ?? required?.Title ?? task.Title ?? source.Id;
                    foreach (var stage in new[] {"delivery", "processing", "download"})
                    {
                        var files = await AcquisitionRegisteredDownload.ReadStageAsync(db, artifacts, run, target, artifact, stage, ct);
                        if (files == null) continue;
                        var input = new AcquisitionPlanArtifactInput(task.Id, run.Id, files.Id, files.OperationId, files.Fingerprint);
                        var token = AcquisitionLocalFilesRecoveryReceipt.Hash(new {input, stage, task.PlanRevision, partId, source, reference,
                            target.Id, placement, DownloadTaskId = native, ResultId = results[0].Id, files.RootDirectory, run.Status, run.CurrentStepIndex});
                        var receipt = new AcquisitionRegisteredDownload(reference, partId, target.Id, native, results[0].Id,
                            input, files.RootDirectory, source.Url, task.PlanRevision, token, stage);
                        matches.Add(new(new(source.Id, title, run.Id, native, files.RootDirectory, files.Manifest.Entries.Count,
                            files.Manifest.Entries.Sum(f => f.Length), token, stage), receipt, source, files, run.CompletedAt ?? run.StartedAt));
                    }
                }
            }
        }
        return matches.OrderBy(c => c.Candidate.Stage switch {"delivery" => 0, "processing" => 1, _ => 2})
            .ThenByDescending(c => c.AttemptAt).DistinctBy(c => c.Candidate.Token).ToList();
    }

    public async Task<AcquisitionPlanTaskDetail> ApplyRegisteredDownloadAsync(int id, AcquisitionRegisteredDownloadSelection input,
        CancellationToken ct, Func<string?, Task>? guardDirectory = null)
    {
        var gate = AcquisitionTaskGate.For(id);
        await gate.WaitAsync(ct);
        try
        {
            var task = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct)
                ?? throw new ArgumentException("ACQUISITION_TASK_NOT_FOUND");
            if (State(task).RegisteredDownload is {} saved && saved.Token == input.Token && saved.PreviousRevision == input.ExpectedRevision)
            {
                if (guardDirectory != null) await guardDirectory(saved.Directory);
                return (await GetAsync(id, ct))!;
            }
            Revision(task, input.ExpectedRevision);
            var choice = (await RegisteredChoicesAsync(task, ct)).SingleOrDefault(c => c.Candidate.Token == input.Token)
                ?? throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            var producerGate = WorkflowRunMutationGate.For(choice.Candidate.RunId);
            await producerGate.WaitAsync(ct);
            try
            {
                await GuardRegisteredArtifactAsync(choice.Artifact, guardDirectory);
                if (!(await ArtifactManifestFiles.ValidateAsync(choice.Artifact.RootDirectory, choice.Artifact.Manifest, ct)).Available)
                    throw new InvalidOperationException("RECOVERY_FILES_UNVERIFIED");
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                task = await Tasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
                Revision(task, input.ExpectedRevision);
                if (!(await RegisteredChoicesAsync(task, ct)).Any(c => c.Candidate.Token == input.Token))
                    throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
                var original = State(task);
                var output = outputDirectories?.ResolveNew(null) ?? task.TargetDirectory;
                ValidateDirectory(output, allowEmpty: false);
                var policy = CaptureStorage();
                var delivery = DeliveryDirectories.Capture();
                if (guardDirectory != null)
                    foreach (var directory in new[] {output, policy.ProcessingWorkspaceDirectory, delivery.RootDirectory})
                        await guardDirectory(directory);
                var part = new AcquisitionPlanPart {Id = choice.Receipt.PartId, Title = choice.Candidate.SourceTitle,
                    Sources = [choice.Source], SelectedSourceId = choice.Source.Id, ContinueFrom = choice.Receipt.Input,
                    ProcessingSource = choice.Receipt.Origin, Processing = new() {Requirement = "unknown"}, UseAdaptiveAssessment = true};
                task.BuilderId = "prepared";
                task.BuilderStateJson = Serialize(original with {RegisteredDownload = choice.Receipt, Issues = [],
                    StoragePolicy = policy, DeliverySettings = delivery, DeliveryLayout = null});
                SavePlan(task, new() {Title = Title(task.Title ?? choice.Candidate.SourceTitle), OutputDirectory = output!, Parts = [part], AdaptiveFileProcessing = true},
                    Sources(task), "recovery.registeredDownload", preserveStorageOverrides: false, refreshStageDirectories: true);
                var destinations = Plan(task).StageDirectories?.Parts.Values.SelectMany(p => new[] {p.Download, p.Processing, p.Delivery}) ?? [];
                if (destinations.Any(d => d?.Ownership == "exclusive" && Overlap(d.Directory, choice.Artifact.RootDirectory)))
                    throw new InvalidOperationException("PLAN_STAGE_DIRECTORY_CONFLICT");
                var changed = await Tasks.Where(t => t.Id == id && t.DeletedAt == null && t.WorkflowRunId == null &&
                    t.Status == AcquisitionStatus.Waiting && t.PlanRevision == input.ExpectedRevision).ExecuteUpdateAsync(update => update
                    .SetProperty(t => t.BuilderId, task.BuilderId).SetProperty(t => t.BuilderStateJson, task.BuilderStateJson)
                    .SetProperty(t => t.PlanRevision, task.PlanRevision).SetProperty(t => t.PlanJson, task.PlanJson)
                    .SetProperty(t => t.PlanHistoryJson, task.PlanHistoryJson).SetProperty(t => t.Phase, task.Phase)
                    .SetProperty(t => t.TargetDirectory, task.TargetDirectory).SetProperty(t => t.UpdatedAt, task.UpdatedAt), ct);
                if (changed != 1) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
                await transaction.CommitAsync(ct);
            }
            finally { producerGate.Release(); }
            return (await GetAsync(id, ct))!;
        }
        finally {gate.Release();}
    }
}
