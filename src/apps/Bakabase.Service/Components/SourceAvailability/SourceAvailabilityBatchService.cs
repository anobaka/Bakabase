using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.SourceAvailability;

public sealed class SourceAvailabilityBatchService(BakabaseDbContext db, SourceAvailabilityStore store,
    SourceAvailabilityService availability, BTaskManager tasks, IBakabaseLocalizer localizer)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<List<SourceAvailabilityRow>> GetAsync(int taskId, CancellationToken ct) =>
        (await SnapshotAsync([taskId], ct)).Select(s => s.Row).ToList();
    public async Task<SourceAvailabilityBatchView?> GetBatchAsync(string id, CancellationToken ct)
    {
        ValidateId(id);
        var batch = await store.BatchAsync(id, ct);
        if (batch?.Status == "checking" && tasks.GetTaskViewModel("SourceAvailability:" + id) == null)
        {
            batch = batch with {Status = "failed", ExpiresAt = DateTimeOffset.UtcNow};
            await store.SaveBatchAsync(batch, ct);
        }
        return batch?.View();
    }
    public async Task<SourceAvailabilityBatchView> PreviewAsync(SourceAvailabilityPreviewInput input, CancellationToken ct)
    {
        if (input.TaskIds == null || input.TaskIds.Count is < 1 or > 2000 || input.TaskIds.Any(id => id <= 0))
            throw new ArgumentException("SOURCE_AVAILABILITY_SELECTION_INVALID");
        if (tasks.IsShuttingDown) throw new InvalidOperationException("SOURCE_AVAILABILITY_SERVER_STOPPING");
        var batch = new SourceAvailabilityBatch {BatchId = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2), Force = input.Force,
            Items = await SnapshotAsync(input.TaskIds.Distinct().ToList(), ct)};
        if (batch.Items.Count > 10000) throw new ArgumentException("SOURCE_AVAILABILITY_SELECTION_TOO_LARGE");
        await store.SaveBatchAsync(batch, ct);
        var id = batch.BatchId;
        await tasks.Enqueue(BTaskBuilder.Create("SourceAvailability:" + id)
            .Named(() => localizer.BTask_Name("SourceAvailability"))
            .Persistent().WithConcurrencyLimit("source-availability-checks", () => 1)
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<SourceAvailabilityBatchService>();
                await service.CheckBatchAsync(id, args);
            }));
        return batch.View();
    }
    private async Task CheckBatchAsync(string id, BTaskArgs args)
    {
        var batch = await store.BatchAsync(id, args.CancellationToken) ?? throw new InvalidOperationException("SOURCE_AVAILABILITY_BATCH_MISSING");
        try
        {
            for (var offset = 0; offset < batch.Items.Count; offset += 4)
            {
                await args.YieldAsync();
                var chunk = batch.Items.Skip(offset).Take(4).ToArray();
                var checkedItems = await Task.WhenAll(chunk.Select(async item =>
                {
                    var observed = item.IsPreparation
                        ? await availability.CheckPreparationAsync(item.Preparation, item.Source, batch.Force, args.CancellationToken)
                        : await availability.CheckAsync(item.Source, batch.Force, args.CancellationToken);
                    return item with {Row = await ProjectAsync(item.Row, item.Source, observed, args.CancellationToken)};
                }));
                for (var i = 0; i < checkedItems.Length; i++) batch.Items[offset + i] = checkedItems[i];
                batch = batch with {Processed = offset + checkedItems.Length};
                await store.SaveBatchAsync(batch, args.CancellationToken);
                await args.UpdateTask(t => t.Percentage = batch.Processed * 100 / Math.Max(1, batch.Items.Count));
            }
            batch = batch with {Status = "ready", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20)};
            await store.SaveBatchAsync(batch, args.CancellationToken);
        }
        catch
        {
            await store.SaveBatchAsync(batch with {Status = "failed", ExpiresAt = DateTimeOffset.UtcNow}, CancellationToken.None);
            throw;
        }
    }
    public async Task<SourceAvailabilityApplyResult> ApplyAsync(SourceAvailabilityApplyInput input, CancellationToken ct)
    {
        ValidateId(input.BatchId);
        if (input.Action is not ("disable" or "restore") || input.SelectedKeys == null ||
            input.SelectedKeys.Count is < 1 or > 10000 || input.SelectedKeys.Distinct().Count() != input.SelectedKeys.Count)
            throw new ArgumentException("SOURCE_AVAILABILITY_SELECTION_INVALID");
        var batch = await store.BatchAsync(input.BatchId, ct);
        if (batch == null || batch.Status != "ready" || batch.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("SOURCE_AVAILABILITY_PREVIEW_EXPIRED");
        var wanted = input.SelectedKeys.ToHashSet(StringComparer.Ordinal);
        var selected = batch.Items.Where(i => wanted.Contains(i.Row.Key)).ToList();
        if (selected.Count != wanted.Count) throw new ArgumentException("SOURCE_AVAILABILITY_SELECTION_CHANGED");
        var result = new SourceAvailabilityApplyResult([], []);
        foreach (var group in selected.GroupBy(i => i.Row.TaskId))
        {
            var gate = AcquisitionTaskGate.For(group.Key);
            await gate.WaitAsync(ct);
            try
            {
                var current = await SnapshotAsync([group.Key], ct);
                foreach (var expected in group)
                {
                    var now = current.SingleOrDefault(i => i.Row.Key == expected.Row.Key);
                    var code = now == null || now.Revision != expected.Revision || now.PlanHash != expected.PlanHash
                        ? "SOURCE_CHANGED" : now.Row.Protected ? "SOURCE_IN_USE" :
                        now.Row.DecisionVersion != expected.Row.DecisionVersion ? "DECISION_CHANGED" : null;
                    if (code == null && input.Action == "disable" &&
                        (!expected.Row.CanDisable || now!.Row.Disabled || !SourceAvailabilityService.IsConfirmedUnavailable(expected.Row.Availability) ||
                         !SourceAvailabilityService.IsConfirmedUnavailable(now.Row.Availability) || now.Row.Availability != expected.Row.Availability &&
                         JsonSerializer.Serialize(now.Row.Availability, Json) != JsonSerializer.Serialize(expected.Row.Availability, Json))) code = "EVIDENCE_CHANGED";
                    if (code == null && input.Action == "restore" && (!expected.Row.CanRestore || !now!.Row.Disabled)) code = "DECISION_CHANGED";
                    if (code != null) {result.Skipped.Add(new(expected.Row.Key, code)); continue;}
                    var decision = new SourceAvailabilityDecision(Guid.NewGuid().ToString("N"), input.Action == "disable", DateTimeOffset.UtcNow,
                        batch.BatchId, expected.Row.Availability);
                    var saved = await store.DecideAsync(SourceAvailabilityService.DecisionKey(Context(group.Key), now!.Source),
                        expected.Row.DecisionVersion, decision, ct);
                    if (saved) result.AppliedKeys.Add(expected.Row.Key);
                    else result.Skipped.Add(new(expected.Row.Key, "DECISION_CHANGED"));
                }
            }
            finally { gate.Release(); }
        }
        return result;
    }
    internal async Task<List<SourceAvailabilitySnapshot>> SnapshotAsync(List<int> ids, CancellationToken ct)
    {
        var tasksFound = await db.Set<AcquisitionTaskDbModel>().AsNoTracking().Where(t => t.DeletedAt == null && ids.Contains(t.Id)).ToListAsync(ct);
        var runIds = tasksFound.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => runIds.Contains(r.Id)).Select(r => new {r.Id, r.Status}).ToDictionaryAsync(r => r.Id, ct);
        var correctedRuns = await AcquisitionEffectiveSourceProjection.ReadCorrectedRunsAsync(db, runIds, ct);
        var output = new List<SourceAvailabilitySnapshot>();
        foreach (var task in tasksFound.OrderBy(t => t.Id))
        {
            var active = task.Status is AcquisitionStatus.Running or AcquisitionStatus.Completed ||
                task.WorkflowRunId is { } runId && (!runs.TryGetValue(runId, out var run) || run.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending or WorkflowRunStatus.Success);
            AcquisitionPlan? plan;
            var snapshotJson = task.PlanJson ?? task.BuilderStateJson ?? "";
            try
            {
                plan = task.PlanJson != null ? JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, Json) : null;
                if (plan == null && task.BuilderStateJson != null)
                {
                    var draft = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(task.BuilderStateJson, Json);
                    plan = new() {Title = task.Title ?? "", Parts = (draft?.DraftSources ?? []).Select(s => new AcquisitionPlanPart
                    {Id = "draft:" + s.Id, Sources = [new() {Id = s.Id, Url = s.Url, Kind = s.Kind, ProviderId = s.ProviderId,
                        MethodId = s.MethodId, AccountId = s.AccountId, AccessCode = s.AccessCode}]}).ToList()};
                }
            }
            catch (JsonException) {continue;}
            if (plan == null) continue;
            if (task.WorkflowRunId is {} owner && correctedRuns.TryGetValue(owner, out var current))
            {
                plan = AcquisitionEffectiveSourceProjection.Apply(plan, current);
                // A preview for the old credential cannot authorize a decision about its correction.
                snapshotJson = JsonSerializer.Serialize(new {Original = snapshotJson, Effective = plan}, Json);
            }
            var planHash = SourceAvailabilityStore.Hash(snapshotJson);
            var isPreparation = task.WorkflowRunId == null && task.Status == AcquisitionStatus.Waiting;
            foreach (var part in plan.Parts)
            foreach (var source in part.Sources)
            {
                CloudSource resolved;
                try { resolved = DownloadMethodRegistry.Normalize(new DownloadCandidate {Id = source.Id, Url = source.Url,
                    Kind = source.Kind, ProviderId = source.ProviderId, MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode}); }
                catch (ArgumentException) {resolved = new() {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId ?? "unknown",
                    Kind = source.Kind, MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode};}
                var row = new SourceAvailabilityRow {Key = SourceAvailabilityStore.Hash($"{task.Id}:{part.Id}:{source.Id}"),
                    TaskId = task.Id, Title = task.Title ?? plan.Title, PartId = part.Id, SourceId = source.Id,
                    Url = source.Url, AccessCode = source.AccessCode, ProviderId = resolved.ProviderId, Protected = active};
                var preparation = isPreparation ? await availability.GetPreparationCheckAsync(new(task.Id, task.PlanRevision, planHash, part.Id), resolved, ct) : null;
                row = await ProjectAsync(row, resolved, preparation ?? await availability.GetAsync(resolved, ct), ct);
                output.Add(new(row, resolved, task.PlanRevision, planHash, isPreparation));
            }
        }
        return output;
    }
    private async Task<SourceAvailabilityRow> ProjectAsync(SourceAvailabilityRow row, CloudSource source,
        SourceAvailabilityResult? result, CancellationToken ct)
    {
        var decision = await availability.GetDecisionAsync(Context(row.TaskId), source, ct);
        var disabled = decision?.Disabled == true;
        return row with {Availability = result, Disabled = disabled, DecisionVersion = decision?.Version,
            CanDisable = !row.Protected && !disabled && SourceAvailabilityService.IsConfirmedUnavailable(result),
            CanRestore = !row.Protected && disabled,
            BlockReason = row.Protected ? "SOURCE_IN_USE" : disabled ? "SOURCE_DISABLED" :
                SourceAvailabilityService.IsConfirmedUnavailable(result) ? null : "SOURCE_NOT_CONFIRMED_UNAVAILABLE"};
    }
    private static string Context(int id) => $"acquisition:{id}";
    private static void ValidateId(string id)
    {if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("SOURCE_AVAILABILITY_BATCH_INVALID");}
}
