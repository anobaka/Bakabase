using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Components;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Downloader.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Components.Tasks;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadTaskExecutionLifecycleTests
{
    private string _root = null!;
    private IServiceProvider _services = null!;
    private BtDownloadManager _downloads = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "download-executions-" + Guid.NewGuid().ToString("N"));
        _downloads = new BtDownloadManager(new WaitingTransport(), () => Path.Combine(_root, "bt"));
        _services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "database"), services =>
            services.AddSingleton<IBtDownloadManager>(_downloads));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _downloads.DisposeAsync();
        if (_services is IAsyncDisposable disposable) await disposable.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task StopCancelsIndependentBtAndWaitingRunsButLeavesAcquisitionOwnershipAlone()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var independentTask = NewTask("independent");
        var ownedTask = NewTask("acquisition");
        db.Set<DownloadTaskDbModel>().AddRange(independentTask, ownedTask);
        var definition = new WorkflowDefinitionDbModel { Name = "test", TriggerKind = DownloadResultWorkflow.Trigger };
        db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await db.SaveChangesAsync();
        var pending = new WorkflowRunDbModel { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Pending, StartedAt = DateTime.Now };
        var waiting = new WorkflowRunDbModel { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now };
        var ownedRun = new WorkflowRunDbModel { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now };
        db.Set<WorkflowRunDbModel>().AddRange(pending, waiting, ownedRun);
        var firstResult = NewResult(independentTask.Id, "first");
        var secondResult = NewResult(independentTask.Id, "second");
        var ownedResult = NewResult(ownedTask.Id, "owned");
        db.DownloadResults.AddRange(firstResult, secondResult, ownedResult);
        await db.SaveChangesAsync();
        db.DownloadResultProcessing.AddRange(
            new DownloadResultProcessingDbModel { DownloadResultId = firstResult.Id, WorkflowRunId = pending.Id },
            new DownloadResultProcessingDbModel { DownloadResultId = secondResult.Id, WorkflowRunId = waiting.Id });
        db.DownloadResultOwners.Add(new DownloadResultOwnerDbModel
        { DownloadTaskId = ownedTask.Id, AcquisitionTaskId = 42, WorkflowRunId = ownedRun.Id, ResourceId = 42 });
        await db.SaveChangesAsync();
        var independentJob = await Queue(firstResult);
        var ownedJob = await Queue(ownedResult);

        await Lifecycle().OnStoppedAsync([independentTask.Id, ownedTask.Id]);

        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(independentJob.Id))!.Status);
        Assert.AreNotEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(ownedJob.Id))!.Status);
        db.ChangeTracker.Clear();
        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await db.Set<WorkflowRunDbModel>().FindAsync(pending.Id))!.Status);
        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await db.Set<WorkflowRunDbModel>().FindAsync(waiting.Id))!.Status);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await db.Set<WorkflowRunDbModel>().FindAsync(ownedRun.Id))!.Status);
    }

    [TestMethod]
    public async Task StartOnlyContinuesLatestBtResultsAndHonorsChangedDirectMode()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var btTask = NewTask("bt");
        var directTask = NewTask("direct");
        directTask.Options = "{\"downloadMode\":1}";
        db.Set<DownloadTaskDbModel>().AddRange(btTask, directTask);
        await db.SaveChangesAsync();
        var stale = NewResult(btTask.Id, "gallery");
        var current = NewResult(btTask.Id, "gallery");
        var directOldTorrent = NewResult(directTask.Id, "gallery");
        db.DownloadResults.Add(stale);
        await db.SaveChangesAsync();
        db.DownloadResults.AddRange(current, directOldTorrent);
        await db.SaveChangesAsync();
        var staleJob = await Queue(stale);
        var currentJob = await Queue(current);
        var directJob = await Queue(directOldTorrent);
        foreach (var job in new[] { staleJob, currentJob, directJob }) await _downloads.StopAsync(job.Id);

        await Lifecycle().OnStartedAsync([btTask.Id, directTask.Id]);

        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(staleJob.Id))!.Status);
        Assert.IsTrue((await _downloads.GetAsync(currentJob.Id))!.Status is BtDownloadStatus.Queued or BtDownloadStatus.Running);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(directJob.Id))!.Status);
    }

    [TestMethod]
    public async Task SwitchingBtToMetadataOnlyStopsItsJobAndDoesNotResumeItOrDeleteItsPieces()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var task = NewTask("switch-to-metadata");
        db.DownloadTasks.Add(task);
        await db.SaveChangesAsync();
        var result = NewResult(task.Id, "gallery");
        result.WorkflowDefinitionId = 123;
        db.DownloadResults.Add(result);
        await db.SaveChangesAsync();
        var job = await Queue(result);
        Directory.CreateDirectory(job.OutputDirectory);
        var partial = Path.Combine(job.OutputDirectory, "existing.bin");
        await File.WriteAllTextAsync(partial, "keep");

        await Lifecycle().OnStoppedAsync([task.Id]);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(job.Id))!.Status);
        task.Options = "{\"downloadMode\":3,\"fallbackToDirect\":false}";
        result.RequiresContentsDownload = false;
        await db.SaveChangesAsync();
        Assert.IsTrue(DownloadTaskExecutionLifecycle.CanContinue(task, result),
            "Configured result processing remains possible; it must not resume the former independent BT job.");

        await Lifecycle().OnStartedAsync([task.Id]);

        var retained = (await _downloads.ListAsync()).Single();
        Assert.AreEqual(job.Id, retained.Id);
        Assert.AreEqual(job.OutputDirectory, retained.OutputDirectory);
        Assert.AreEqual(BtDownloadStatus.Stopped, retained.Status);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(partial));

        // The source starts its producer asynchronously. Restore the BT route before that producer
        // has changed this same result back to RequiresContentsDownload=true.
        task.Options = "{\"downloadMode\":2,\"fallbackToDirect\":false}";
        await db.SaveChangesAsync();
        await Lifecycle().OnStartedAsync([task.Id]);
        Assert.IsTrue((await _downloads.GetAsync(job.Id))!.Status is BtDownloadStatus.Queued or BtDownloadStatus.Running);
        Assert.IsFalse((await db.DownloadResults.AsNoTracking().SingleAsync(r => r.Id == result.Id)).RequiresContentsDownload);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(partial));
    }

    [TestMethod]
    public async Task MetadataOnlyStartDoesNotRetryTheFormerImageResultWorkflow()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var source = NewTask("former-images");
        source.Options = "{\"preferTorrent\":true}";
        var definition = new WorkflowDefinitionDbModel {Name = "image workflow", TriggerKind = DownloadResultWorkflow.Trigger};
        db.DownloadTasks.Add(source);
        db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await db.SaveChangesAsync();
        var result = NewResult(source.Id, "gallery");
        result.Kind = DownloadResultKind.LocalFiles;
        result.RequiresContentsDownload = false;
        result.WorkflowDefinitionId = definition.Id;
        var run = new WorkflowRunDbModel
        {WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Cancelled, StartedAt = DateTime.Now};
        db.DownloadResults.Add(result);
        db.Set<WorkflowRunDbModel>().Add(run);
        await db.SaveChangesAsync();
        db.DownloadResultProcessing.Add(new DownloadResultProcessingDbModel
        {DownloadResultId = result.Id, WorkflowRunId = run.Id});
        await db.SaveChangesAsync();

        await Lifecycle().OnStartedAsync([source.Id]);

        Assert.AreEqual(1, await db.Set<WorkflowRunDbModel>().CountAsync());
        Assert.AreEqual(WorkflowRunStatus.Cancelled,
            (await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync()).Status);
        Assert.IsNull((await db.DownloadResultProcessing.AsNoTracking().SingleAsync()).DispatchError);
    }

    [TestMethod]
    public async Task ParentStartHonorsAnExplicitlyStoppedChildAcrossRestart()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var source = NewTask("held child");
        db.Set<DownloadTaskDbModel>().Add(source);
        await db.SaveChangesAsync();
        var result = NewResult(source.Id, "gallery");
        db.DownloadResults.Add(result);
        await db.SaveChangesAsync();
        var job = await Queue(result);
        await _downloads.StopAsync(job.Id);
        var registry = scope.ServiceProvider.GetRequiredService<DownloadTaskRegistry>();
        await registry.EnsureBindingAsync(DownloadCenterBindings.For(job));
        await registry.SetUserStoppedAsync("bt:" + job.Id, true);

        await Lifecycle().OnStartedAsync([source.Id]);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(job.Id))!.Status);
        var policy = ActivatorUtilities.CreateInstance<BtDownloadStartupPolicy>(_services);
        Assert.IsFalse(await policy.CanResumeAsync(job, CancellationToken.None));

        await registry.SetUserStoppedAsync("bt:" + job.Id, false);
        await Lifecycle().OnStartedAsync([source.Id]);
        Assert.IsTrue((await _downloads.GetAsync(job.Id))!.Status is BtDownloadStatus.Queued or BtDownloadStatus.Running);
    }

    [TestMethod]
    public void ModeChangesDoNotRestartOldRequiredBtButPreserveLegacyWorkflowDownloads()
    {
        var task = NewTask("mode");
        task.Options = "{\"downloadMode\":3}";
        var result = NewResult(1, "gallery");
        Assert.IsFalse(DownloadTaskExecutionLifecycle.CanContinue(task, result));
        result.RequiresContentsDownload = false;
        result.WorkflowDefinitionId = 123;
        Assert.IsTrue(DownloadTaskExecutionLifecycle.CanContinue(task, result));
        task.Options = "{\"downloadMode\":1}";
        Assert.IsFalse(DownloadTaskExecutionLifecycle.CanContinue(task, result));
        result.Kind = DownloadResultKind.LocalFiles;
        Assert.IsTrue(DownloadTaskExecutionLifecycle.CanContinue(task, result));
        task.Options = "{\"downloadMode\":3}";
        Assert.IsFalse(DownloadTaskExecutionLifecycle.CanContinue(task, result),
            "An old image result must not resume processing after selecting seed-only output.");
    }

    [TestMethod]
    public async Task StartupPolicyAllowsManualButDefersEveryWorkflowAssociation()
    {
        var policy = ActivatorUtilities.CreateInstance<BtDownloadStartupPolicy>(_services);
        var manual = new BtDownloadJob { Id = "manual", Name = "manual", OutputDirectory = _root };
        Assert.IsTrue(await policy.CanResumeAsync(manual, CancellationToken.None));
        Assert.IsFalse(await policy.CanResumeAsync(manual with { SourceKind = "workflow" }, CancellationToken.None));
        Assert.IsFalse(await policy.CanResumeAsync(manual with { WorkflowRunId = 1 }, CancellationToken.None));
        Assert.IsFalse(await policy.CanResumeAsync(manual with { SourceKind = "download-result" }, CancellationToken.None));
    }

    [TestMethod]
    public async Task StartupPolicyRequiresLatestIndependentBtResultAndActiveSource()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var source = NewTask("startup");
        db.Set<DownloadTaskDbModel>().Add(source);
        await db.SaveChangesAsync();
        var result = NewResult(source.Id, "gallery");
        db.DownloadResults.Add(result);
        await db.SaveChangesAsync();
        var policy = ActivatorUtilities.CreateInstance<BtDownloadStartupPolicy>(_services);
        var job = new BtDownloadJob
        {
            Id = "result", Name = "result", OutputDirectory = _root, SourceKind = "download-result",
            DownloadTaskId = source.Id, DownloadResultId = result.Id
        };
        Assert.IsTrue(await policy.CanResumeAsync(job, CancellationToken.None));
        Assert.IsFalse(await policy.CanResumeAsync(job with { WorkflowRunId = 1 }, CancellationToken.None));
        source.Status = DownloadTaskDbModelStatus.Disabled;
        await db.SaveChangesAsync();
        Assert.IsFalse(await policy.CanResumeAsync(job, CancellationToken.None));
        source.Status = DownloadTaskDbModelStatus.InProgress;
        foreach (var mode in new[] { 1, 3 })
        {
            source.Options = "{\"downloadMode\":" + mode + "}";
            await db.SaveChangesAsync();
            Assert.IsFalse(await policy.CanResumeAsync(job, CancellationToken.None));
        }
        source.Options = "{\"downloadMode\":2}";
        result.RequiresContentsDownload = false;
        await db.SaveChangesAsync();
        Assert.IsFalse(await policy.CanResumeAsync(job, CancellationToken.None));
        result.RequiresContentsDownload = true;
        var owner = new DownloadResultOwnerDbModel
        { DownloadTaskId = source.Id, AcquisitionTaskId = 1, WorkflowRunId = 1, ResourceId = 1 };
        db.DownloadResultOwners.Add(owner);
        await db.SaveChangesAsync();
        Assert.IsFalse(await policy.CanResumeAsync(job, CancellationToken.None));
        db.DownloadResultOwners.Remove(owner);
        var newer = NewResult(source.Id, result.SourceKey);
        db.DownloadResults.Add(newer);
        await db.SaveChangesAsync();
        Assert.IsFalse(await policy.CanResumeAsync(job, CancellationToken.None));
        Assert.IsTrue(await policy.CanResumeAsync(job with { DownloadResultId = newer.Id }, CancellationToken.None));
        db.Set<DownloadTaskDbModel>().Remove(source);
        await db.SaveChangesAsync();
        Assert.IsFalse(await policy.CanResumeAsync(job with { DownloadResultId = newer.Id }, CancellationToken.None));
        Assert.IsFalse(await policy.CanResumeAsync(job with { DownloadResultId = int.MaxValue }, CancellationToken.None));
    }

    [TestMethod]
    public async Task StartingLegacySourceLeavesWorkflowBtContinuationToItsRunner()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var source = NewTask("legacy-workflow");
        source.Options = "{\"downloadMode\":3}";
        var definition = new WorkflowDefinitionDbModel
        { Name = "disabled", TriggerKind = DownloadResultWorkflow.Trigger, Enabled = false };
        db.Set<DownloadTaskDbModel>().Add(source);
        db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await db.SaveChangesAsync();
        var result = NewResult(source.Id, "legacy");
        result.RequiresContentsDownload = false;
        result.WorkflowDefinitionId = definition.Id;
        db.DownloadResults.Add(result);
        await db.SaveChangesAsync();
        var workflowSource = await _downloads.EnsureQueuedAsync(new BtDownloadRequest
        {
            Metadata = [1], OutputDirectory = Path.Combine(_root, "workflow-source"), SourceKind = "workflow",
            DownloadTaskId = source.Id, DownloadResultId = result.Id, WorkflowDefinitionId = definition.Id
        });
        var workflowAssociation = await _downloads.EnsureQueuedAsync(new BtDownloadRequest
        {
            Metadata = [1], OutputDirectory = Path.Combine(_root, "workflow-association"), SourceKind = "download-result",
            DownloadTaskId = source.Id, DownloadResultId = result.Id, WorkflowRunId = 1, WorkflowDefinitionId = definition.Id
        });
        await _downloads.StopAsync(workflowSource.Id);
        await _downloads.StopAsync(workflowAssociation.Id);

        await Lifecycle().OnStartedAsync([source.Id]);

        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(workflowSource.Id))!.Status);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _downloads.GetAsync(workflowAssociation.Id))!.Status);
    }

    [TestMethod]
    public async Task CleanupPreservesActiveAndAllExecutionLinksWithEqualTimestampRetention()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var definition = new WorkflowDefinitionDbModel { Name = "cleanup", TriggerKind = DownloadResultWorkflow.Trigger };
        db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await db.SaveChangesAsync();
        var timestamp = DateTime.Now;
        var history = Enumerable.Range(0, 53).Select(_ => new WorkflowRunDbModel
        { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Success, StartedAt = timestamp }).ToArray();
        var active = new[] { WorkflowRunStatus.Pending, WorkflowRunStatus.Running, WorkflowRunStatus.Waiting }
            .Select(status => new WorkflowRunDbModel { WorkflowDefinitionId = definition.Id, Status = status, StartedAt = timestamp.AddDays(-1) }).ToArray();
        db.Set<WorkflowRunDbModel>().AddRange(history.Concat(active));
        await db.SaveChangesAsync();
        db.DownloadResultProcessing.Add(new DownloadResultProcessingDbModel { DownloadResultId = 9001, WorkflowRunId = history[0].Id });
        db.DownloadResultOwners.Add(new DownloadResultOwnerDbModel { DownloadTaskId = 9001, WorkflowRunId = history[1].Id, ResourceId = 1, AcquisitionTaskId = 1 });
        await db.SaveChangesAsync();

        var prune = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(db, definition.Id, [history[2].Id], 49);

        CollectionAssert.AreEqual(new[] { history[3].Id }, prune.ToArray());
        Assert.IsFalse(prune.Intersect(active.Select(r => r.Id)).Any());
    }

    [DataTestMethod]
    [DataRow("http", WorkflowRunStatus.Failed)]
    [DataRow("bt", WorkflowRunStatus.Cancelled)]
    [DataRow("site", WorkflowRunStatus.Success)]
    public async Task CleanupPreservesCanonicalDownloadLinksWithoutConsumingHistoryBudget(string provider, WorkflowRunStatus status)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var definition = new WorkflowDefinitionDbModel { Name = "download-history", TriggerKind = "test" };
        db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await db.SaveChangesAsync();
        var timestamp = DateTime.Now;
        var linked = new WorkflowRunDbModel
        { WorkflowDefinitionId = definition.Id, Status = status, StartedAt = timestamp.AddDays(-2) };
        var unlinked = Enumerable.Range(0, 51).Select(_ => new WorkflowRunDbModel
        { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Success, StartedAt = timestamp }).ToArray();
        db.Set<WorkflowRunDbModel>().Add(linked);
        db.Set<WorkflowRunDbModel>().AddRange(unlinked);
        await db.SaveChangesAsync();
        db.ManagedDownloadTasks.Add(new ManagedDownloadTaskDbModel
        {
            Id = provider + ":retained", Provider = provider, ProviderTaskId = "retained",
            Category = provider == "site" ? "site" : "general", OriginKind = "workflow",
            WorkflowRunId = linked.Id, WorkflowDefinitionId = definition.Id, CreatedAt = timestamp
        });
        await db.SaveChangesAsync();

        var prune = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(db, definition.Id, [], 50);

        CollectionAssert.AreEqual(new[] { unlinked[0].Id }, prune.ToArray(),
            "A canonical download must keep its workflow details and retry entry even when the transfer snapshot is absent.");
        await db.ManagedDownloadTasks.ExecuteDeleteAsync();
        prune = await WorkflowRunCleanupTask.GetPrunableRunIdsAsync(db, definition.Id, [], 50);
        CollectionAssert.AreEquivalent(new[] { unlinked[0].Id, linked.Id }, prune.ToArray(),
            "Removing the last task reference should release the run to normal retention.");
    }

    [TestMethod]
    public async Task BTaskExecutionWaitIncludesCancellationCleanup()
    {
        var manager = _services.GetRequiredService<BTaskManager>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = "test.execution-drain." + Guid.NewGuid().ToString("N");
        await manager.Enqueue(BTaskBuilder.Create(id).Run(async args =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, args.CancellationToken); }
            finally { await cleanup.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        }));
        await manager.Start(id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.Stop(id);
        var drain = manager.WaitForExecutionAsync(id);
        Assert.IsFalse(drain.IsCompleted);
        cleanup.TrySetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private DownloadTaskExecutionLifecycle Lifecycle() => ActivatorUtilities.CreateInstance<DownloadTaskExecutionLifecycle>(_services);

    private DownloadTaskDbModel NewTask(string name) => new()
    {
        Key = name, Name = name, ThirdPartyId = ThirdPartyId.ExHentai, DownloadPath = _root,
        Status = DownloadTaskDbModelStatus.InProgress, Options = "{\"downloadMode\":2}"
    };

    private DownloadResultDbModel NewResult(int taskId, string sourceKey) => new()
    {
        DownloadTaskId = taskId, ThirdPartyId = ThirdPartyId.ExHentai, SourceKey = sourceKey,
        Name = sourceKey, Kind = DownloadResultKind.TorrentMetadata, RequiresContentsDownload = true,
        Path = Path.Combine(_root, "metadata.torrent"), DownloadDirectory = _root,
        Fingerprint = Guid.NewGuid().ToString("N"), DeduplicationKey = Guid.NewGuid().ToString("N")
    };

    private Task<BtDownloadJob> Queue(DownloadResultDbModel result) => _downloads.EnsureQueuedAsync(new BtDownloadRequest
    {
        Metadata = [1], OutputDirectory = Path.Combine(_root, "output", result.Id.ToString()),
        DownloadTaskId = result.DownloadTaskId, DownloadResultId = result.Id,
        SourceKind = "download-result", SourceId = result.Id.ToString(), IdempotencyKey = "result:" + result.Id
    });

    private sealed class WaitingTransport : ITorrentDownloader
    {
        public async Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException();
        }
        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}
