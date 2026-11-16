using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionWorkflowSchedulingTests
{
    private sealed class Options : IBOptions<AcquisitionOptions>
    {
        public AcquisitionOptions Value { get; } = new() {Concurrency = 2};
    }

    private sealed class ExistingPolicy : IWorkflowRunSchedulingPolicy
    {
        public bool AppliesTo(string kind) => kind == CloudWorkflow.Trigger;
        public bool ResumeOnStartup => false;
        public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) => builder
            .WithConcurrencyLimit("existing", () => 1).ConflictsWith("original-conflict").DependsOn("original-dependency");
    }

    [DataTestMethod]
    [DataRow("acquisition", "task", "1", true)]
    [DataRow("postParser", "task", "1", false)]
    [DataRow("acquisition", "resource", "1", false)]
    [DataRow("acquisition", "task", "0", false)]
    [DataRow("acquisition", "task", "-1", false)]
    [DataRow("acquisition", "task", "not-an-id", false)]
    public void OnlyOwnedAcquisitionRunsUseTheSharedPool(string module, string kind, string id, bool expected)
    {
        var options = new Options();
        var policy = new AcquisitionWorkflowSchedulingPolicy(options);
        var resolver = new WorkflowRunSchedulingPolicyResolver([], [policy]);
        var builder = resolver.Configure(BTaskBuilder.Create("run"), 0, CloudWorkflow.Trigger, 27,
            [new(module, kind, id)]);
        Assert.AreEqual(expected ? AcquisitionWorkflowSchedulingPolicy.Group : null, builder.ConcurrencyGroup);
        Assert.IsTrue(builder.ConflictKeys!.SetEquals(["workflow.run.27"]));
        Assert.IsTrue(resolver.ResumeOnStartup(CloudWorkflow.Trigger));
    }

    [TestMethod]
    public void ExistingTriggerPolicyAndIndependentWorkflowConstraintsArePreserved()
    {
        var policy = new AcquisitionWorkflowSchedulingPolicy(new Options());
        var sources = new[] {new WorkflowSourceRef("acquisition", "task", "1")};
        var resolver = new WorkflowRunSchedulingPolicyResolver([new ExistingPolicy()], [policy]);
        var existing = resolver.Configure(BTaskBuilder.Create("existing"), 0, CloudWorkflow.Trigger, 1, sources);
        Assert.AreEqual("existing", existing.ConcurrencyGroup);
        Assert.AreEqual(1, existing.GetConcurrencyLimit!());
        Assert.IsTrue(existing.ConflictKeys!.SetEquals(["original-conflict"]));
        Assert.IsTrue(existing.DependsOn!.SetEquals(["original-dependency"]));
        Assert.IsFalse(resolver.ResumeOnStartup(CloudWorkflow.Trigger));
        var independent = resolver.Configure(BTaskBuilder.Create("other"), 12, "other.trigger", 2, sources);
        Assert.IsNull(independent.ConcurrencyGroup);
        Assert.IsTrue(independent.ConflictKeys!.SetEquals(["workflow.definition.12"]));
    }

    [TestMethod]
    public void LiveConcurrencyUpdatesApplyToAlreadyQueuedBuilders()
    {
        var options = new Options();
        var resolver = new WorkflowRunSchedulingPolicyResolver([], [new AcquisitionWorkflowSchedulingPolicy(options)]);
        var builder = resolver.Configure(BTaskBuilder.Create("run"), 0, CloudWorkflow.Trigger, 1,
            [new("acquisition", "task", "1")]);
        Assert.AreEqual(2, builder.GetConcurrencyLimit!());
        options.Value.Concurrency = 4;
        Assert.AreEqual(4, builder.GetConcurrencyLimit!());
        options.Value.Concurrency = 0;
        Assert.AreEqual(1, builder.GetConcurrencyLimit!());
    }

    private static string Snapshot(params string[] kinds) => WorkflowRunPlans.Serialize(new WorkflowRunPlan
    {
        Name = "Scheduling fixture", TriggerKind = CloudWorkflow.Trigger,
        Nodes = kinds.Select((kind, index) => new WorkflowRunPlanNode {NodeId = $"step-{index}", Kind = kind}).ToArray()
    });

    [DataTestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    [DataRow(4, false)]
    [DataRow(-1, false)]
    public void OnlyProvenLocalRemainingNodesCanShareWithWaitingDownloads(int cursor, bool eligible)
    {
        var resolver = new WorkflowRunSchedulingPolicyResolver([], [new AcquisitionWorkflowSchedulingPolicy(new Options())]);
        var source = new[] {new WorkflowSourceRef("acquisition", "task", "7")};
        var snapshot = Snapshot("download.enqueueSupported", "download.awaitCompletion", "acquisition.plan.process",
            "fs.publishArtifacts");
        var configured = resolver.Configure(BTaskBuilder.Create("workflow.run.19"), 0, CloudWorkflow.Trigger,
            19, source, snapshot, cursor);
        Assert.AreEqual(eligible, configured.CanShareConcurrencySlotWith != null);
        foreach (var unproven in new[] {null, "{bad json", Snapshot("acquisition.plan.process", "unknown.node")})
            Assert.IsNull(resolver.Configure(BTaskBuilder.Create("run"), 0, CloudWorkflow.Trigger, 19, source, unproven)
                .CanShareConcurrencySlotWith);
    }

    [TestMethod]
    public void SharingRequiresCurrentDownloadWaitingEvidenceFromEveryActiveWorker()
    {
        var policy = new AcquisitionWorkflowSchedulingPolicy(new Options());
        var canShare = policy.Configure(BTaskBuilder.Create("local"), Snapshot("acquisition.plan.process"), 0)
            .CanShareConcurrencySlotWith!;
        var worker = new BTask("workflow.run.12", () => "Download")
            {Status = BTaskStatus.Running, StartedAt = DateTime.Now.AddMinutes(-1)};
        Assert.IsFalse(canShare(worker));
        worker.Data = new WorkflowTaskProgress(99, "download.awaitCompletion", "downloading");
        Assert.IsFalse(canShare(worker));
        worker.Data = new WorkflowTaskProgress(12, "download.awaitCompletion", "verifyingFiles");
        Assert.IsFalse(canShare(worker));
        worker.Data = new WorkflowTaskProgress(12, "download.awaitCompletion", "downloading");
        Assert.IsTrue(canShare(worker));
        worker.Status = BTaskStatus.Cancelling;
        Assert.IsFalse(canShare(worker));
        worker.Status = BTaskStatus.Running;
        worker.StartedAt = DateTime.Now.AddSeconds(1);
        Assert.IsFalse(canShare(worker), "A previous execution of the same run cannot lend the slot.");
    }

    [TestMethod]
    public async Task FullDownloadPoolAdmitsOneLocalWorkflowPastQueuedDownloads()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var manager = services.GetRequiredService<BTaskManager>();
        var resolver = new WorkflowRunSchedulingPolicyResolver([], [new AcquisitionWorkflowSchedulingPolicy(new Options())]);
        const string awaiting = "download.awaitCompletion";
        const string process = "acquisition.plan.process";
        var local = Snapshot("acquisition.plan.continueFiles", process, "fs.publishArtifacts");
        var network = Snapshot("download.enqueueSupported", awaiting, process);
        var started = Enumerable.Range(0, 6).Select(_ =>
            new TaskCompletionSource<BTaskArgs>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        async Task Enqueue(int runId, string snapshot)
        {
            var configured = resolver.Configure(BTaskBuilder.Create($"workflow.run.{runId}"), 0, CloudWorkflow.Trigger,
                runId, [new("acquisition", "task", runId.ToString())], snapshot);
            await manager.Enqueue(configured.Run(async args =>
            {
                await args.UpdateTask(t => t.Data = new WorkflowTaskProgress(runId,
                    snapshot == network ? awaiting : process, snapshot == network ? "downloading" : "processingFiles", 1));
                started[runId].TrySetResult(args);
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            }));
        }
        try
        {
            await Enqueue(1, network);
            await Enqueue(2, network);
            await Task.WhenAll(manager.Start("workflow.run.1"), manager.Start("workflow.run.2"));
            await Task.WhenAll(started[1].Task, started[2].Task).WaitAsync(TimeSpan.FromSeconds(5));
            await Enqueue(3, network);
            await Enqueue(4, local);
            await Enqueue(5, local);

            // Exercise the daemon's ordering, then race manual starts with its admission decisions.
            await manager.Initialize();
            await started[4].Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(manager.Start("workflow.run.3"), manager.Start("workflow.run.5"));
            Assert.IsFalse(started[3].Task.IsCompleted);
            Assert.IsFalse(started[5].Task.IsCompleted);
            Assert.AreEqual(3, manager.Tasks.Count(t => t.Task.Status.IsActive()));

            var download = await started[1].Task;
            await download.UpdateTask(t => t.Data = new WorkflowTaskProgress(1, process, "processingFiles", 2));
            Assert.AreEqual(BTaskStatus.Running, download.Task.Status);
            await manager.Stop("workflow.run.4");
            await manager.WaitForExecutionAsync("workflow.run.4");
            await manager.Start("workflow.run.5");
            Assert.IsFalse(started[5].Task.IsCompleted);
            await manager.Stop("workflow.run.1");
            await started[3].Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await manager.PrepareForShutdown(); }
    }

    [TestMethod]
    public async Task DifferentDynamicRunsShareTwoSlotsAndQueuedBodiesDoNotExecute()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var manager = services.GetRequiredService<BTaskManager>();
        var options = new Options();
        var resolver = new WorkflowRunSchedulingPolicyResolver([], [new AcquisitionWorkflowSchedulingPolicy(options)]);
        var ids = Enumerable.Range(1, 8).Select(i => $"acquisition-scheduling-{i}").ToArray();
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            var builder = resolver.Configure(BTaskBuilder.Create(id), 0, CloudWorkflow.Trigger, index + 1,
                [new("acquisition", "task", (index + 1).ToString())]);
            await manager.Enqueue(builder.Run(async args =>
            {
                if (Interlocked.Increment(ref entered) >= 2) bothEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, args.CancellationToken);
            }));
        }
        try
        {
            await Task.WhenAll(ids.Select(id => manager.Start(id)));
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, Volatile.Read(ref entered));
            Assert.AreEqual(2, manager.Tasks.Count(t => ids.Contains(t.Id) && t.Task.Status.IsActive()));
            Assert.AreEqual(6, manager.Tasks.Count(t => ids.Contains(t.Id) && t.Task.Status == BTaskStatus.NotStarted));
            var queued = manager.Tasks.First(t => ids.Contains(t.Id) && t.Task.Status == BTaskStatus.NotStarted);
            Assert.IsFalse(string.IsNullOrWhiteSpace(manager.GetTaskViewModel(queued.Id)!.ReasonForUnableToStart));
            var active = manager.Tasks.First(t => ids.Contains(t.Id) && t.Task.Status.IsActive());
            await manager.Stop(active.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (active.HasAttachedExecution) await Task.Delay(10, timeout.Token);
            await manager.Start(queued.Id);
            Assert.AreEqual(BTaskStatus.Running, queued.Task.Status);
        }
        finally { await manager.PrepareForShutdown(); }
    }
}
