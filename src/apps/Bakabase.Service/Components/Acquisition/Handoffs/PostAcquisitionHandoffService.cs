using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

/// <summary>Resumes only durable, explicitly opted-in handoffs. Never scans completed posts for work.</summary>
public sealed partial class PostAcquisitionHandoffService(BakabaseDbContext db, PostDownloadIntentAdapter reader,
    DownloadIntentService downloads, AcquisitionPlanService plans, PostParserTaskExecutionGate parserGate,
    IUserStoragePolicy storage, IBOptions<ThirdPartyOptions> options, AcquisitionOutputDirectoryResolver directories,
    IPostParserTaskService postTasks, IBOptions<AcquisitionOptions>? acquisitionOptions = null)
{
    private static readonly SemaphoreSlim ConsumerGate = new(1, 1);
    public string? RetryDirectory => directories.ResolveNew(string.IsNullOrWhiteSpace(options.Value.PostParserAcquisition?.OutputDirectory)
        ? null : options.Value.PostParserAcquisition.OutputDirectory);

    public async Task<AcquisitionHandoffView?> GetAsync(string id, CancellationToken ct)
    {
        var row = await db.AcquisitionHandoffs.AsNoTracking().SingleOrDefaultAsync(h => h.Id == id, ct);
        return row == null ? null : AcquisitionHandoffView.From(row);
    }

    public async Task<AcquisitionHandoffView> RetryAsync(string id, CancellationToken ct)
    {
        await ConsumerGate.WaitAsync(ct);
        try
        {
            var row = await db.AcquisitionHandoffs.SingleOrDefaultAsync(h => h.Id == id, ct)
                ?? throw new ArgumentException("HANDOFF_NOT_FOUND");
            if (row.Status is not ("attention" or "error") || row.AcquisitionTaskId != null)
                throw new InvalidOperationException("HANDOFF_ALREADY_CREATED");
            if (options.Value.PostParserAcquisition?.Enabled != true)
                throw new InvalidOperationException("HANDOFF_DISABLED");
            if (!await CurrentAsync(row, ct)) throw new InvalidOperationException("HANDOFF_SOURCE_CHANGED");
            row.OutputDirectory = RetryDirectory;
            row.Status = "queued"; row.Code = null; row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return AcquisitionHandoffView.From(row);
        }
        finally {ConsumerGate.Release();}
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await ConsumerGate.WaitAsync(ct);
        try
        {
            var row = await db.AcquisitionHandoffs.Where(h => h.SourceModule == "postParser" && h.SourceKind == "task" &&
                (h.Status == "queued" || h.Status == "prepared")).OrderBy(h => h.CreatedAt).FirstOrDefaultAsync(ct);
            if (row == null) return false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                await ProcessAsync(row, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {throw;}
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                // A failed transaction may leave tracked inserts. Never accidentally persist them here.
                db.ChangeTracker.Clear();
                var code = error is OperationCanceledException ? "HANDOFF_TIMED_OUT" : "HANDOFF_FAILED";
                await db.AcquisitionHandoffs.Where(h => h.Id == row.Id).ExecuteUpdateAsync(update => update
                    .SetProperty(h => h.Status, "error").SetProperty(h => h.Code, code)
                    .SetProperty(h => h.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
            }
            return true;
        }
        finally {ConsumerGate.Release();}
    }

    private async Task ProcessAsync(AcquisitionHandoffDbModel row, CancellationToken ct)
    {
        if (row.AcquisitionTaskId == null)
        {
            if (options.Value.PostParserAcquisition?.Enabled != true)
            {await StateAsync(row, "attention", "HANDOFF_DISABLED", ct); return;}
            if (!await CurrentAsync(row, ct))
            {await StateAsync(row, "attention", "HANDOFF_SOURCE_CHANGED", ct); return;}
            var input = new PostDownloadIntentRequest {TaskId = int.Parse(row.SourceId, CultureInfo.InvariantCulture),
                Revision = int.Parse(row.SourceRevision, CultureInfo.InvariantCulture)};
            // Read a stable saved result. Do not hold the parser gate during resolvers or readiness checks.
            await parserGate.Semaphore.WaitAsync(ct);
            (DownloadIntentRequest Input, string? SourceReference,
                IReadOnlyDictionary<string, ProcessingPlans.SourceProcessingPlanView> ProcessingPlans,
                Bakabase.Modules.PostParser.Models.Domain.PostDownloadMediaView Media) saved;
            IReadOnlyList<IReadOnlyList<string>>? groups;
            try
            {
                saved = await reader.Read(input, false, ct);
                var post = await db.PostParserTasks.AsNoTracking().SingleAsync(t => t.Id == input.TaskId, ct);
                groups = PostAcquisitionSelection.Groups(PostParserSavedResults.ReadDownloadInfo(post), saved.Input.Candidates);
            }
            finally {parserGate.Semaphore.Release();}

            var directory = row.OutputDirectory ?? "";
            var directoryInvalid = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException();
                    storage.EnsurePathAllowed(directory);
                }
            }
            catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
            {directory = ""; directoryInvalid = true;}
            // An ambiguous result becomes a draft with all its original locators and credentials.
            // A clear result selects one ready mirror per explicit main group, never every mirror.
            IReadOnlyList<DownloadCandidateView> chosen = [];
            IReadOnlyList<DownloadCandidateView> checkedCandidates = [];
            var priority = acquisitionOptions?.Value.SourceProviderPriority ?? new AcquisitionOptions().SourceProviderPriority;
            if (groups is {Count: > 0})
            {
                var check = await downloads.PreflightAsync(saved.Input with {CheckSourceAvailability = true}, directory, ct);
                checkedCandidates = check.Candidates;
                chosen = PostAcquisitionSelection.Choose(groups, check.Candidates, priority);
            }
            var problem = groups == null ? "SOURCE_SELECTION_REQUIRED" : groups.Count == 0 ? "HANDOFF_NO_MAIN_SOURCE" :
                chosen.Any(c => c.ResolvedSource == null) ? "HANDOFF_SOURCE_UNRESOLVED" : null;
            await parserGate.Semaphore.WaitAsync(ct);
            try
            {
                if (!await CurrentAsync(row, ct))
                {await StateAsync(row, "attention", "HANDOFF_SOURCE_CHANGED", ct); return;}
                // Manual and automatic producer adapters share this gate. Respect an existing
                // reviewed task instead of creating or auto-starting another copy of that revision.
                var existing = await ExistingTasksAsync(row, ct);
                if (existing.Count > 0)
                {
                    row.AcquisitionTaskId = existing.Count == 1 ? existing[0] : null;
                    await StateAsync(row, "attention", existing.Count == 1 ? "HANDOFF_EXISTING_TASK" : "HANDOFF_MULTIPLE_TASKS", ct);
                    return;
                }
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var origin = new WorkflowSourceRef(row.SourceModule, row.SourceKind, row.SourceId, row.SourceRevision);
                var included = problem != null ? saved.Input.Candidates : saved.Input.Candidates.Where(c =>
                    chosen.Any(selected => selected.Candidate.Id == c.Id || c.MirrorGroupConfirmed == true &&
                        selected.Candidate.MirrorGroupConfirmed == true && c.GroupId == selected.Candidate.GroupId)).ToList();
                var refs = new[] {origin}.Concat(included.Select(c => origin with {ResultKey = c.Id})).ToList();
                if (problem != null)
                {
                    var draft = await plans.CreateDraftRowAsync(new(saved.Input.Title ?? "Acquisition",
                        saved.Input.Candidates.Select(c => new AcquisitionDraftSource
                        {
                            Id = c.Id, Url = c.Url, Title = c.Title, Kind = c.Kind, ProviderId = c.ProviderId,
                            AccessCode = c.AccessCode, ArchivePassword = c.ArchivePassword,
                            Processing = c.ExtractionPlanJson == null ? null : JsonSerializer.Deserialize<FileProcessingPlan>(c.ExtractionPlanJson, JsonSerializerOptions.Web)
                        }).ToList(), directory), null, [], refs, row.Id, ct);
                    var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(draft.BuilderStateJson!, JsonSerializerOptions.Web)!;
                    draft.BuilderStateJson = JsonSerializer.Serialize(state with {Issues = [new(problem,
                        "Review the saved source selection before starting acquisition.")]}, JsonSerializerOptions.Web);
                    draft.Phase = "attention"; row.AcquisitionTaskId = draft.Id; row.Status = "attention"; row.Code = problem;
                }
                else
                {
                    var plan = PostAcquisitionPlanAdapter.BuildPlan(input, saved, chosen, directory, checkedCandidates,
                        automaticSelection: true, providerPriority: priority);
                    var detail = await plans.CreatePreparedPlanAsync(plan, refs, ct);
                    row.AcquisitionTaskId = detail.Id; row.Status = "prepared";
                    row.Code = directoryInvalid ? "OUTPUT_DIRECTORY_REQUIRED" : null;
                }
                row.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            finally {parserGate.Semaphore.Release();}
        }
        if (row.Status != "prepared" || row.AcquisitionTaskId is not { } taskId) return;
        var current = await plans.GetAsync(taskId, ct);
        if (current == null) {await StateAsync(row, "attention", "HANDOFF_TARGET_UNAVAILABLE", ct); return;}
        if (current.Task.IsDeleted) {await StateAsync(row, "attention", "HANDOFF_TARGET_DELETED", ct); return;}
        // The association was persisted before submission. A restart reattaches an existing run;
        // it must never call Run again for an already-started task, nor replace a user-edited plan.
        if (current.Task.WorkflowRunId != null) {await StateAsync(row, "created", null, ct); return;}
        // A readiness decision can be revoked while initial preflight is outside the parser gate.
        // Recheck current task-owned proof and keep that decision stable until the run is submitted.
        await parserGate.Semaphore.WaitAsync(ct);
        try
        {
            if (!await CurrentAsync(row, ct))
            {await StateAsync(row, "attention", "HANDOFF_SOURCE_CHANGED", ct); return;}
            if (current.Task.PlanRevision != 1)
            {await StateAsync(row, "attention", "HANDOFF_PLAN_CHANGED", ct); return;}
            var started = await plans.RunAsync(taskId, current.Task.PlanRevision, ct);
            await StateAsync(row, started.Task.WorkflowRunId != null ? "created" : "attention",
                started.Task.WorkflowRunId != null ? null : started.Issues.FirstOrDefault(i => i.Blocking)?.Code ?? "HANDOFF_NEEDS_CONFIGURATION", ct);
        }
        finally {parserGate.Semaphore.Release();}
    }

    private async Task<bool> CurrentAsync(AcquisitionHandoffDbModel row, CancellationToken ct)
    {
        if (!int.TryParse(row.SourceId, out var id) || !int.TryParse(row.SourceRevision, out var revision)) return false;
        var post = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.Revision == revision &&
            !t.IsDeleted && t.ParsingState == "complete", ct);
        if (post == null || (post.WorkflowRunId ?? 0) != row.ProducerRunId || !SavedResultReady(post)) return false;
        var producer = await db.WorkflowRuns.AsNoTracking().Where(r => r.Id == row.ProducerRunId)
            .Select(r => new {r.Status}).SingleOrDefaultAsync(ct);
        if (row.Id.StartsWith(SavedResultIdPrefix, StringComparison.Ordinal))
            return row.Id == SavedResultId(post) && (producer == null || producer.Status == WorkflowRunStatus.Success);
        return producer?.Status == WorkflowRunStatus.Success;
    }

    private async Task<List<int>> ExistingTasksAsync(AcquisitionHandoffDbModel handoff, CancellationToken ct)
    {
        return (await ExistingPostTasksAsync(ct)).GetValueOrDefault((handoff.SourceId, handoff.SourceRevision)) ?? [];
    }

    private async Task StateAsync(AcquisitionHandoffDbModel row, string status, string? code, CancellationToken ct)
    {
        row.Status = status; row.Code = code; row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
