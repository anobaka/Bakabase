using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Components;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("stopped")]
    [DataRow("failedStorage")]
    [DataRow("foreignLease")]
    [DataRow("differentMagnet")]
    [DataRow("duplicateWorkflow")]
    [DataRow("foreignRegistry")]
    [DataRow("changedAttempt")]
    public async Task LegacyBitTorrentResumesOnlyTheExclusiveRecordedOriginal(string variant)
    {
        var (task, run, _) = await Seed("download.awaitCompletion", WorkflowRunStatus.Waiting, null);
        var runId = run.Id;
        var nativeId = Guid.NewGuid().ToString("N");
        var magnet = "magnet:?xt=urn:btih:" + new string('a', 40);
        var target = new CloudDownloadTarget
        {
            Id = "part", SelectedSourceId = "source", TransportTaskId = "bt:" + nativeId,
            Sources = [new() {Id = "source", ProviderId = "magnet", Kind = "share", Url = magnet}]
        };
        var workset = new DownloadWorkset
        {
            Id = $"acquisition:{task.Id}:{task.PlanRevision}", OutputDirectory = _root,
            NoRepeatExistingDownloads = true, Targets = [target]
        };
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var storage = new UserStoragePolicy(false);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new()
        {
            Name = "Fixture", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "enqueue", Kind = "download.enqueueSupported"},
                new() {NodeId = "await", Kind = "download.awaitCompletion"}]
        });
        run.CurrentStepIndex = 1;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitReason = "cloud.cloudAttention";
        run.CompletedAt = null;
        run.Sources.Add(new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        var outputs = JsonSerializer.Serialize(workset.GetWorkflowOutputMetadata("download.enqueueSupported"), WorkflowJson.Options);
        _db.Set<WorkflowNodeExecutionDbModel>().Add(new()
        {
            WorkflowRunId = runId, StepIndex = 0, NodeId = "enqueue", Kind = "download.enqueueSupported",
            Status = "succeeded", Attempt = 1, OutputsJson = outputs
        });
        if (variant == "duplicateWorkflow")
            _db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Waiting, CurrentItemJson = run.CurrentItemJson});
        await _db.SaveChangesAsync();
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, _root);
        var owner = variant == "foreignLease" ? "workflow:99999:other" : $"workflow:{runId}:{target.Id}";
        var op = await store.ClaimAsync(new(intent, intent, "download", DownloadArtifactCoordinator.Source(target)),
            owner, TimeSpan.FromMinutes(1), default);
        Assert.AreEqual(ArtifactClaimStatus.Acquired, op.Status);
        _db.ManagedDownloadTasks.Add(new()
        {
            Id = target.TransportTaskId!, Provider = "bt", ProviderTaskId = nativeId, Category = "general",
            OriginKind = "workflow", OriginId = intent, WorkflowRunId = variant == "foreignRegistry" ? 99999 : null, UserStopped = true
        });
        await _db.SaveChangesAsync();
        if (variant == "changedAttempt")
            await _db.ArtifactOperations.Where(a => a.Id == intent).ExecuteUpdateAsync(u => u.SetProperty(a => a.Attempt, 2));
        var output = Path.Combine(_root, "cloud-" + intent[..16], "1");
        Directory.CreateDirectory(output);
        var payload = Path.Combine(output, "retained.bin");
        await File.WriteAllTextAsync(payload, "partial");
        var persistedMagnet = variant == "differentMagnet" ? "magnet:?xt=urn:btih:" + new string('b', 40) : magnet;
        var stopped = variant != "failedStorage";
        var records = Path.Combine(_root, "native");
        Directory.CreateDirectory(records);
        var recordPath = Path.Combine(records, nativeId + ".json");
        var record = JsonSerializer.Serialize(new
        {
            Job = new BtDownloadJob
            {
                Id = nativeId, Name = "Fixture", OutputDirectory = output, IsLinked = true, SourceKind = "workflow", SourceId = intent,
                Status = stopped ? BtDownloadStatus.Stopped : BtDownloadStatus.Failed, Error = stopped ? null : "LOCAL_STORAGE_UNAVAILABLE",
                CompletedBytes = 7
            },
            IdempotencyKey = intent + ":1", MagnetUri = persistedMagnet,
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("magnet:" + persistedMagnet))),
            Timeout = TimeSpan.FromMinutes(1)
        });
        await File.WriteAllTextAsync(recordPath, record);
        var transport = new LegacyMagnetTransport();
        await using var manager = new BtDownloadManager(transport, () => records);
        var manual = new CloudManualDownloadResume(_db, manager, artifacts: store, storage: storage);
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, store, manualDownloads: manual);
        Assert.IsNull(await manual.InspectLegacyBitTorrentAsync(runId, target, _root, default));
        Assert.AreEqual(record, await File.ReadAllTextAsync(recordPath));
        await manager.InitializeAsync();

        var preview = await diagnostics.DiagnoseAsync(task, run, default, includeStoppedVerification: true);
        var selected = await manual.ReadMultipleAsync(runId, workset, default);
        var allowed = variant is "stopped" or "failedStorage";
        Assert.AreEqual(allowed, preview.Row.CanContinue);
        Assert.AreEqual(allowed, selected!.Single().CanRetry);
        Assert.AreEqual(record, await File.ReadAllTextAsync(recordPath));
        Assert.AreEqual(0, transport.Calls);

        // The workflow runner persists the same checkpoint at the running cursor before invoking its selected resume.
        run.Status = WorkflowRunStatus.Running;
        run.WaitReason = null;
        await _db.SaveChangesAsync();
        var sites = _services.GetRequiredService<DownloadTaskService>();
        if (!allowed)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                manual.ResumeAsync(runId, target, _root, sites, default));
            Assert.AreEqual(record, await File.ReadAllTextAsync(recordPath));
            Assert.AreEqual(0, transport.Calls);
            return;
        }

        var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(preview.SignalJson, WorkflowJson.Options)!;
        CollectionAssert.AreEqual(new[] {target.Id}, signal.ResumeTargetIds!);
        Assert.IsTrue(signal.ObserveExistingOnly == true);
        Assert.IsNull(signal.RevalidateExistingDownloads);
        using var time = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var activity = new AwaitCloudDownloadsActivity();
        var outcome = await activity.ResumeAsync(new()
        {
            RunId = runId, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = workset,
            ActivityConfigJson = "{}", Services = new LegacyMagnetServices(_services, manual, manager),
            Logger = NullLogger.Instance
        }, workset, preview.SignalJson, time.Token);
        Assert.IsNull(outcome.Suspension);
        var finished = ((DownloadWorkset)outcome.Replacement!).Targets.Single();
        Assert.AreEqual("bt:" + nativeId, finished.TransportTaskId);
        Assert.AreEqual(nativeId, (await manager.WaitForCompletionAsync(nativeId, ct: time.Token)).Id);
        Assert.AreEqual(1, (await manager.ReadSnapshotAsync()).Count);
        Assert.AreEqual(1, (await store.GetOperationAsync(intent, default))!.Attempt);
        Assert.AreEqual("partial-completed", await File.ReadAllTextAsync(payload));
        Assert.AreEqual(1, transport.Calls);
    }

    private sealed class LegacyMagnetServices(IServiceProvider fallback, CloudManualDownloadResume manual,
        IBtDownloadManager manager) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(CloudManualDownloadResume) ? manual :
            type == typeof(IBtDownloadManager) ? manager : fallback.GetService(type);
    }

    private sealed class LegacyMagnetTransport : ITorrentDownloader
    {
        public int Calls { get; private set; }

        public async Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory, TimeSpan timeout,
            Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Calls++;
            var file = Path.Combine(workingDirectory, "retained.bin");
            Assert.AreEqual("partial", await File.ReadAllTextAsync(file, ct));
            await File.AppendAllTextAsync(file, "-completed", ct);
            return new(workingDirectory, [file]);
        }

        public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory, TimeSpan timeout,
            Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();

        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory, TimeSpan timeout,
            Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}
