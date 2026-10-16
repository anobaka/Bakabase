using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Federation.Processing;

public sealed record ResourceProcessingStart(int ProfileId, ResourceProcessingKind Kind);
public sealed record ResourceProcessingControl(string Action, long ExpectedVersion);
public sealed record ResourceProcessingApply(string Token, ResourceProcessingOutput Output);
public sealed record ResourceProcessingWorkerResult(ResourceProcessingJobView Job, ResourceProcessingOutput? Output);
public sealed record ResourceProcessingArtifactRead(string Hash, long Offset);
public sealed record ResourceProcessingReceiptRequest(string Token);
public sealed record ResourceProcessingAbandonRequest(string? Token);
public sealed record ResourceProcessingReceiptStatus(ResourceProcessingReceipt? Receipt);
public sealed record ResourceProcessingWorkerControl(string Action);

/// <summary>One disk checkpoint per batch, with at most one partition and one result in memory.
/// Only explicitly paired management connections carry work; ownership never moves to a worker.</summary>
public sealed class ResourceProcessingCoordinator(IServiceProvider services)
{
    private readonly SemaphoreSlim _runner = new(1, 1);
    private bool _recovered;
    public static string LocalRequester(string nodeId) => "local:" + nodeId;

    public async Task<ResourceProcessingJobView> StartAsync(ResourceProcessingStart input, CancellationToken ct)
    {
        if (!Enum.IsDefined(input.Kind)) throw new ResourceProcessingException("InvalidKind", "Choose a processing rule.");
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var self = await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct);
        var profile = await sp.GetRequiredService<IResourceProfileService>().Get(input.ProfileId)
            ?? throw new ResourceProcessingException("RuleMissing", "This rule no longer exists.");
        var target = input.Kind == ResourceProcessingKind.PlayableFiles
            ? profile.PlayableFileOptions?.ExecutionTarget : profile.EnhancerOptions?.ExecutionTarget;
        // An explicit run can use the ordinary owner default, but never guess a foreign worker.
        target ??= new(ResourceProcessingExecutionKind.Owner);
        var owner = new ReplicaIdentity(profile.SourceDeviceId ?? self.NodeId, profile.SourceLibraryEpoch ?? self.LibraryEpoch);
        if (owner.ServerId != self.NodeId)
            await sp.GetRequiredService<MetadataReplicaCoordinator>().RequireAsync(owner.ServerId, owner.LibraryEpoch, ct);
        await ValidateTargetAsync(sp, owner, target, ct);
        var scopeRevision = profile.ResourceScopeId == null ? 0 :
            (await sp.GetRequiredService<IResourceScopeService>().GetAll()).Single(s => s.Id == profile.ResourceScopeId).Revision;
        var plan = new ResourceProcessingBatchPlan
        {
            ProfileId = profile.Id, ProfileUpdatedAt = profile.UpdatedAt, ScopeRevision = scopeRevision,
            Owner = owner, Target = target, SearchJson = profile.SearchJson, Kind = input.Kind,
            PlayableFiles = profile.PlayableFileOptions,
            Enhancers = OrderEnhancers(profile.EnhancerOptions?.Enhancers ?? [])
        };
        if (input.Kind == ResourceProcessingKind.PlayableFiles && plan.PlayableFiles == null ||
            input.Kind == ResourceProcessingKind.Enhancement && plan.Enhancers.Length == 0)
            throw new ResourceProcessingException("RuleEmpty", "Configure this rule before running it.");
        plan.RuleHash = RuleHash(profile, scopeRevision, input.Kind);
        var row = await sp.GetRequiredService<ResourceProcessingJobStore>().CreateBatchAsync(
            LocalRequester(self.NodeId), Guid.NewGuid().ToString("N"), owner.ServerId, owner.LibraryEpoch,
            plan.RuleHash, input.Kind, ResourceProcessingWire.Serialize(plan), ct);
        return View(row);
    }

    public async Task<ResourceProcessingJobView> ControlAsync(string jobId, ResourceProcessingControl input, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var self = await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct);
        var store = sp.GetRequiredService<ResourceProcessingJobStore>();
        var row = await store.GetAsync(ResourceProcessingRole.Manager, LocalRequester(self.NodeId), jobId, ct)
            ?? throw new ResourceProcessingException("JobMissing", "The job no longer exists.");
        if (row.Version != input.ExpectedVersion) throw new ResourceProcessingException("JobChanged", "The task changed. Refresh before continuing.");
        switch (input.Action)
        {
            case "pause" when row.State is "Queued" or "Running":
                row.PauseRequested = true;
                if (row.State == "Queued") row.State = "Paused";
                break;
            case "resume" when row.State == "Paused":
                row.PauseRequested = false; row.State = "Queued"; break;
            case "cancel" when row.State is "Queued" or "Running" or "Paused" or "Failed" or "Conflict":
                row.CancelRequested = true;
                if (row.State != "Running") row.State = "Cancelled";
                break;
            case "retry" when row.State == "Failed":
                var retryPlan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
                retryPlan.RetryWorkerRequested = true;
                row.InputJson = ResourceProcessingWire.Serialize(retryPlan);
                row.State = "Queued"; row.Error = null; row.ErrorCode = null; row.PauseRequested = false; break;
            default: throw new ResourceProcessingException("InvalidState", "This task cannot perform that action in its current state.");
        }
        await store.UpdateAsync(row, ct);
        // A paused coordinator never applies results. Also ask an already queued remote
        // worker to pause/cancel; an unavailable worker's result remains unapplied.
        if (input.Action is "pause" or "cancel" or "resume")
        {
            var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
            var worker = WorkerId(plan, self.NodeId);
            if (plan.Token != null && worker != self.NodeId && plan.PendingIndex < plan.PendingIds.Length)
            {
                try
                {
                    await SendAsync<bool>(sp.GetRequiredService<IManagedServerService>(), worker, HttpMethod.Post,
                        WorkerPath($"/management/resource-processing/worker/{StepId(row.JobId, plan.PendingIds[plan.PendingIndex], plan.StepIndex)}/control", WorkerEpoch(plan)),
                        new ResourceProcessingWorkerControl(input.Action), ct);
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
            }
        }
        return View(row);
    }

    public async Task RunCycleAsync(Func<ResourceProcessingJobView, Task> progress, Func<Task> yield, CancellationToken ct)
    {
        if (!await _runner.WaitAsync(0, ct)) return;
        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var store = sp.GetRequiredService<ResourceProcessingJobStore>();
            if (!_recovered)
            {
                await store.RecoverInterruptedAsync(ResourceProcessingRole.Manager, ct);
                await store.RecoverInterruptedAsync(ResourceProcessingRole.Worker, ct);
                _recovered = true;
            }
            await CleanupCancelledAsync(sp, ct);
            // Several cheap steps share a scheduling turn; a large library is not throttled
            // to one resource per polling interval. Each step gets a fresh ORM scope.
            var until = DateTime.UtcNow.AddSeconds(15);
            for (var step = 0; step < 128 && DateTime.UtcNow < until; step++)
            {
                await yield();
                await using var next = services.CreateAsyncScope();
                var nextSp = next.ServiceProvider;
                var worker = await NextReadyAsync(nextSp, ResourceProcessingRole.Worker, ct);
                if (worker != null) await RunWorkerAsync(nextSp, worker, progress, yield, ct);
                var manager = await NextReadyAsync(nextSp, ResourceProcessingRole.Manager, ct);
                if (manager != null) await RunManagerAsync(nextSp, manager, progress, yield, ct);
                if (worker == null && manager == null) break;
            }
        }
        finally { _runner.Release(); }
    }

    private static Task<ResourceProcessingJobDbModel?> NextReadyAsync(IServiceProvider sp, ResourceProcessingRole role, CancellationToken ct)
    {
        var retryBefore = DateTime.UtcNow.AddSeconds(-15);
        var pollBefore = DateTime.UtcNow.AddSeconds(-3);
        return sp.GetRequiredService<BakabaseDbContext>().ResourceProcessingJobs.AsNoTracking()
            .Where(j => j.Role == role.ToString() && j.State == "Queued" && !j.PauseRequested && !j.CancelRequested &&
                (j.Phase != "WaitingForDevice" || j.UpdatedAtUtc < retryBefore) &&
                (j.Phase != "WaitingForWorker" || j.UpdatedAtUtc < pollBefore))
            .OrderBy(j => j.UpdatedAtUtc).ThenBy(j => j.Id).FirstOrDefaultAsync(ct);
    }

    internal async Task RunWorkerAsync(IServiceProvider sp, ResourceProcessingJobDbModel row,
        Func<ResourceProcessingJobView, Task> progress, Func<Task> yield, CancellationToken ct)
    {
        var store = sp.GetRequiredService<ResourceProcessingJobStore>();
        try
        {
            await RequireWorkerRequesterAsync(sp, row.RequesterId, ct);
            var saved = ResourceProcessingWorkerEnvelope.Read(row.InputJson);
            saved.RequireIdentity(await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct));
            row.State = "Running"; row.Attempt++;
            await store.UpdateAsync(row, ct);
            var prepared = saved.Prepared;
            var result = await sp.GetRequiredService<ResourceProcessingWorker>().ExecuteAsync(prepared,
                async (p, token) =>
                {
                    await yield();
                    await RequireWorkerRequesterAsync(sp, row.RequesterId, token);
                    saved.RequireIdentity(await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(token));
                    row = await ReloadRunningAsync(store, row, token);
                    row.Phase = p.Phase; row.ProcessedCount = p.ProcessedCount; row.TotalCount = p.TotalCount;
                    await store.UpdateAsync(row, token); await progress(View(row));
                }, ct);
            row = await ReloadRunningAsync(store, row, ct);
            saved.RequireIdentity(await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct));
            row.ResultJson = ResourceProcessingWire.Serialize(result); row.State = "Completed"; row.Phase = "Completed";
            row.ProcessedCount = result.ProcessedCount;
            await store.UpdateAsync(row, ct);
        }
        catch (Exception error)
        {
            await SaveFailureAsync(store, row, error, ct);
            if (error is ResourceProcessingException {Code: "EpochChanged"})
            {
                // Execution has not started, or has cooperatively unwound. Old work
                // cannot be resumed on the new library, so do not retain its quota.
                var expired = await store.GetByIdAsync(row.Id, CancellationToken.None);
                if (expired != null && expired.InputJson != "{}")
                {
                    try
                    {
                        var saved = ResourceProcessingWorkerEnvelope.Read(expired.InputJson);
                        sp.GetRequiredService<ResourceProcessingArtifacts>().Cleanup(saved.Prepared, "worker");
                        expired.InputJson = "{}"; expired.ResultJson = null;
                        await store.UpdateAsync(expired, CancellationToken.None);
                    }
                    catch (Exception cleanupError) when (cleanupError is not OperationCanceledException)
                    {
                        // Keep the bounded preparation if cleanup failed; never allow
                        // its failed identity check to become runnable again.
                    }
                }
            }
        }
        await progress(View(row));
    }

    internal async Task RunManagerAsync(IServiceProvider sp, ResourceProcessingJobDbModel row,
        Func<ResourceProcessingJobView, Task> progress, Func<Task> yield, CancellationToken ct)
    {
        var store = sp.GetRequiredService<ResourceProcessingJobStore>();
        try
        {
            row.State = "Running"; row.Attempt++;
            await store.UpdateAsync(row, ct);
            var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
            var self = await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct);
            if (row.RequesterId != LocalRequester(self.NodeId))
                throw new ResourceProcessingException("Conflict", "This task belongs to the device identity used before the data directory was reset.");
            await CleanupAcknowledgedAsync(sp, row, plan, self.NodeId, ct);
            // Reconcile an already committed result before checking whether its rule changed.
            // Counting a saved result does not authorize a new write under an old rule.
            if (plan.Token != null && row.ResultJson != null && plan.PendingIndex < plan.PendingIds.Length)
            {
                var saved = PreparedFor(plan, row.JobId);
                var applied = await ReceiptAsync(sp, plan, saved.Input.JobId, self.NodeId, ct);
                if (applied != null)
                {
                    if (applied.ResultHash != ResourceProcessingWire.Hash(ResourceProcessingWire.Deserialize<ResourceProcessingOutput>(row.ResultJson)))
                        throw new ResourceProcessingException("Conflict", "The source accepted a different result for this work id.");
                    await AdvanceAppliedAsync(store, row, plan, saved, self.NodeId, ct);
                    await CleanupAcknowledgedAsync(sp, row, plan, self.NodeId, ct);
                    return;
                }
            }
            await CheckRuleAsync(sp, plan);
            if (plan.Owner.ServerId != self.NodeId)
                await sp.GetRequiredService<MetadataReplicaCoordinator>().RequireAsync(plan.Owner.ServerId, plan.Owner.LibraryEpoch, ct);
            else if (plan.Owner.LibraryEpoch != self.LibraryEpoch)
                throw new ResourceProcessingException("Conflict", "The resource library was replaced.");
            await ValidateTargetAsync(sp, plan.Owner, plan.Target, ct);
            await yield();
            row = await ReloadRunningAsync(store, row, ct);
            if (plan.PendingIndex >= plan.PendingIds.Length)
            {
                if (plan.ScanComplete) { row.State = "Completed"; row.Phase = "Completed"; row.TotalCount = row.ProcessedCount; row.InputJson = "{}"; row.ResultJson = null; await store.UpdateAsync(row, ct); return; }
                row.Phase = "Scanning";
                await ScanAsync(sp, plan, self.NodeId, ct);
                await SavePlanAsync(store, row, plan, ct);
                if (plan.PendingIds.Length == 0) { row.State = "Queued"; await store.UpdateAsync(row, ct); return; }
            }
            var resourceId = plan.PendingIds[plan.PendingIndex];
            row.ResourceId = resourceId;
            var stepId = StepId(row.JobId, resourceId, plan.StepIndex);
            var input = new ResourceProcessingInput(stepId, plan.Owner.ServerId, plan.Owner.LibraryEpoch,
                resourceId, null, plan.RuleHash, plan.Kind,
                plan.Kind == ResourceProcessingKind.PlayableFiles ? plan.PlayableFiles : null,
                plan.Kind == ResourceProcessingKind.Enhancement ? plan.Enhancers[plan.StepIndex] : null);
            var workerId = WorkerId(plan, self.NodeId);
            var transport = sp.GetRequiredService<IManagedServerService>();
            ResourceProcessingPrepared prepared;
            if (plan.Token == null)
            {
                row.Phase = "Preparing"; await SavePlanAsync(store, row, plan, ct); await progress(View(row));
                prepared = plan.Owner.ServerId == self.NodeId
                    ? await sp.GetRequiredService<ResourceProcessingOwner>().PrepareAsync(LocalRequester(self.NodeId), input, ct)
                    : await SendAsync<ResourceProcessingPrepared>(transport, plan.Owner.ServerId, HttpMethod.Post,
                        "/management/resource-processing/owner/prepare", input, ct);
                plan.Token = prepared.Token; plan.ResourceRevision = prepared.ResourceRevision; plan.CatalogGeneration = prepared.CatalogGeneration;
                await SavePlanAsync(store, row, plan, ct);
            }
            else prepared = new(input, plan.Token, plan.ResourceRevision, plan.CatalogGeneration!, "");
            if (row.ResultJson == null)
            {
                // Re-fetch the same immutable preparation after a crash; never synthesize owner metadata.
                if (prepared.ResourceJson.Length == 0)
                    prepared = plan.Owner.ServerId == self.NodeId
                        ? await sp.GetRequiredService<ResourceProcessingOwner>().PrepareAsync(LocalRequester(self.NodeId), input, ct)
                        : await SendAsync<ResourceProcessingPrepared>(transport, plan.Owner.ServerId, HttpMethod.Post,
                            "/management/resource-processing/owner/prepare", input, ct);
                row.Phase = "Processing"; await SavePlanAsync(store, row, plan, ct); await progress(View(row));
                ResourceProcessingOutput output;
                if (workerId == self.NodeId)
                    output = await sp.GetRequiredService<ResourceProcessingWorker>().ExecuteAsync(prepared, async (p, token) =>
                    {
                        await yield(); row = await ReloadRunningAsync(store, row, token);
                        SetStepProgress(row, p);
                        await store.UpdateAsync(row, token); await progress(View(row));
                    }, ct);
                else
                {
                    var status = await QueueWorkerAsync(transport, workerId, prepared, plan.RetryWorkerRequested, ct, WorkerEpoch(plan));
                    if (plan.RetryWorkerRequested)
                    {
                        plan.RetryWorkerRequested = false;
                        await SavePlanAsync(store, row, plan, ct);
                    }
                    if (status.Job.State is "Failed" or "Conflict" or "Cancelled")
                        throw new ResourceProcessingException(status.Job.State == "Conflict" ? "Conflict" : "WorkerFailed", status.Job.Error ?? "The worker stopped before returning a result.");
                    if (status.Output == null)
                    {
                        row.State = "Queued"; row.Phase = "WaitingForWorker";
                        SetStepProgress(row, new(status.Job.Phase ?? "Preparing", status.Job.ProcessedCount, status.Job.TotalCount));
                        await SavePlanAsync(store, row, plan, ct); return;
                    }
                    output = status.Output;
                }
                row = await ReloadRunningAsync(store, row, ct);
                row.ResultJson = ResourceProcessingWire.Serialize(output);
                await SavePlanAsync(store, row, plan, ct);
            }
            var result = ResourceProcessingWire.Deserialize<ResourceProcessingOutput>(row.ResultJson);
            var receipt = await ReceiptAsync(sp, plan, stepId, self.NodeId, ct);
            if (receipt != null && receipt.ResultHash != ResourceProcessingWire.Hash(result))
                throw new ResourceProcessingException("Conflict", "The source already accepted a different result for this work id.");
            row.Phase = "Transferring"; await SavePlanAsync(store, row, plan, ct);
            foreach (var artifact in receipt == null ? result.Artifacts : [])
            {
                long offset = 0;
                // Empty generated files also need an authenticated manifest on the owner.
                var firstChunk = true;
                while (firstChunk || offset < artifact.Length)
                {
                    firstChunk = false;
                    await yield(); row = await ReloadRunningAsync(store, row, ct);
                    string encoded;
                    if (workerId == self.NodeId)
                    {
                        await using var stream = sp.GetRequiredService<ResourceProcessingArtifacts>().OpenWorkerArtifact(prepared, artifact.Hash);
                        stream.Position = offset;
                        var bytes = new byte[(int)Math.Min(ResourceProcessingWire.ArtifactChunkBytes, artifact.Length - offset)];
                        await stream.ReadExactlyAsync(bytes, ct); encoded = Convert.ToBase64String(bytes);
                    }
                    else encoded = (await SendAsync<ResourceProcessingArtifactChunk>(transport, workerId, HttpMethod.Post,
                        WorkerPath($"/management/resource-processing/worker/{stepId}/artifact", WorkerEpoch(plan)), new ResourceProcessingArtifactRead(artifact.Hash, offset), ct)).Base64;
                    var chunk = new ResourceProcessingArtifactChunk(stepId, plan.Token!, artifact, offset, encoded);
                    var next = plan.Owner.ServerId == self.NodeId
                        ? await sp.GetRequiredService<ResourceProcessingOwner>().PutArtifactChunkAsync(LocalRequester(self.NodeId), chunk, ct)
                        : await SendAsync<long>(transport, plan.Owner.ServerId, HttpMethod.Post,
                            "/management/resource-processing/owner/artifact", chunk, ct);
                    if (next < offset || next == offset && artifact.Length != 0 || next > artifact.Length) throw new ResourceProcessingException("InvalidArtifact", "The artifact transfer did not advance safely.");
                    offset = next;
                }
            }
            if (receipt == null) await CheckRuleAsync(sp, plan);
            row = await ReloadRunningAsync(store, row, ct);
            row.Phase = "Applying"; await SavePlanAsync(store, row, plan, ct); await progress(View(row));
            // Output is durable before apply. A lost response retries the same id and token,
            // and the owner returns its transactional receipt without applying a second time.
            if (receipt == null)
                _ = plan.Owner.ServerId == self.NodeId
                    ? await sp.GetRequiredService<ResourceProcessingOwner>().ApplyAsync(LocalRequester(self.NodeId), stepId, plan.Token!, result, ct)
                    : await SendAsync<ResourceProcessingReceipt>(transport, plan.Owner.ServerId, HttpMethod.Post,
                        $"/management/resource-processing/owner/{stepId}/apply", new ResourceProcessingApply(plan.Token!, result), ct);
            await AdvanceAppliedAsync(store, row, plan, prepared, self.NodeId, ct);
            await CleanupAcknowledgedAsync(sp, row, plan, self.NodeId, ct);
        }
        catch (Exception error) { await SaveFailureAsync(store, row, error, ct); }
        await progress(View(row));
    }

    private static async Task ScanAsync(IServiceProvider sp, ResourceProcessingBatchPlan plan, string localId, CancellationToken ct)
    {
        plan.PendingIndex = 0;
        if (plan.Owner.ServerId == localId)
        {
            var db = sp.GetRequiredService<BakabaseDbContext>();
            plan.UpperId ??= await db.ResourcesV2.MaxAsync(r => (int?)r.Id, ct) ?? 0;
            var ids = await db.ResourcesV2.AsNoTracking().Where(r => r.Id > plan.AfterId && r.Id <= plan.UpperId)
                .OrderBy(r => r.Id).Select(r => r.Id).Take(256).ToArrayAsync(ct);
            if (ids.Length == 0) { plan.PendingIds = []; plan.ScanComplete = true; return; }
            var evaluated = await sp.GetRequiredService<IResourceProfileSourceEvaluator>().EvaluateAsync(new()
            { ResourceIds = ids, Profiles = [new() {Id = plan.ProfileId, SearchJson = plan.SearchJson}] }, ct);
            plan.PendingIds = evaluated.Matches.Where(m => m.ProfileIds.Contains(plan.ProfileId)).Select(m => m.ResourceId).Order().ToArray();
            plan.AfterId = ids[^1]; plan.ScanComplete = plan.AfterId >= plan.UpperId;
        }
        else
        {
            var scan = await sp.GetRequiredService<MetadataReplicaQuery>().ScanRuleIdsAsync(plan.Owner, plan.SearchJson,
                null, plan.AfterId, plan.UpperId, ct);
            plan.Generation = scan.Generation; plan.UpperId = scan.UpperId; plan.AfterId = scan.AfterId;
            plan.PendingIds = scan.Ids; plan.ScanComplete = scan.Complete;
        }
    }

    private static ResourceProcessingPrepared PreparedFor(ResourceProcessingBatchPlan plan, string batchId) => new(
        new ResourceProcessingInput(StepId(batchId, plan.PendingIds[plan.PendingIndex], plan.StepIndex), plan.Owner.ServerId,
            plan.Owner.LibraryEpoch, plan.PendingIds[plan.PendingIndex], null, plan.RuleHash, plan.Kind,
            plan.Kind == ResourceProcessingKind.PlayableFiles ? plan.PlayableFiles : null,
            plan.Kind == ResourceProcessingKind.Enhancement ? plan.Enhancers[plan.StepIndex] : null),
        plan.Token ?? "", plan.ResourceRevision, plan.CatalogGeneration ?? "", "");

    private static async Task<ResourceProcessingReceipt?> ReceiptAsync(IServiceProvider sp, ResourceProcessingBatchPlan plan,
        string stepId, string localId, CancellationToken ct) => plan.Owner.ServerId == localId
        ? await sp.GetRequiredService<ResourceProcessingOwner>().GetReceiptAsync(LocalRequester(localId), stepId, plan.Token!, ct)
        : (await SendAsync<ResourceProcessingReceiptStatus>(sp.GetRequiredService<IManagedServerService>(), plan.Owner.ServerId,
            HttpMethod.Post, $"/management/resource-processing/owner/{stepId}/receipt",
            new ResourceProcessingReceiptRequest(plan.Token!), ct)).Receipt;

    private static async Task AdvanceAppliedAsync(ResourceProcessingJobStore store, ResourceProcessingJobDbModel row,
        ResourceProcessingBatchPlan plan, ResourceProcessingPrepared prepared, string localId, CancellationToken ct)
    {
        plan.CleanupPrepared = prepared with {ResourceJson = ""};
        plan.CleanupWorker = WorkerId(plan, localId);
        plan.CleanupWorkerEpoch = WorkerEpoch(plan);
        plan.Token = null; plan.CatalogGeneration = null; plan.ResourceRevision = 0;
        plan.StepIndex++;
        if (plan.Kind == ResourceProcessingKind.PlayableFiles || plan.StepIndex >= plan.Enhancers.Length)
        { plan.PendingIndex++; plan.StepIndex = 0; row.ProcessedCount++; }
        row.ResultJson = null; row.State = row.CancelRequested ? "Cancelled" : "Queued"; row.Phase = "Scanning"; row.Error = null; row.ErrorCode = null;
        row.CurrentStepPhase = null; row.CurrentStepProcessedCount = null; row.CurrentStepTotalCount = null;
        await SavePlanAsync(store, row, plan, ct);
    }

    private static async Task CleanupAcknowledgedAsync(IServiceProvider sp, ResourceProcessingJobDbModel row,
        ResourceProcessingBatchPlan plan, string localId, CancellationToken ct)
    {
        if (plan.CleanupPrepared == null) return;
        if (plan.CleanupWorker == localId) sp.GetRequiredService<ResourceProcessingArtifacts>().Cleanup(plan.CleanupPrepared, "worker");
        else await CleanupWorkerAsync(sp.GetRequiredService<IManagedServerService>(), plan.CleanupWorker!,
            WorkerPath($"/management/resource-processing/worker/{plan.CleanupPrepared.Input.JobId}/ack", plan.CleanupWorkerEpoch), new { }, ct);
        plan.CleanupPrepared = null; plan.CleanupWorker = null; plan.CleanupWorkerEpoch = null;
        await SavePlanAsync(sp.GetRequiredService<ResourceProcessingJobStore>(), row, plan, ct);
    }

    internal static async Task CleanupCancelledAsync(IServiceProvider sp, CancellationToken ct)
    {
        var before = DateTime.UtcNow.AddSeconds(-15);
        var self = await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct);
        var requester = LocalRequester(self.NodeId);
        var row = await sp.GetRequiredService<BakabaseDbContext>().ResourceProcessingJobs.AsNoTracking()
            .Where(j => j.Role == "Manager" && j.RequesterId == requester && j.State == "Cancelled" && j.InputJson != "{}" && j.UpdatedAtUtc < before)
            .OrderBy(j => j.UpdatedAtUtc).FirstOrDefaultAsync(ct);
        if (row == null) return;
        var store = sp.GetRequiredService<ResourceProcessingJobStore>();
        var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
        try
        {
            await CleanupAcknowledgedAsync(sp, row, plan, self.NodeId, ct);
            // Cancellation may arrive while an owner apply response is in flight. An
            // existing receipt records completed work; it never authorizes another write.
            if (plan.Token != null && row.ResultJson != null && plan.PendingIndex < plan.PendingIds.Length)
            {
                var saved = PreparedFor(plan, row.JobId);
                var receipt = await ReceiptAsync(sp, plan, saved.Input.JobId, self.NodeId, ct);
                if (receipt != null)
                {
                    if (receipt.ResultHash != ResourceProcessingWire.Hash(ResourceProcessingWire.Deserialize<ResourceProcessingOutput>(row.ResultJson)))
                        throw new ResourceProcessingException("Conflict", "The source accepted a different result for this work id.");
                    await AdvanceAppliedAsync(store, row, plan, saved, self.NodeId, ct);
                    await CleanupAcknowledgedAsync(sp, row, plan, self.NodeId, ct);
                }
            }
            if (plan.PendingIndex < plan.PendingIds.Length)
            {
                var prepared = PreparedFor(plan, row.JobId);
                // Revoke the owner's preparation first. A worker never writes source
                // metadata, and an offline worker must not retain the owner's quota.
                if (plan.Owner.ServerId == self.NodeId)
                    await sp.GetRequiredService<ResourceProcessingOwner>().AbandonAsync(LocalRequester(self.NodeId), prepared.Input.JobId, plan.Token, ct);
                else await SendAsync<bool>(sp.GetRequiredService<IManagedServerService>(), plan.Owner.ServerId, HttpMethod.Post,
                    $"/management/resource-processing/owner/{prepared.Input.JobId}/abandon", new ResourceProcessingAbandonRequest(plan.Token), ct);
                var worker = WorkerId(plan, self.NodeId);
                if (worker == self.NodeId)
                {
                    if (plan.Token != null) sp.GetRequiredService<ResourceProcessingArtifacts>().Cleanup(prepared, "worker");
                }
                else
                {
                    var stopped = await CleanupWorkerAsync(sp.GetRequiredService<IManagedServerService>(), worker,
                        WorkerPath($"/management/resource-processing/worker/{prepared.Input.JobId}/abandon", WorkerEpoch(plan)), new { }, ct);
                    if (!stopped) { await store.UpdateAsync(row, ct); return; }
                }
            }
            row.InputJson = "{}"; row.ResultJson = null; row.Phase = "Cancelled"; row.ErrorCode = null; row.Error = null;
            row.CurrentStepPhase = null; row.CurrentStepProcessedCount = null; row.CurrentStepTotalCount = null;
            await store.UpdateAsync(row, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Cancellation is already effective. Retain only one bounded cleanup checkpoint
            // and retry when the device is available; never resurrect this batch as runnable.
            row.ErrorCode = "CleanupPending"; row.Error = error.Message;
            try { await store.UpdateAsync(row, ct); }
            catch (ResourceProcessingException e) when (e.Code == "JobChanged") { }
        }
    }

    private static async Task ValidateTargetAsync(IServiceProvider sp, ReplicaIdentity owner, ResourceProcessingExecutionTarget target, CancellationToken ct)
    {
        if (!Enum.IsDefined(target.Kind)) throw new ResourceProcessingException("InvalidTarget", "Choose a worker device.");
        if (target.Kind != ResourceProcessingExecutionKind.Managed) return;
        if (!ResourceProcessingWire.IsIdentity(target.DeviceId) || !ResourceProcessingWire.IsIdentity(target.LibraryEpoch))
            throw new ResourceProcessingException("InvalidTarget", "Select a paired worker and its current library.");
        await sp.GetRequiredService<ManagedLibraryService>().RequireSourceAsync(target.DeviceId!, target.LibraryEpoch!, ct);
    }

    private static async Task CheckRuleAsync(IServiceProvider sp, ResourceProcessingBatchPlan plan)
    {
        var profile = await sp.GetRequiredService<IResourceProfileService>().Get(plan.ProfileId);
        var revision = profile?.ResourceScopeId == null ? 0 : (await sp.GetRequiredService<IResourceScopeService>().GetAll())
            .SingleOrDefault(s => s.Id == profile.ResourceScopeId)?.Revision ?? -1;
        if (profile == null || profile.UpdatedAt != plan.ProfileUpdatedAt || revision != plan.ScopeRevision ||
            RuleHash(profile, revision, plan.Kind) != plan.RuleHash)
            throw new ResourceProcessingException("Conflict", "The rule or its shared conditions changed. Start a new run with the current rule.");
    }

    private static string RuleHash(ResourceProfile p, int scopeRevision, ResourceProcessingKind kind) => ResourceProcessingWire.Hash(new
    {
        p.Id, p.UpdatedAt, p.ResourceScopeId, scopeRevision, p.SourceDeviceId, p.SourceLibraryEpoch, p.SearchJson, kind,
        Playable = kind == ResourceProcessingKind.PlayableFiles ? p.PlayableFileOptions : null,
        Enhancers = kind == ResourceProcessingKind.Enhancement ? p.EnhancerOptions : null
    });
    private static string WorkerId(ResourceProcessingBatchPlan plan, string localId) => plan.Target.Kind switch
    { ResourceProcessingExecutionKind.Owner => plan.Owner.ServerId, ResourceProcessingExecutionKind.Local => localId,
        ResourceProcessingExecutionKind.Managed => plan.Target.DeviceId!, _ => throw new InvalidOperationException() };
    private static string? WorkerEpoch(ResourceProcessingBatchPlan plan) => plan.Target.Kind == ResourceProcessingExecutionKind.Owner
        ? plan.Owner.LibraryEpoch : plan.Target.Kind == ResourceProcessingExecutionKind.Managed ? plan.Target.LibraryEpoch : null;
    private static string WorkerPath(string path, string? epoch) => epoch == null ? path : path + "?managedLibraryEpoch=" + Uri.EscapeDataString(epoch);
    private static string StepId(string batch, int resource, int step) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{batch}:{resource}:{step}")))[..32].ToLowerInvariant();

    internal static EnhancerFullOptions[] OrderEnhancers(IReadOnlyList<EnhancerFullOptions> options)
    {
        if (options.Count > 128 || options.Any(o => o.EnhancerId <= 0) || options.Select(o => o.EnhancerId).Distinct().Count() != options.Count)
            throw new ResourceProcessingException("InvalidRule", "Choose distinct enhancer rules.");
        var ordered = new List<EnhancerFullOptions>();
        var remaining = options.ToDictionary(o => o.EnhancerId);
        while (remaining.Count > 0)
        {
            var next = remaining.Values.FirstOrDefault(o => (o.Requirements ?? []).All(id => !remaining.ContainsKey(id)));
            if (next == null) throw new ResourceProcessingException("InvalidRule", "Enhancer dependencies contain a cycle.");
            ordered.Add(next); remaining.Remove(next.EnhancerId);
        }
        return ordered.ToArray();
    }

    private static async Task<ResourceProcessingJobDbModel> ReloadRunningAsync(ResourceProcessingJobStore store, ResourceProcessingJobDbModel previous, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = await store.GetByIdAsync(previous.Id, ct) ?? throw new ResourceProcessingException("JobMissing", "The job no longer exists.");
        if (current.CancelRequested || current.PauseRequested) throw new ProcessingInterruptedException();
        return current;
    }
    private static async Task RequireWorkerRequesterAsync(IServiceProvider sp, string requester, CancellationToken ct)
    {
        var self = await sp.GetRequiredService<INodeIdentityProvider>().GetAsync(ct);
        if (requester == LocalRequester(self.NodeId)) return;
        if (sp.GetRequiredService<IRemoteDeviceService>().Find(requester) == null)
            throw new ResourceProcessingException("ManagerRevoked", "The manager is no longer paired with this worker.");
    }
    private static async Task SavePlanAsync(ResourceProcessingJobStore store, ResourceProcessingJobDbModel row, ResourceProcessingBatchPlan plan, CancellationToken ct)
    { row.InputJson = ResourceProcessingWire.Serialize(plan); await store.UpdateAsync(row, ct); }

    internal static void SetStepProgress(ResourceProcessingJobDbModel row, ResourceProcessingProgress progress)
    {
        row.CurrentStepPhase = progress.Phase;
        row.CurrentStepProcessedCount = progress.ProcessedCount;
        row.CurrentStepTotalCount = progress.TotalCount;
    }

    internal static async Task SaveFailureAsync(ResourceProcessingJobStore store, ResourceProcessingJobDbModel row, Exception error, CancellationToken ct)
    {
        // Cancellation leaves a durable runnable checkpoint; pause and cancel retain the operator's intent.
        var latest = await store.GetByIdAsync(row.Id, CancellationToken.None);
        if (latest == null) return;
        if (error is ResourceProcessingException {Code: "JobChanged"} &&
            latest.State is "Completed" or "Conflict" or "Cancelled") return;
        latest.State = latest.CancelRequested ? "Cancelled" : latest.PauseRequested ? "Paused" :
            error is OperationCanceledException or ProcessingInterruptedException ? "Queued" :
            error is ReplicaCheckpointConflictException or ResourceScopeConflictException || error is ResourceProcessingException {Code: "Conflict" or "ResourceChanged" or "CatalogChanged" or "RuleChanged" or "EpochChanged"} ||
            error is FederationAccessException {ErrorCode: "LibraryEpochChanged"} ? "Conflict" : "Failed";
        if (!latest.CancelRequested && !latest.PauseRequested &&
            (error is HttpRequestException || error is FederationAccessException {StatusCode: >= 500} ||
            error is ResourceProcessingException {Code: "DeviceUnavailable"}))
        { latest.State = "Queued"; latest.Phase = "WaitingForDevice"; }
        latest.ErrorCode = error is ResourceProcessingException pe ? pe.Code : error is FederationAccessException fe ? fe.ErrorCode : error.GetType().Name;
        latest.Error = error is ProcessingInterruptedException ? null : error.Message;
        try { await store.UpdateAsync(latest, CancellationToken.None); }
        catch (ResourceProcessingException e) when (e.Code == "JobChanged") { }
        if (error is OperationCanceledException && ct.IsCancellationRequested) throw error;
    }

    internal static async Task<ResourceProcessingWorkerResult> QueueWorkerAsync(IManagedServerService transport,
        string workerId, ResourceProcessingPrepared prepared, bool retryRequested, CancellationToken ct, string? workerEpoch = null)
    {
        var result = await SendAsync<ResourceProcessingWorkerResult>(transport, workerId, HttpMethod.Post,
            WorkerPath("/management/resource-processing/worker/queue", workerEpoch), prepared, ct);
        // A lost best-effort resume must not strand a worker while its manager is runnable.
        // Failed computation is retried only after an explicit operator request.
        var action = result.Job.State == "Paused" ? "resume" : result.Job.State == "Failed" && retryRequested ? "retry" : null;
        if (action == null) return result;
        await SendAsync<bool>(transport, workerId, HttpMethod.Post,
            WorkerPath($"/management/resource-processing/worker/{prepared.Input.JobId}/control", workerEpoch), new ResourceProcessingWorkerControl(action), ct);
        return await SendAsync<ResourceProcessingWorkerResult>(transport, workerId, HttpMethod.Post,
            WorkerPath("/management/resource-processing/worker/queue", workerEpoch), prepared, ct);
    }

    internal static async Task<T> SendAsync<T>(IManagedServerService transport, string deviceId, HttpMethod method,
        string path, object? body, CancellationToken ct)
    {
        var response = await transport.SendAsync(deviceId, method, path, body == null ? null : ResourceProcessingWire.Serialize(body), ct);
        if (response.StatusCode is < 200 or >= 300)
        {
            var code = response.StatusCode == 409 ? "Conflict" : response.StatusCode >= 500 ? "DeviceUnavailable" : "DeviceRefused";
            if (response.StatusCode == 409 && response.Body.Length <= 8192)
            {
                try
                {
                    using var error = System.Text.Json.JsonDocument.Parse(response.Body);
                    if (error.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        error.RootElement.TryGetProperty("code", out var reported) &&
                        reported.ValueKind == System.Text.Json.JsonValueKind.String &&
                        reported.GetString() is "LibraryEpochChanged" or "EpochChanged") code = "EpochChanged";
                }
                catch (System.Text.Json.JsonException) { }
            }
            throw new ResourceProcessingException(code,
                "The device could not complete the processing request (" + response.StatusCode + "). " + response.Body[..Math.Min(response.Body.Length, 1024)]);
        }
        return ResourceProcessingWire.Deserialize<T>(response.Body);
    }

    private static async Task<bool> CleanupWorkerAsync(IManagedServerService transport, string deviceId,
        string path, object body, CancellationToken ct)
    {
        try { return await SendAsync<bool>(transport, deviceId, HttpMethod.Post, path, body, ct); }
        // The expected old worker no longer exists. Its pinned local checkpoint stops
        // and cleans old work; this does not authorize replay against the new library.
        catch (ResourceProcessingException error) when (error.Code == "EpochChanged") { return true; }
    }
    public static ResourceProcessingJobView View(ResourceProcessingJobDbModel j) => new(j.Id, j.JobId, j.Role,
        j.OwnerId, j.OwnerEpoch, j.ResourceId, j.Kind, j.State, j.Phase, j.ProcessedCount, j.TotalCount,
        j.Attempt, j.PauseRequested, j.CancelRequested, j.ErrorCode, j.Error, j.CreatedAtUtc, j.UpdatedAtUtc, j.Version,
        j.CurrentStepPhase, j.CurrentStepProcessedCount, j.CurrentStepTotalCount);
    private sealed class ProcessingInterruptedException : Exception;
}

public sealed class ResourceProcessingBatchPlan
{
    public int ProfileId { get; set; }
    public DateTime ProfileUpdatedAt { get; set; }
    public int ScopeRevision { get; set; }
    public string RuleHash { get; set; } = null!;
    public ReplicaIdentity Owner { get; set; } = null!;
    public ResourceProcessingExecutionTarget Target { get; set; } = null!;
    public ResourceProcessingKind Kind { get; set; }
    public string? SearchJson { get; set; }
    public ResourceProfilePlayableFileOptions? PlayableFiles { get; set; }
    public EnhancerFullOptions[] Enhancers { get; set; } = [];
    public string? Generation { get; set; }
    public int? UpperId { get; set; }
    public int AfterId { get; set; }
    public int[] PendingIds { get; set; } = [];
    public int PendingIndex { get; set; }
    public int StepIndex { get; set; }
    public bool ScanComplete { get; set; }
    public string? Token { get; set; }
    public long ResourceRevision { get; set; }
    public string? CatalogGeneration { get; set; }
    public bool RetryWorkerRequested { get; set; }
    public ResourceProcessingPrepared? CleanupPrepared { get; set; }
    public string? CleanupWorker { get; set; }
    public string? CleanupWorkerEpoch { get; set; }
}
