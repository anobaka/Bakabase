using System.Collections.Concurrent;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class BTaskTelemetryTests
{
    private static readonly TelemetryActor Actor = new(Guid.NewGuid().ToString("D"), Surface: "browser");

    [TestMethod]
    public async Task QueuedTaskCarriesActorButNeverItsPrivateNameOrId()
    {
        var accessor = new ActorAccessor { CurrentActor = Actor };
        var telemetry = new Recorder();
        await using var services = Services(accessor, telemetry);
        TelemetryActor? inBody = null;
        var task = new BTask("workflow.run.private-record", () => "private file name");
        var handler = new BTaskHandler(args =>
        {
            inBody = args.TelemetryActor;
            Assert.AreEqual(Actor, TelemetryExecutionContext.CurrentActor);
            return Task.CompletedTask;
        }, task, services);
        accessor.CurrentActor = null;
        await handler.Start(automatically: true);
        await handler.WaitForExecutionAsync();
        Assert.AreEqual(Actor, inBody);
        var captured = telemetry.Events.Single();
        Assert.AreEqual("task_attempt_finished", captured.Name);
        Assert.AreEqual(Actor, captured.Actor);
        Assert.AreEqual("workflow", captured.Properties["feature_id"]);
        Assert.AreEqual("manual", captured.Properties["trigger"]);
        Assert.AreEqual("success", captured.Properties["outcome"]);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(captured.Properties).Contains("private"));
        Assert.IsFalse(TelemetryExecutionContext.HasScope);
    }

    [TestMethod]
    public async Task RecurringAutomaticRunDoesNotInheritPreviousManualActor()
    {
        var accessor = new ActorAccessor { CurrentActor = Actor };
        var telemetry = new Recorder();
        await using var services = Services(accessor, telemetry);
        var task = new BTask("CustomRecurringWork", () => "task", isPersistent: true) { Interval = TimeSpan.Zero };
        var handler = new BTaskHandler(_ => Task.CompletedTask, task, services);
        await handler.Start();
        await handler.WaitForExecutionAsync();
        accessor.CurrentActor = null;
        await handler.Start(automatically: true);
        await handler.WaitForExecutionAsync();
        var events = telemetry.Events.ToArray();
        Assert.AreEqual(2, events.Length);
        Assert.AreEqual(Actor, events[0].Actor);
        Assert.IsNull(events[1].Actor);
        Assert.AreEqual("scheduled", events[1].Properties["trigger"]);
        Assert.AreNotEqual(events[0].Properties["attempt_id"], events[1].Properties["attempt_id"]);
    }

    [TestMethod]
    public async Task RetryIsAnAttemptOutcomeAndKeepsTheOriginalActor()
    {
        var accessor = new ActorAccessor { CurrentActor = Actor };
        var telemetry = new Recorder();
        await using var services = Services(accessor, telemetry);
        var task = new BTask("download-private-id", () => "private", type: BTaskType.Download,
            retryPolicy: new BTaskRetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.Zero });
        var runs = 0;
        var handler = new BTaskHandler(_ => ++runs == 1 ? throw new InvalidOperationException("secret") : Task.CompletedTask,
            task, services);
        await handler.Start();
        await handler.WaitForExecutionAsync();
        accessor.CurrentActor = null;
        await handler.Start(automatically: true);
        await handler.WaitForExecutionAsync();
        var events = telemetry.Events.ToArray();
        Assert.AreEqual("retry_scheduled", events[0].Properties["outcome"]);
        Assert.AreEqual("success", events[1].Properties["outcome"]);
        Assert.AreEqual(Actor, events[1].Actor);
        Assert.AreEqual("Download", events[1].Properties["task_type"]);
    }

    [TestMethod]
    [DataRow("MetadataSync")]
    [DataRow("ResourceData")]
    [DataRow("Enhancement")]
    [DataRow("AcquisitionQueue")]
    [DataRow("SubscriptionCheck")]
    [DataRow("DataSync")]
    [DataRow("WorkflowRunCleanup")]
    [DataRow("SyncSteam")]
    [DataRow("SyncDLsite")]
    [DataRow("SyncExHentai")]
    [DataRow("ResourceProcessing")]
    [DataRow("DownloadResultDispatch")]
    [DataRow("PlatformFetchWatch")]
    [DataRow("WorkflowScheduler")]
    public async Task BackgroundMaintenanceExcludesAllOutcomesButKeepsManualActivity(string id)
    {
        var accessor = new ActorAccessor();
        var telemetry = new Recorder();
        await using var services = Services(accessor, telemetry);
        var fail = false;
        var task = new BTask(id, () => id, isPersistent: true) { Interval = TimeSpan.Zero };
        var handler = new BTaskHandler(_ => fail ? throw new InvalidOperationException("failure") : Task.CompletedTask, task, services);
        await handler.Start(automatically: true);
        await handler.WaitForExecutionAsync();
        Assert.AreEqual(BTaskStatus.Completed, task.Status);
        fail = true;
        await handler.Start(automatically: true);
        await handler.WaitForExecutionAsync();
        Assert.AreEqual(BTaskStatus.Error, task.Status);
        Assert.AreEqual(0, telemetry.Events.Count);
        accessor.CurrentActor = Actor;
        fail = false;
        await handler.Start();
        await handler.WaitForExecutionAsync();
        Assert.AreEqual(Actor, telemetry.Events.Single().Actor);
    }

    [TestMethod]
    public async Task WaitingIsNotSuccessAndTelemetryFailureDoesNotChangeTaskOutcome()
    {
        var telemetry = new Recorder();
        await using var services = Services(new ActorAccessor(), telemetry);
        var waiting = new BTaskHandler(_ => throw new BTaskSuspendedException("secret"),
            new BTask("waiting", () => "waiting"), services);
        await waiting.Start();
        await waiting.WaitForExecutionAsync();
        Assert.AreEqual("waiting", telemetry.Events.Single().Properties["outcome"]);
        telemetry.Throw = true;
        var completed = new BTaskHandler(_ => Task.CompletedTask, new BTask("done", () => "done"), services);
        await completed.Start();
        await completed.WaitForExecutionAsync();
        Assert.AreEqual(BTaskStatus.Completed, completed.Task.Status);
    }

    [TestMethod]
    public void EnhancementReportsOnePartialSummaryForManyResources()
    {
        var telemetry = new Recorder();
        using var services = Services(new ActorAccessor(), telemetry);
        using (var batch = new EnhancementTelemetryBatch(services, [(int)EnhancerId.DLsite, (int)EnhancerId.DLsite,
                   (int)EnhancerId.DLsite], CancellationToken.None))
        {
            batch.Persisted((int)EnhancerId.DLsite, failed: false, empty: false);
            batch.Persisted((int)EnhancerId.DLsite, failed: true, empty: true);
            batch.Skipped((int)EnhancerId.DLsite);
            batch.Completed = true;
        }
        var captured = telemetry.Events.Single();
        Assert.AreEqual("enhancement_completed", captured.Name);
        Assert.AreEqual("partial_success", captured.Properties["outcome"]);
        Assert.AreEqual(3, captured.Properties["item_count"]);
        Assert.AreEqual(1, captured.Properties["failure_count"]);
        Assert.AreEqual(1, captured.Properties["skipped_count"]);
        Assert.AreEqual(0, captured.Properties["incomplete_count"]);
    }

    private static ServiceProvider Services(ActorAccessor accessor, Recorder recorder) => new ServiceCollection()
        .AddLogging().AddSingleton<ITelemetryActorAccessor>(accessor).AddSingleton<IProductTelemetry>(recorder).BuildServiceProvider();
    private sealed class ActorAccessor : ITelemetryActorAccessor { public TelemetryActor? CurrentActor { get; set; } }
    private sealed class Recorder : IProductTelemetry
    {
        public bool Throw { get; set; }
        public ConcurrentQueue<(string Name, IReadOnlyDictionary<string, object?> Properties, TelemetryActor? Actor)> Events { get; } = new();
        public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null, TelemetryActor? actor = null)
        {
            if (Throw) throw new InvalidOperationException("telemetry unavailable");
            Events.Enqueue((eventName, properties!, actor));
        }
    }
}
