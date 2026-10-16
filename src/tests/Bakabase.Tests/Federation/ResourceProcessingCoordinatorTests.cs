using System.Reflection;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Components.Federation.Processing;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Federation;

[TestClass]
[DoNotParallelize]
public sealed class ResourceProcessingCoordinatorTests
{
    private string _directory = null!;
    private BakabaseDbContext _db = null!;
    private ResourceProcessingJobStore _store = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bakabase-coordinator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_directory, "test.sqlite") + ";Pooling=False").Options);
        await _db.Database.EnsureCreatedAsync();
        _store = new(_db);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    private static ResourceProcessingBatchPlan Plan() => new()
    {
        ProfileId = 1, ProfileUpdatedAt = DateTime.UtcNow, RuleHash = new string('a', 64),
        Owner = new ReplicaIdentity("manager", "epoch"),
        Target = new(ResourceProcessingExecutionKind.Local), Kind = ResourceProcessingKind.PlayableFiles,
        PlayableFiles = new() {Extensions = ["mp4"]}, PendingIds = [17],
        AfterId = 12, Token = new string('b', 64)
    };

    private Task<ResourceProcessingJobDbModel> Create(string? jobId = null) => _store.CreateBatchAsync(
        ResourceProcessingCoordinator.LocalRequester("manager"), jobId ?? Guid.NewGuid().ToString("N"),
        "manager", "epoch", new string('a', 64), ResourceProcessingKind.PlayableFiles,
        ResourceProcessingWire.Serialize(Plan()), default);

    private ServiceProvider Services(IManagedServerService? transport = null, Identity? identity = null)
    {
        var services = new ServiceCollection().AddSingleton(_store).AddSingleton(_db)
            .AddSingleton<INodeIdentityProvider>(identity ?? new Identity())
            .AddSingleton(new ResourceProcessingArtifacts(new DataDirectory(_directory)));
        if (transport != null) services.AddSingleton(transport);
        services.AddSingleton(Proxy<IResourceProfileService>((method, _) => method.Name == "Get"
            ? Task.FromResult<ResourceProfile?>(null) : throw new NotSupportedException(method.Name)));
        return services.BuildServiceProvider();
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TransportProxy>();
        ((TransportProxy)(object)proxy).Call = call;
        return proxy;
    }

    private static IManagedServerService Transport(Func<string, string, object> reply) => Proxy<IManagedServerService>((_, args) =>
        Task.FromResult(new ManagedServerResponse(200, "application/json", ResourceProcessingWire.Serialize(reply((string)args![0]!, (string)args[2]!)))));

    private ResourceProcessingArtifacts Artifacts() => new(new DataDirectory(_directory));

    private ManagedResourceProcessingController Controller(Identity identity)
    {
        var http = new DefaultHttpContext();
        http.SetRemoteAccessContext(new RemoteAccessContext {IsLoopback = true, Mode = RemoteAccessMode.Unrestricted});
        return new(null!, _store, Artifacts(), identity) {ControllerContext = new() {HttpContext = http}};
    }

    private static ResourceProcessingPrepared Preparation() => new(new(Guid.NewGuid().ToString("N"), "owner", "owner-epoch", 17,
        null, new string('a', 64), ResourceProcessingKind.PlayableFiles, new() {Extensions = ["mp4"]}),
        new string('b', 64), 1, "catalog", "{}");

    [TestMethod]
    public async Task Latest_page_orders_by_creation_time_not_random_job_id_and_cursor_keeps_ties()
    {
        var old = await Create(new string('f', 32));
        var newer = await Create(new string('0', 32));
        var tied = await Create(new string('1', 32));
        var created = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.ResourceProcessingJobs.Where(j => j.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.CreatedAtUtc, created));
        await _db.ResourceProcessingJobs.Where(j => j.Id != old.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.CreatedAtUtc, created.AddMinutes(1)));
        var first = await _store.ListAsync(ResourceProcessingRole.Manager, null, 1, default);
        Assert.AreEqual(tied.Id, first.Single().Id);
        var second = await _store.ListAsync(ResourceProcessingRole.Manager, first.Single().Id, 1, default);
        Assert.AreEqual(newer.Id, second.Single().Id);
        var third = await _store.ListAsync(ResourceProcessingRole.Manager, second.Single().Id, 1, default);
        Assert.AreEqual(old.Id, third.Single().Id);
        Assert.AreEqual(0, (await _store.ListAsync(ResourceProcessingRole.Manager, "unknown", 1, default)).Count);
    }

    [TestMethod]
    public async Task Network_failure_after_pause_preserves_pause_and_resume_preserves_checkpoint()
    {
        var running = await Create(); running.State = "Running";
        await _store.UpdateAsync(running, default);
        var operatorView = (await _store.GetByIdAsync(running.Id, default))!;
        operatorView.PauseRequested = true; await _store.UpdateAsync(operatorView, default);
        await ResourceProcessingCoordinator.SaveFailureAsync(_store, running, new HttpRequestException("offline"), default);
        var paused = (await _store.GetByIdAsync(running.Id, default))!;
        Assert.AreEqual("Paused", paused.State);
        Assert.IsTrue(paused.PauseRequested);
        using var services = Services();
        var resumed = await new ResourceProcessingCoordinator(services).ControlAsync(running.JobId, new("resume", paused.Version), default);
        Assert.AreEqual("Queued", resumed.State);
        Assert.IsFalse(resumed.PauseRequested);
        var checkpoint = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>((await _store.GetByIdAsync(running.Id, default))!.InputJson);
        Assert.AreEqual(12, checkpoint.AfterId);
        CollectionAssert.AreEqual(new[] {17}, checkpoint.PendingIds);
        Assert.AreEqual(new string('b', 64), checkpoint.Token);
    }

    [TestMethod]
    public async Task Network_failure_after_cancel_cannot_make_the_cancelled_job_runnable()
    {
        var running = await Create(); running.State = "Running"; await _store.UpdateAsync(running, default);
        var operatorView = (await _store.GetByIdAsync(running.Id, default))!;
        operatorView.CancelRequested = true; await _store.UpdateAsync(operatorView, default);
        await ResourceProcessingCoordinator.SaveFailureAsync(_store, running,
            new ResourceProcessingException("DeviceUnavailable", "offline"), default);
        var cancelled = (await _store.GetByIdAsync(running.Id, default))!;
        Assert.AreEqual("Cancelled", cancelled.State);
        Assert.IsTrue(cancelled.CancelRequested);
        Assert.IsNull(await _store.NextQueuedAsync(ResourceProcessingRole.Manager, default));
    }

    [TestMethod]
    public async Task Uninterrupted_network_failure_remains_retryable_after_restart()
    {
        var row = await Create(); row.State = "Running"; await _store.UpdateAsync(row, default);
        await ResourceProcessingCoordinator.SaveFailureAsync(_store, row, new HttpRequestException("offline"), default);
        var saved = (await new ResourceProcessingJobStore(_db).GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("Queued", saved.State);
        Assert.AreEqual("WaitingForDevice", saved.Phase);
        Assert.IsFalse(saved.PauseRequested);
        Assert.IsFalse(saved.CancelRequested);
    }

    [TestMethod]
    public async Task Explicit_retry_keeps_durable_output_and_checkpoint_and_arms_failed_worker_retry()
    {
        var row = await Create(); row.State = "Failed"; row.Error = "worker failed";
        row.ResultJson = ResourceProcessingWire.Serialize(new ResourceProcessingOutput(ResourceProcessingKind.PlayableFiles, ["clip.mp4"], [], [], 1));
        await _store.UpdateAsync(row, default);
        using var services = Services(); var coordinator = new ResourceProcessingCoordinator(services);
        await coordinator.ControlAsync(row.JobId, new("retry", row.Version), default);
        var saved = (await _store.GetByIdAsync(row.Id, default))!;
        var checkpoint = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(saved.InputJson);
        Assert.IsTrue(checkpoint.RetryWorkerRequested);
        Assert.AreEqual("Queued", saved.State);
        Assert.AreEqual(row.ResultJson, saved.ResultJson, "A durable output must be reused after an uncertain owner apply response.");
        Assert.AreEqual(12, checkpoint.AfterId);
        var stale = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() =>
            coordinator.ControlAsync(row.JobId, new("cancel", row.Version), default));
        Assert.AreEqual("JobChanged", stale.Code);
        Assert.IsFalse((await _store.GetByIdAsync(row.Id, default))!.CancelRequested);
    }

    [TestMethod]
    public async Task Runnable_manager_reconciles_a_worker_left_paused_after_lost_resume()
    {
        var calls = new List<string>();
        var result = await QueueWorker("Paused", false, calls);
        Assert.AreEqual("Queued", result.Job.State);
        CollectionAssert.AreEqual(new[] {"queue", "resume", "queue"}, calls);
    }

    [TestMethod]
    public async Task Failed_worker_only_retries_after_explicit_operator_request()
    {
        var calls = new List<string>();
        Assert.AreEqual("Failed", (await QueueWorker("Failed", false, calls)).Job.State);
        CollectionAssert.AreEqual(new[] {"queue"}, calls);
        calls.Clear();
        Assert.AreEqual("Queued", (await QueueWorker("Failed", true, calls)).Job.State);
        CollectionAssert.AreEqual(new[] {"queue", "retry", "queue"}, calls);
    }

    [TestMethod]
    public async Task Worker_queue_pins_its_own_epoch_and_reset_blocks_execution_and_replay()
    {
        var identity = new Identity(); var controller = Controller(identity); var prepared = Preparation();
        Assert.IsInstanceOfType<OkObjectResult>(await controller.Queue(prepared, default));
        var queued = (await _store.GetAsync(ResourceProcessingRole.Worker, "local:manager", prepared.Input.JobId, default))!;
        Assert.AreEqual("epoch", ResourceProcessingWorkerEnvelope.Read(queued.InputJson).WorkerEpoch);
        identity.Epoch = "replacement";
        using var services = Services(identity: identity);
        // No ResourceProcessingWorker is registered: an epoch mismatch must fail before
        // any worker component or resource path can be resolved.
        await new ResourceProcessingCoordinator(services).RunWorkerAsync(services, queued, _ => Task.CompletedTask,
            () => Task.CompletedTask, default);
        var stopped = (await _store.GetByIdAsync(queued.Id, default))!;
        Assert.AreEqual("Conflict", stopped.State);
        Assert.AreEqual("EpochChanged", stopped.ErrorCode);
        Assert.IsNull(stopped.ResultJson);
        Assert.AreEqual("{}", stopped.InputJson);
        Assert.AreEqual(409, ((ObjectResult)await controller.Queue(prepared, default)).StatusCode);
    }

    [TestMethod]
    public async Task Worker_cancel_waits_for_running_work_then_compacts_and_fences_a_stale_runner()
    {
        var identity = new Identity(); var controller = Controller(identity); var prepared = Preparation();
        await controller.Queue(prepared, default);
        var queued = (await _store.GetAsync(ResourceProcessingRole.Worker, "local:manager", prepared.Input.JobId, default))!;
        var path = Artifacts().GetDirectory(prepared, "worker"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "working"), "data");
        queued.State = "Running"; await _store.UpdateAsync(queued, default);
        Assert.AreEqual(false, ((OkObjectResult)await controller.AbandonWorker(prepared.Input.JobId, default)).Value);
        Assert.IsTrue(Directory.Exists(path), "Cancellation must not remove a running enhancer's files.");
        await ResourceProcessingCoordinator.SaveFailureAsync(_store, queued, new HttpRequestException("stopped"), default);
        var staleRunner = (await _store.GetByIdAsync(queued.Id, default))!;
        Assert.AreEqual(true, ((OkObjectResult)await controller.AbandonWorker(prepared.Input.JobId, default)).Value);
        Assert.IsFalse(Directory.Exists(path));
        var compact = (await _store.GetByIdAsync(queued.Id, default))!;
        Assert.AreEqual("{}", compact.InputJson);
        Assert.AreEqual("Cancelled", compact.State);
        staleRunner.State = "Running";
        Assert.AreEqual("JobChanged", (await Assert.ThrowsExceptionAsync<ResourceProcessingException>(
            () => _store.UpdateAsync(staleRunner, default))).Code);
    }

    [TestMethod]
    public async Task Receipt_recovery_counts_once_before_a_changed_rule_and_retries_ack_after_restart()
    {
        var row = await Create(); var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
        plan.Owner = new("source-b", "epoch-b"); plan.Target = new(ResourceProcessingExecutionKind.Managed, "worker-c", "epoch-c");
        var output = new ResourceProcessingOutput(ResourceProcessingKind.PlayableFiles, ["clip.mp4"], [], [], 1);
        row.InputJson = ResourceProcessingWire.Serialize(plan); row.ResultJson = ResourceProcessingWire.Serialize(output);
        await _store.UpdateAsync(row, default);
        var receipts = 0; var acks = 0; var allowAck = false;
        var transport = Transport((device, path) =>
        {
            if (path.EndsWith("/receipt"))
            { receipts++; Assert.AreEqual("source-b", device); return new ResourceProcessingReceiptStatus(new("step", 1, ResourceProcessingWire.Hash(output))); }
            if (path.Contains("/ack?"))
            {
                acks++; Assert.AreEqual("worker-c", device); StringAssert.Contains(path, "managedLibraryEpoch=epoch-c");
                if (!allowAck) throw new HttpRequestException("lost ack");
                return true;
            }
            throw new AssertFailedException("Recovery must not prepare, compute, or apply again: " + path);
        });
        using var services = Services(transport);
        await new ResourceProcessingCoordinator(services).RunManagerAsync(services, row, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        var checkpoint = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual(1L, checkpoint.ProcessedCount);
        Assert.IsNotNull(ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(checkpoint.InputJson).CleanupPrepared);
        Assert.IsNull(checkpoint.ResultJson);
        allowAck = true;
        await new ResourceProcessingCoordinator(services).RunManagerAsync(services, checkpoint, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        var finished = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("Conflict", finished.State, "The missing/changed rule stops future work after receipt recovery.");
        Assert.AreEqual(1L, finished.ProcessedCount);
        Assert.IsNull(ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(finished.InputJson).CleanupPrepared);
        Assert.AreEqual(1, receipts); Assert.AreEqual(2, acks);
    }

    [TestMethod]
    public async Task Cancelled_cleanup_releases_owner_first_and_retains_checkpoint_until_worker_stops()
    {
        var row = await Create(); var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
        plan.Owner = new("source-b", "epoch-b"); plan.Target = new(ResourceProcessingExecutionKind.Managed, "worker-c", "epoch-c");
        row.InputJson = ResourceProcessingWire.Serialize(plan); row.State = "Cancelled"; row.CancelRequested = true;
        await _store.UpdateAsync(row, default);
        var stopped = false; var ownerCalls = 0; var unavailable = true;
        var transport = Transport((device, _) =>
        {
            if (device == "worker-c")
            {
                if (unavailable) throw new HttpRequestException("offline");
                return stopped;
            }
            ownerCalls++; return true;
        });
        using var services = Services(transport);
        async Task CleanupCycle()
        {
            await _db.ResourceProcessingJobs.Where(j => j.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.UpdatedAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            await ResourceProcessingCoordinator.CleanupCancelledAsync(services, default);
        }
        await CleanupCycle();
        var pending = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("Cancelled", pending.State); Assert.AreEqual("CleanupPending", pending.ErrorCode);
        Assert.AreNotEqual("{}", pending.InputJson);
        Assert.AreEqual(1, ownerCalls, "An offline worker must not consume an owner preparation indefinitely.");
        unavailable = false; await CleanupCycle(); Assert.AreEqual(2, ownerCalls);
        stopped = true; await CleanupCycle();
        var compact = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("{}", compact.InputJson); Assert.AreEqual("Cancelled", compact.State);
        Assert.AreEqual(3, ownerCalls);
    }

    [TestMethod]
    public async Task Current_resource_progress_persists_separately_from_completed_resources()
    {
        var row = await Create(); row.ProcessedCount = 4; row.TotalCount = 20;
        ResourceProcessingCoordinator.SetStepProgress(row, new("DiscoveringFiles", 2200, 3000));
        await _store.UpdateAsync(row, default);
        var view = (await _store.ListAsync(ResourceProcessingRole.Manager, null, 30, default)).Single();
        Assert.AreEqual(4L, view.ProcessedCount); Assert.AreEqual(20L, view.TotalCount);
        Assert.AreEqual(2200L, view.CurrentStepProcessedCount); Assert.AreEqual(3000L, view.CurrentStepTotalCount);
        Assert.AreEqual("DiscoveringFiles", view.CurrentStepPhase);
    }

    [TestMethod]
    public async Task Unacknowledged_completed_work_from_replaced_library_does_not_exhaust_worker_quota()
    {
        var identity = new Identity(); var controller = Controller(identity);
        for (var i = 0; i < 16; i++)
        {
            var prepared = Preparation(); await controller.Queue(prepared, default);
            var row = (await _store.GetAsync(ResourceProcessingRole.Worker, "local:manager", prepared.Input.JobId, default))!;
            row.State = "Completed"; row.ResultJson = "{}"; await _store.UpdateAsync(row, default);
        }
        Assert.AreEqual(16, (await _store.GetRetainedWorkersAsync("local:manager", default)).Count);
        identity.Epoch = "replacement";
        Assert.IsInstanceOfType<OkObjectResult>(await controller.Queue(Preparation(), default));
        Assert.AreEqual(1, (await _store.GetRetainedWorkersAsync("local:manager", default)).Count);
        Assert.AreEqual(16, await _db.ResourceProcessingJobs.CountAsync(j => j.State == "Conflict" && j.InputJson == "{}"));
    }

    [TestMethod]
    public async Task Cancellation_during_uncertain_apply_counts_the_existing_receipt_without_a_second_write()
    {
        var row = await Create(); var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
        plan.Owner = new("source-b", "epoch-b");
        var output = new ResourceProcessingOutput(ResourceProcessingKind.PlayableFiles, ["clip.mp4"], [], [], 1);
        row.InputJson = ResourceProcessingWire.Serialize(plan); row.ResultJson = ResourceProcessingWire.Serialize(output);
        row.State = "Cancelled"; row.CancelRequested = true;
        await _store.UpdateAsync(row, default);
        await _db.ResourceProcessingJobs.Where(j => j.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.UpdatedAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        var transport = Transport((_, path) => path.EndsWith("/receipt")
            ? new ResourceProcessingReceiptStatus(new("step", 1, ResourceProcessingWire.Hash(output)))
            : throw new AssertFailedException("Committed work must not be written or abandoned again: " + path));
        using var services = Services(transport);
        await ResourceProcessingCoordinator.CleanupCancelledAsync(services, default);
        var finished = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("Cancelled", finished.State);
        Assert.AreEqual(1L, finished.ProcessedCount);
        Assert.AreEqual("{}", finished.InputJson);
        Assert.IsNull(finished.ResultJson);
    }

    [TestMethod]
    public async Task Anonymous_browser_cannot_abandon_or_queue_prepared_work()
    {
        var controller = Controller(new Identity());
        controller.HttpContext.SetRemoteAccessContext(new RemoteAccessContext {IsLoopback = false, Mode = RemoteAccessMode.Unrestricted});
        Assert.AreEqual(403, ((ObjectResult)await controller.Queue(Preparation(), default)).StatusCode);
        Assert.AreEqual(403, ((ObjectResult)await controller.Abandon(Guid.NewGuid().ToString("N"), new(null), default)).StatusCode);
        Assert.AreEqual(0, await _db.ResourceProcessingJobs.CountAsync());
    }

    [TestMethod]
    public async Task Missing_worker_abandon_fences_a_delayed_queue_without_claiming_another_requester()
    {
        var prepared = Preparation(); var controller = Controller(new Identity());
        Assert.AreEqual(true, ((OkObjectResult)await controller.AbandonWorker(prepared.Input.JobId, default)).Value);
        var cancelled = (await _store.GetAsync(ResourceProcessingRole.Worker, "local:manager", prepared.Input.JobId, default))!;
        Assert.AreEqual("Cancelled", cancelled.State);
        Assert.AreEqual("{}", cancelled.InputJson);
        var rejected = (ObjectResult)await controller.Queue(prepared, default);
        Assert.AreEqual(400, rejected.StatusCode);
        StringAssert.Contains(ResourceProcessingWire.Serialize(rejected.Value!), "JobCancelled");
        Assert.AreEqual(0, (await _store.GetRetainedWorkersAsync("local:manager", default)).Count);
        var other = await _store.CreateAsync(ResourceProcessingRole.Worker, "other-manager", prepared.Input,
            ResourceProcessingWire.Serialize(new ResourceProcessingWorkerEnvelope(prepared, "manager", "epoch")), default);
        Assert.AreEqual("Queued", other.State);
        Assert.AreEqual(true, ((OkObjectResult)await controller.AbandonWorker(prepared.Input.JobId, default)).Value);
        Assert.AreEqual("Queued", (await _store.GetByIdAsync(other.Id, default))!.State);
    }

    [TestMethod]
    public async Task Worker_epoch_reset_finishes_cleanup_but_other_conflicts_remain_visible()
    {
        var row = await Create(); var plan = ResourceProcessingWire.Deserialize<ResourceProcessingBatchPlan>(row.InputJson);
        plan.Owner = new("source-b", "epoch-b"); plan.Target = new(ResourceProcessingExecutionKind.Managed, "worker-c", "old-epoch");
        row.InputJson = ResourceProcessingWire.Serialize(plan); row.State = "Cancelled"; row.CancelRequested = true;
        await _store.UpdateAsync(row, default);
        var code = "ResourceChanged"; var ownerCalls = 0;
        var transport = Proxy<IManagedServerService>((_, args) =>
        {
            var device = (string)args![0]!;
            if (device == "source-b") { ownerCalls++; return Task.FromResult(new ManagedServerResponse(200, "application/json", "true")); }
            StringAssert.Contains((string)args[2]!, "managedLibraryEpoch=old-epoch");
            return Task.FromResult(new ManagedServerResponse(409, "application/json", ResourceProcessingWire.Serialize(new {code})));
        });
        using var services = Services(transport);
        async Task CleanupCycle()
        {
            await _db.ResourceProcessingJobs.Where(j => j.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.UpdatedAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            await ResourceProcessingCoordinator.CleanupCancelledAsync(services, default);
        }
        await CleanupCycle();
        var conflicted = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("CleanupPending", conflicted.ErrorCode);
        Assert.AreNotEqual("{}", conflicted.InputJson, "An arbitrary conflict must not be accepted as completed cleanup.");
        code = "LibraryEpochChanged"; await CleanupCycle();
        var finished = (await _store.GetByIdAsync(row.Id, default))!;
        Assert.AreEqual("Cancelled", finished.State); Assert.AreEqual("{}", finished.InputJson);
        Assert.AreEqual(2, ownerCalls);
    }

    private async Task<ResourceProcessingWorkerResult> QueueWorker(string state, bool retry, List<string> calls)
    {
        var row = await Create(); row.State = state;
        var prepared = new ResourceProcessingPrepared(new(row.JobId, row.OwnerId, row.OwnerEpoch, 17, null,
            row.RuleHash, ResourceProcessingKind.PlayableFiles, new() {Extensions = ["mp4"]}), new string('b', 64), 1, "catalog", "{}");
        var transport = DispatchProxy.Create<IManagedServerService, TransportProxy>();
        ((TransportProxy)(object)transport).Call = (_, args) =>
        {
            Assert.AreEqual("worker-c", args![0]);
            var path = (string)args[2]!;
            if (path.EndsWith("/queue"))
            {
                calls.Add("queue");
                return Task.FromResult(new ManagedServerResponse(200, "application/json", ResourceProcessingWire.Serialize(
                    new ResourceProcessingWorkerResult(ResourceProcessingCoordinator.View(row), null))));
            }
            var action = ResourceProcessingWire.Deserialize<ResourceProcessingWorkerControl>((string)args[3]!).Action;
            calls.Add(action); row.State = "Queued";
            return Task.FromResult(new ManagedServerResponse(200, "application/json", "true"));
        };
        return await ResourceProcessingCoordinator.QueueWorkerAsync(transport, "worker-c", prepared, retry, default);
    }

    private sealed class Identity : INodeIdentityProvider
    {
        public string Epoch { get; set; } = "epoch";
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("manager", Epoch, "Manager"));
    }

    private sealed class DataDirectory(string path) : IFederationDataDirectory
    { public string Path => path; public string Ensure() { Directory.CreateDirectory(path); return path; } }

    public class TransportProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }
}
