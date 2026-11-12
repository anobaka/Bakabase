using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;
using PostContent = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

public sealed partial class PostParserWorkflowService<TDbContext>
{
    public async Task<PostContent> ResolveReadinessAsync(PostParserInput input, int runId, PostContent content,
        CancellationToken ct, string? expectedDecisionId = null)
    {
        if (input.TaskId is not { } id)
            return PostContentReadinessPolicy.Apply(content, content.Readiness?.Basis == "analysis" ? content.Readiness : null);
        await gate.Semaphore.WaitAsync(ct);
        try
        {
        var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted &&
            t.Revision == input.Revision && t.WorkflowRunId == runId, ct)
            ?? throw new OperationCanceledException("The parsing task was removed or replaced by a newer request.");
        var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
        var before = row.ToDomainModel().ContentSnapshot;
        var originalState = state;
        if (before != null)
        {
            state = state.RecordEquivalent(before, content, evidenceCanonicalizers, out _);
            if (!PostContentReadinessPolicy.IsCurrent(content, content.Readiness))
                content = PostContentReadinessPolicy.Reconcile(content, before, evidenceCanonicalizers);
        }
        if (expectedDecisionId != null && state.Active(content)?.Id != expectedDecisionId)
            throw new InvalidOperationException("POST_CONTENT_READINESS_CHANGED: The readable content or confirmation changed. Review it again; no purchase was approved.");
        content = state.Apply(content);
        if (!ReferenceEquals(state, originalState)) await PersistReadinessUnderGateAsync(row, state, content, ct);
        return content;
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<PostParserContentReadinessView> GetContentReadinessAsync(int id, CancellationToken ct = default)
    {
        var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            ?? throw new InvalidOperationException("The parsing task no longer exists.");
        var run = row.WorkflowRunId is { } runId ? await Runs.AsNoTracking().Include(r => r.NodeExecutions).SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        return ReadinessView(row, run);
    }

    private bool ReadinessBusy(WorkflowRunDbModel? run) => run?.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending ||
        run != null && tasks.Tasks.Any(t => t.Id == $"workflow.run.{run.Id}" &&
            (t.HasAttachedExecution || !t.Task.Status.IsFinished()));

    private PostParserContentReadinessView ReadinessView(PostParserTaskDbModel row, WorkflowRunDbModel? run)
    {
        var content = row.ToDomainModel().ContentSnapshot;
        var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
        var active = content == null ? null : state.Active(content);
        var remaining = content?.Locks.Any(l => !l.IsBought) == true;
        var blocking = content != null && PostContentReadinessPolicy.HasBlockingLocks(content);
        var busy = ReadinessBusy(run);
        var canConfirm = !busy && content != null && PostParserContentReadinessState.HasStableBlockIds(content);
        PostParserInput? payload = null;
        try { payload = run?.PayloadJson == null ? null : JsonSerializer.Deserialize<PostParserInput>(run.PayloadJson, WorkflowJson.Options); }
        catch (JsonException) { }
        return new()
        {
            TaskId = row.Id, Revision = row.Revision, WorkflowRunId = row.WorkflowRunId,
            Source = content?.SourceHint ?? row.Source.ToString(), ContentSnapshot = content,
            ContentFingerprint = content == null ? null : PostContentReadinessPolicy.Fingerprint(content),
            DecisionVersion = state.Version, Assessment = content?.Readiness,
            Status = content == null ? "unknown" : blocking ? "needsUnlock" : "sufficient",
            Basis = content == null ? null : !remaining ? "unlocked" : content.Readiness?.Basis,
            ActiveDecisionId = active?.Id,
            HasStaleDecision = state.History.LastOrDefault() is {Action: "confirm"} && active == null,
            CanConfirm = canConfirm,
            CanContinue = canConfirm && run?.Status == WorkflowRunStatus.Waiting && payload?.TaskId == row.Id &&
                payload.Revision == row.Revision && PostParserPurchaseRules.CurrentActivityKind(run) == PostParserWorkflow.UnlockContent,
            CanRevoke = !busy && active != null,
            CanRefresh = !tasks.IsShuttingDown && !busy && content != null && string.IsNullOrWhiteSpace(row.Text) &&
                Uri.TryCreate(row.Link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
                contentReader.CanRead(row.Link, ReadinessSourceHint(row)),
            ParsedResultMayBeStale = state.ParsedResultMayBeStale, History = state.History,
            EvidenceEquivalences = state.EvidenceEquivalences
        };
    }

    private static string? ReadinessSourceHint(PostParserTaskDbModel row) =>
        Enum.IsDefined(row.Source) ? row.Source.ToString() : null;

    /// <summary>Refresh evidence only. No analysis, purchasing, checkpoint changes or workflow scheduling.</summary>
    public async Task<PostParserContentReadinessView> RefreshContentReadinessAsync(int id,
        RefreshPostParserContentReadinessInput input, CancellationToken ct = default)
    {
        PostParserTaskDbModel before;
        WorkflowRunDbModel? beforeRun;
        PostParserContentReadinessView beforeView;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            before = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            beforeRun = before.WorkflowRunId is { } runId
                ? await Runs.AsNoTracking().Include(r => r.NodeExecutions).SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
            beforeView = ReadinessView(before, beforeRun);
            CheckReadinessSnapshot(beforeView, input.Revision, input.WorkflowRunId, input.ExpectedContentFingerprint, input.ExpectedDecisionVersion);
            if (!beforeView.CanRefresh)
                throw new InvalidOperationException("Wait for the current operation to finish and choose a saved, readable post before refreshing its evidence.");
        }
        finally { gate.Semaphore.Release(); }

        // Do not hold the global parser gate during a remote GET. Recheck ownership and all
        // relevant snapshots afterwards so concurrent parsing, confirmation or deletion wins.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        var fresh = await contentReader.ReadAsync(before.Link, ReadinessSourceHint(before), timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            var run = row.WorkflowRunId is { } runId
                ? await Runs.AsNoTracking().Include(r => r.NodeExecutions).SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
            var view = ReadinessView(row, run);
            CheckReadinessSnapshot(view, input.Revision, input.WorkflowRunId, input.ExpectedContentFingerprint, input.ExpectedDecisionVersion);
            if (!view.CanRefresh || row.Link != before.Link || row.Text != before.Text || row.Source != before.Source ||
                row.ContentSnapshotJson != before.ContentSnapshotJson || row.ContentReadinessJson != before.ContentReadinessJson ||
                row.Results != before.Results || run?.Status != beforeRun?.Status || run?.CurrentStepIndex != beforeRun?.CurrentStepIndex)
                throw new InvalidOperationException("POST_CONTENT_READINESS_CHANGED: The post or execution changed while reading. Refresh and review it again.");
            var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
            // Readers supply evidence, never authority. Reuse an old validated decision only
            // for the exact same nonvolatile evidence; a fresh read cannot invent a proof.
            state = state.RecordEquivalent(beforeView.ContentSnapshot!, fresh, evidenceCanonicalizers, out var equivalent);
            fresh = state.Apply(PostContentReadinessPolicy.Reconcile(fresh with {Readiness = null}, beforeView.ContentSnapshot!, evidenceCanonicalizers));
            state = state with {ParsedResultMayBeStale = state.ParsedResultMayBeStale ||
                !string.IsNullOrWhiteSpace(row.Results) && !equivalent};
            await PersistReadinessUnderGateAsync(row, state, fresh, ct);
            await PublishAsync(row, run?.Status);
            return ReadinessView(row, run);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<PostParserContentReadinessView> ConfirmContentReadinessAsync(int id,
        ConfirmPostParserContentReadinessInput input, CancellationToken ct = default)
    {
        if (input.NotRequiredBlockIds == null || input.NotRequiredBlockIds.Count is 0 or > 256 ||
            input.NotRequiredBlockIds.Any(string.IsNullOrWhiteSpace) || input.NotRequiredBlockIds.Any(i => i.Length > 512) ||
            input.NotRequiredBlockIds.Distinct(StringComparer.Ordinal).Count() != input.NotRequiredBlockIds.Count ||
            (input.Reason?.Length ?? 0) > 1000)
            throw new ArgumentException("Select every current restricted block before confirming no further unlocking is needed. Optional notes must not exceed 1000 characters.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            var run = row.WorkflowRunId is { } runId ? await Runs.Include(r => r.NodeExecutions).SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
            var view = ReadinessView(row, run);
            CheckReadinessSnapshot(view, input.Revision, input.WorkflowRunId, input.ExpectedContentFingerprint, input.ExpectedDecisionVersion);
            if (!view.CanConfirm) throw new InvalidOperationException("Read the post again, or wait for its current operation to finish, before confirming its restricted blocks.");
            var content = view.ContentSnapshot!;
            var remaining = content.Locks.Where(l => !l.IsBought).Select(l => l.BlockId!).ToHashSet(StringComparer.Ordinal);
            if (!remaining.SetEquals(input.NotRequiredBlockIds))
                throw new InvalidOperationException("The restricted block selection changed. Review every remaining block again.");
            PostParserInput? payload = null;
            if (input.ContinueParsing)
            {
                if (tasks.IsShuttingDown || run?.Status != WorkflowRunStatus.Waiting ||
                    PostParserPurchaseRules.CurrentActivityKind(run) != PostParserWorkflow.UnlockContent)
                    throw new InvalidOperationException("Only the same waiting content-unlock step can continue from this confirmation.");
                payload = JsonSerializer.Deserialize<PostParserInput>(run.PayloadJson ?? "null", WorkflowJson.Options);
                if (payload?.TaskId != id || payload.Revision != row.Revision)
                    throw new InvalidOperationException("The parsing execution changed. Refresh before continuing.");
            }
            var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
            if (state.History.Count >= 1000) throw new InvalidOperationException("The confirmation history is full; preserve it before adding more decisions.");
            var entry = new PostParserContentReadinessEvent {Version = state.Version + 1, ContentFingerprint = view.ContentFingerprint!,
                SourceRevision = row.Revision, WorkflowRunId = row.WorkflowRunId,
                BlockIds = input.NotRequiredBlockIds.ToList(), Reason = input.Reason?.Trim() ?? ""};
            state = state with {History = state.History.Append(entry).ToList()};
            content = state.Apply(content);
            await PersistReadinessUnderGateAsync(row, state, content, ct);
            if (input.ContinueParsing)
            {
                // A readiness signal grants no purchase authority. A fresh read must still match this exact decision.
                await resumer.ResumeAsync(run!.Id, JsonSerializer.Serialize(new PostParserPurchaseSignal
                    {ContentReadinessDecisionId = entry.Id}, WorkflowJson.Options), ct);
            }
            await PublishAsync(row, input.ContinueParsing ? WorkflowRunStatus.Pending : run?.Status);
            return ReadinessView(row, run);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<PostParserContentReadinessView> RevokeContentReadinessAsync(int id,
        RevokePostParserContentReadinessInput input, CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await ParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new InvalidOperationException("The parsing task no longer exists.");
            var run = row.WorkflowRunId is { } runId ? await Runs.AsNoTracking().Include(r => r.NodeExecutions).SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
            var view = ReadinessView(row, run);
            CheckReadinessSnapshot(view, input.Revision, input.WorkflowRunId, input.ExpectedContentFingerprint, input.ExpectedDecisionVersion);
            if (!view.CanRevoke || view.ActiveDecisionId != input.DecisionId)
                throw new InvalidOperationException("The confirmation is no longer current, or the parsing operation is still running.");
            var state = PostParserContentReadinessState.Read(row.ContentReadinessJson);
            if (state.History.Count >= 1000) throw new InvalidOperationException("The confirmation history is full.");
            state = state with {History = state.History.Append(new PostParserContentReadinessEvent
            {
                Version = state.Version + 1, Action = "revoke", DecisionId = input.DecisionId,
                ContentFingerprint = view.ContentFingerprint!, SourceRevision = row.Revision, WorkflowRunId = row.WorkflowRunId,
                Reason = "The user revoked this confirmation."
            }).ToList()};
            await PersistReadinessUnderGateAsync(row, state, state.Apply(view.ContentSnapshot!), ct);
            await PublishAsync(row, run?.Status);
            return ReadinessView(row, run);
        }
        finally { gate.Semaphore.Release(); }
    }

    private static void CheckReadinessSnapshot(PostParserContentReadinessView view, int revision, int? runId, string hash, int version)
    {
        var evidenceMatches = view.ContentFingerprint == hash ||
            new PostParserContentReadinessState {EvidenceEquivalences = view.EvidenceEquivalences}
                .MatchesFingerprint(hash, view.ContentFingerprint ?? "");
        if (view.Revision != revision || view.WorkflowRunId != runId || !evidenceMatches || view.DecisionVersion != version)
            throw new InvalidOperationException("POST_CONTENT_READINESS_CHANGED: The post, execution or confirmation changed. Refresh and review it again.");
    }

    private async Task PersistReadinessUnderGateAsync(PostParserTaskDbModel row, PostParserContentReadinessState state,
        PostContent content, CancellationToken ct)
    {
        var previous = row.ContentReadinessJson;
        var next = JsonSerializer.Serialize(state.WithProjection(content), WorkflowJson.Options);
        var snapshot = JsonSerializer.Serialize(content, WorkflowJson.Options);
        var changed = await ParserTasks.Where(t => t.Id == row.Id && !t.IsDeleted && t.Revision == row.Revision &&
            t.WorkflowRunId == row.WorkflowRunId && t.ContentReadinessJson == previous && t.ContentSnapshotJson == row.ContentSnapshotJson)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ContentReadinessJson, next).SetProperty(t => t.ContentSnapshotJson, snapshot), ct);
        if (changed != 1) throw new InvalidOperationException("POST_CONTENT_READINESS_CHANGED: The post changed. Refresh and review it again.");
        row.ContentReadinessJson = next;
        row.ContentSnapshotJson = snapshot;
        cache.ClearCache();
    }
}
