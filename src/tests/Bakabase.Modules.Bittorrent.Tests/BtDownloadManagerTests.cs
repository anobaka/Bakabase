using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Bakabase.Modules.Bittorrent.Components;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Bittorrent.Tests;

[TestClass]
public sealed class BtDownloadManagerTests
{
    private string _root = null!;
    private readonly List<BtDownloadManager> _managers = [];

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bt-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        foreach (var manager in _managers) await manager.DisposeAsync();
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task CompletedTransferUsesExactDestinationAndStructuredProgress()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = Request() with { Metadata = [1, 2, 3], MagnetUri = null };
        var job = await manager.EnsureQueuedAsync(request);
        var transfer = await transport.NextAsync();
        Assert.AreEqual(request.OutputDirectory, transfer.Request.WorkingDirectory);
        Assert.IsTrue(transfer.Request.UseWorkingDirectoryAsDestination);
        CollectionAssert.AreEqual(request.Metadata, transfer.Request.Metadata);
        await transfer.ReportAsync(new TorrentTransferProgress
        { Stage = "Downloading", Percentage = 25, CompletedBytes = 25, TotalBytes = 100, SpeedBytesPerSecond = 12, Peers = 3 });
        var current = await manager.GetAsync(job.Id);
        Assert.AreEqual(25L, current!.CompletedBytes);
        Assert.AreEqual(3, current.Peers);
        transfer.Succeed();
        var completed = await manager.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(BtDownloadStatus.Completed, completed.Status);
        Assert.AreEqual(100, completed.Percentage);
        Assert.AreEqual(100L, completed.CompletedBytes);
        Assert.IsNotNull(completed.CompletedAt);
        Assert.AreEqual(1, completed.Files.Count);
        Assert.IsFalse(JsonSerializer.Serialize(completed).Contains("MagnetUri"));
        var json = await File.ReadAllTextAsync(Path.Combine(_root, "store", job.Id + ".json"));
        StringAssert.Contains(json, $"metadata/{job.Id}.torrent");
    }

    [TestMethod]
    public async Task ConcurrentIdempotentRequestsCreateOneTransferAndRejectChangedRequest()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = Request() with { IdempotencyKey = "download-result:1", SourceKind = "download-result", DownloadResultId = 1 };
        var jobs = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => manager.EnsureQueuedAsync(request)));
        Assert.AreEqual(1, jobs.Select(x => x.Id).Distinct().Count());
        await transport.NextAsync();
        Assert.AreEqual(1, (await manager.ListAsync()).Count);
        Assert.AreEqual(jobs[0].Id, (await manager.FindByIdempotencyKeyAsync(request.IdempotencyKey))!.Id);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(request with { OutputDirectory = _root }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(request with { MagnetUri = "magnet:?xt=urn:btih:different" }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(request with { DownloadResultId = 2 }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(request with { WorkflowDefinitionId = 2 }));
    }

    [TestMethod]
    public async Task IdempotentTerminalRequestUpdatesTimeoutForNextAttemptWithoutRestarting()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = Request() with { IdempotencyKey = "workflow:timeout", Timeout = TimeSpan.FromMinutes(30) };
        var job = await manager.EnsureQueuedAsync(request);
        var first = await transport.NextAsync();
        Assert.AreEqual(request.Timeout, first.Request.Timeout);
        await manager.EnsureQueuedAsync(request with { Timeout = TimeSpan.FromHours(2) });
        using (var record = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "store", job.Id + ".json"))))
            Assert.AreEqual(request.Timeout, TimeSpan.Parse(record.RootElement.GetProperty("Timeout").GetString()!));
        await manager.StopAsync(job.Id);
        var updated = await manager.EnsureQueuedAsync(request with { Timeout = TimeSpan.FromHours(2) });
        Assert.AreEqual(BtDownloadStatus.Stopped, updated.Status);
        await manager.ShutdownAsync();

        var resumedTransport = new ControlledTransport();
        var restarted = Create(resumedTransport);
        await restarted.ResumeAsync(job.Id);
        var second = await resumedTransport.NextAsync();
        Assert.AreEqual(TimeSpan.FromHours(2), second.Request.Timeout);
        second.Succeed();
        await restarted.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(BtDownloadStatus.Completed,
            (await restarted.EnsureQueuedAsync(request with { Timeout = TimeSpan.FromHours(3) })).Status);
        await restarted.RecheckAsync(job.Id);
        var third = await resumedTransport.NextAsync();
        Assert.AreEqual(TimeSpan.FromHours(3), third.Request.Timeout);
        third.Fail(new TimeoutException());
        await Assert.ThrowsExceptionAsync<BtDownloadJobException>(() => restarted.WaitForCompletionAsync(job.Id));
        Assert.AreEqual(BtDownloadStatus.Failed,
            (await restarted.EnsureQueuedAsync(request with { Timeout = TimeSpan.FromHours(4) })).Status);
        await restarted.ResumeAsync(job.Id);
        Assert.AreEqual(TimeSpan.FromHours(4), (await resumedTransport.NextAsync()).Request.Timeout);
    }

    [TestMethod]
    public async Task BoundedConcurrencyReadsLiveLimitAndHoldsSlotUntilStopped()
    {
        var transport = new ControlledTransport { DelayCancellation = true };
        var limit = 1;
        var manager = Create(transport, () => limit);
        var first = await manager.EnsureQueuedAsync(Request("first"));
        var firstTransfer = await transport.NextAsync();
        await manager.EnsureQueuedAsync(Request("second"));
        await manager.EnsureQueuedAsync(Request("third"));
        await Task.Delay(350);
        Assert.AreEqual(1, transport.StartedCount);
        limit = 2;
        await transport.NextAsync();
        var stop = manager.StopAsync(first.Id);
        await UntilAsync(async () => (await manager.GetAsync(first.Id))!.Status == BtDownloadStatus.Stopping);
        await Task.Delay(350);
        Assert.AreEqual(2, transport.StartedCount);
        firstTransfer.ReleaseCancellation();
        await stop;
        await transport.NextAsync();
        Assert.AreEqual(3, transport.StartedCount);
        Assert.IsTrue(transport.MaximumActive <= 2);
        transport.ReleaseAllCancellation();
    }

    [TestMethod]
    public async Task StopWinningCompletionRaceStaysStoppedAndResumeKeepsIdAndPath()
    {
        var transport = new ControlledTransport { IgnoreCancellation = true };
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(Request());
        var transfer = await transport.NextAsync();
        var stop = manager.StopAsync(job.Id);
        await UntilAsync(async () => (await manager.GetAsync(job.Id))!.Status == BtDownloadStatus.Stopping);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.ResumeAsync(job.Id));
        transfer.Succeed();
        Assert.AreEqual(BtDownloadStatus.Stopped, (await stop).Status);
        var continued = await manager.ResumeAsync(job.Id);
        Assert.AreEqual(job.Id, continued.Id);
        Assert.AreEqual(job.OutputDirectory, continued.OutputDirectory);
        var resumedTransfer = await transport.NextAsync();
        resumedTransfer.Succeed();
        await manager.WaitForCompletionAsync(job.Id);
    }

    [TestMethod]
    public async Task ShutdownRestartsInterruptedTransfersButLeavesExplicitStopsStopped()
    {
        var transport = new ControlledTransport();
        var firstManager = Create(transport, () => 1);
        var running = await firstManager.EnsureQueuedAsync(Request("running"));
        await transport.NextAsync();
        var stopped = await firstManager.EnsureQueuedAsync(Request("stopped"));
        await firstManager.StopAsync(stopped.Id);
        await firstManager.ShutdownAsync();
        var secondTransport = new ControlledTransport();
        var restarted = Create(secondTransport, () => 1);
        await restarted.InitializeAsync();
        var transfer = await secondTransport.NextAsync();
        Assert.AreEqual(running.OutputDirectory, transfer.Request.WorkingDirectory);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await restarted.GetAsync(stopped.Id))!.Status);
        Assert.AreEqual(2, (await restarted.ListAsync()).Count);
        transfer.Succeed();
        await restarted.WaitForCompletionAsync(running.Id);
    }

    [TestMethod]
    public async Task StartupPolicyStopsDisallowedOwnersBeforeStartingAnyTransfers()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport, () => 3);
        var owned = await manager.EnsureQueuedAsync(Request("workflow") with { SourceKind = "workflow", WorkflowRunId = 12 });
        var disabledSource = await manager.EnsureQueuedAsync(Request("disabled") with { SourceKind = "download-result", DownloadTaskId = 34 });
        var manual = await manager.EnsureQueuedAsync(Request("manual"));
        await transport.NextAsync();
        await transport.NextAsync();
        await transport.NextAsync();
        var stopped = await manager.EnsureQueuedAsync(Request("stopped"));
        await manager.StopAsync(stopped.Id);
        await manager.ShutdownAsync();
        var interruptedRecord = Path.Combine(_root, "store", owned.Id + ".json");
        var persisted = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(interruptedRecord))!;
        persisted["Job"]!["Status"] = (int)BtDownloadStatus.Running;
        await File.WriteAllTextAsync(interruptedRecord, persisted.ToJsonString());

        var recoveredTransport = new ControlledTransport();
        var inspected = new List<string>();
        var restarted = new BtDownloadManager(recoveredTransport, () => Path.Combine(_root, "store"),
            canResumeOnStartup: (job, _) =>
            {
                inspected.Add(job.Id);
                return Task.FromResult(job.SourceKind == "manual");
            });
        _managers.Add(restarted);
        await restarted.InitializeAsync();
        Assert.AreEqual(manual.OutputDirectory, (await recoveredTransport.NextAsync()).Request.WorkingDirectory);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await restarted.GetAsync(owned.Id))!.Status);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await restarted.GetAsync(disabledSource.Id))!.Status);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await restarted.GetAsync(stopped.Id))!.Status);
        Assert.AreEqual(3, inspected.Count);
        Assert.IsFalse(inspected.Contains(stopped.Id));
        Assert.AreEqual(1, recoveredTransport.StartedCount);
        await restarted.ResumeAsync(owned.Id);
        Assert.AreEqual(owned.OutputDirectory, (await recoveredTransport.NextAsync()).Request.WorkingDirectory);
    }

    [TestMethod]
    public async Task ExpiredShutdownDeadlineStillSignalsCancellationToActiveTransfers()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(Request());
        await transport.NextAsync();
        using var deadline = new CancellationTokenSource();
        await deadline.CancelAsync();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => manager.ShutdownAsync(deadline.Token));
        await UntilAsync(async () => (await manager.GetAsync(job.Id))!.Status == BtDownloadStatus.Queued);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(Request("after-shutdown")));
    }

    [TestMethod]
    public async Task CrashRecoveryRespectsPersistedStoppingIntent()
    {
        var manager = Create(new ControlledTransport());
        var job = await manager.EnsureQueuedAsync(Request());
        await manager.StopAsync(job.Id);
        await manager.ShutdownAsync();
        var path = Path.Combine(_root, "store", job.Id + ".json");
        var record = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        record["Job"]!["Status"] = (int)BtDownloadStatus.Stopping;
        await File.WriteAllTextAsync(path, record.ToJsonString());
        var restarted = Create(new ControlledTransport());
        Assert.AreEqual(BtDownloadStatus.Stopped, (await restarted.GetAsync(job.Id))!.Status);
    }

    [TestMethod]
    public async Task WaitCancellationOnlyStopsTransferWhenExplicitlyOwned()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(Request());
        await transport.NextAsync();
        using var firstWait = new CancellationTokenSource();
        var wait = manager.WaitForCompletionAsync(job.Id, ct: firstWait.Token);
        await firstWait.CancelAsync();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await wait);
        Assert.AreEqual(BtDownloadStatus.Running, (await manager.GetAsync(job.Id))!.Status);
        using var ownedWait = new CancellationTokenSource();
        wait = manager.WaitForCompletionAsync(job.Id, stopOnCancellation: true, ct: ownedWait.Token);
        await ownedWait.CancelAsync();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await wait);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await manager.GetAsync(job.Id))!.Status);
    }

    [TestMethod]
    public async Task FailureDoesNotPersistCredentialsAndRetryReusesExistingTransfer()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = Request() with { IdempotencyKey = "workflow:42" };
        var job = await manager.EnsureQueuedAsync(request);
        (await transport.NextAsync()).Fail(new IOException("socks5://user:private-secret@proxy.invalid"));
        var error = await Assert.ThrowsExceptionAsync<BtDownloadJobException>(() => manager.WaitForCompletionAsync(job.Id));
        Assert.AreEqual(BtDownloadStatus.Failed, error.Job.Status);
        var json = await File.ReadAllTextAsync(Path.Combine(_root, "store", job.Id + ".json"));
        Assert.IsFalse(json.Contains("private-secret"));
        Assert.AreEqual(BtDownloadStatus.Failed, (await manager.EnsureQueuedAsync(request)).Status);
        Assert.AreEqual(job.Id, (await manager.ResumeAsync(job.Id)).Id);
        (await transport.NextAsync()).Succeed();
        await manager.WaitForCompletionAsync(job.Id);
    }

    [TestMethod]
    public async Task RemovalNeverDeletesDataAndRejectsLinkedJobs()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var manual = await manager.EnsureQueuedAsync(Request());
        var transfer = await transport.NextAsync();
        Directory.CreateDirectory(manual.OutputDirectory);
        var file = Path.Combine(manual.OutputDirectory, "payload.bin");
        await File.WriteAllTextAsync(file, "retained");
        transfer.Succeed();
        Assert.IsTrue((await manager.WaitForCompletionAsync(manual.Id)).CanDelete);
        await manager.DeleteAsync(manual.Id);
        Assert.IsNull(await manager.GetAsync(manual.Id));
        Assert.IsTrue(File.Exists(file));
        var linked = await manager.EnsureQueuedAsync(Request("linked") with { DownloadResultId = 2 });
        await manager.StopAsync(linked.Id);
        Assert.IsFalse((await manager.GetAsync(linked.Id))!.CanDelete);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.DeleteAsync(linked.Id));
    }

    [TestMethod]
    public async Task InvalidInputsAreRejectedBeforeCreatingRecords()
    {
        var manager = Create(new ControlledTransport());
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => manager.EnsureQueuedAsync(Request() with { OutputDirectory = "relative" }));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => manager.EnsureQueuedAsync(Request() with { TorrentUrl = "https://user:password@example.invalid/file.torrent", MagnetUri = null }));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => manager.EnsureQueuedAsync(Request() with { Metadata = [1] }));
        Assert.AreEqual(0, (await manager.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ShutdownPredicatePreventsNewTransfers()
    {
        var manager = new BtDownloadManager(new ControlledTransport(), () => Path.Combine(_root, "store"), isShuttingDown: () => true);
        _managers.Add(manager);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(Request()));
    }

    [TestMethod]
    public async Task OverlappingOutputTreesWaitWhileDisjointDirectoriesRunConcurrently()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport, () => 3);
        var root = Path.Combine(_root, "shared");
        var first = await manager.EnsureQueuedAsync(Request("parent") with { OutputDirectory = root });
        var firstTransfer = await transport.NextAsync();
        var child = await manager.EnsureQueuedAsync(Request("child") with { OutputDirectory = Path.Combine(root, "child") });
        await manager.EnsureQueuedAsync(Request("separate"));
        await transport.NextAsync();
        await Task.Delay(350);
        Assert.AreEqual(2, transport.StartedCount);
        Assert.AreEqual(BtDownloadStatus.Queued, (await manager.GetAsync(child.Id))!.Status);
        firstTransfer.Succeed();
        await manager.WaitForCompletionAsync(first.Id);
        Assert.AreEqual(Path.Combine(root, "child"), (await transport.NextAsync()).Request.WorkingDirectory);
    }

    [TestMethod]
    public async Task SymbolicLinkAliasesOfOutputDirectoriesAreSerialized()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symlinks requires permission on Windows.");
        var actual = Path.Combine(_root, "actual");
        var alias = Path.Combine(_root, "alias");
        Directory.CreateDirectory(actual);
        Directory.CreateSymbolicLink(alias, actual);
        var transport = new ControlledTransport();
        var manager = Create(transport, () => 2);
        var first = await manager.EnsureQueuedAsync(Request() with { OutputDirectory = actual });
        await transport.NextAsync();
        var second = await manager.EnsureQueuedAsync(Request("alias") with { OutputDirectory = alias });
        await Task.Delay(350);
        Assert.AreEqual(1, transport.StartedCount);
        Assert.AreEqual(BtDownloadStatus.Queued, (await manager.GetAsync(second.Id))!.Status);
        await manager.StopAsync(first.Id);
        await transport.NextAsync();
    }

    [TestMethod]
    public async Task CompletedTransferOnlyRerunsWhenExplicitlyRechecked()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = Request() with { IdempotencyKey = "download-result:45", DownloadResultId = 45 };
        var job = await manager.EnsureQueuedAsync(request);
        (await transport.NextAsync()).Succeed();
        await manager.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(BtDownloadStatus.Completed, (await manager.EnsureQueuedAsync(request)).Status);
        Assert.AreEqual(BtDownloadStatus.Completed, (await manager.ResumeAsync(job.Id)).Status);
        var recheck = await manager.RecheckAsync(job.Id);
        Assert.AreEqual(job.Id, recheck.Id);
        Assert.AreEqual(BtDownloadStatus.Queued, recheck.Status);
        Assert.AreEqual(0, recheck.Files.Count);
        var transfer = await transport.NextAsync();
        Assert.AreEqual(job.OutputDirectory, transfer.Request.WorkingDirectory);
        transfer.Succeed();
        await manager.WaitForCompletionAsync(job.Id);
    }

    [TestMethod]
    public async Task PersistenceFailureIsVisibleAndCanBeRetriedAfterStorageRecovers()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(Request());
        var transfer = await transport.NextAsync();
        Directory.Move(Path.Combine(_root, "store"), Path.Combine(_root, "unavailable-store"));
        transfer.Succeed();
        var failed = await Assert.ThrowsExceptionAsync<BtDownloadJobException>(() => manager.WaitForCompletionAsync(job.Id));
        StringAssert.Contains(failed.Job.Error!, "record could not be saved");
        Directory.Move(Path.Combine(_root, "unavailable-store"), Path.Combine(_root, "store"));
        await manager.ResumeAsync(job.Id);
        (await transport.NextAsync()).Succeed();
        await manager.WaitForCompletionAsync(job.Id);
    }

    [TestMethod]
    public async Task MovingAppDataRebasesManagedOutputsAndCachedMetadataButPreservesExternalPaths()
    {
        var originalAppData = Path.Combine(_root, "app-data");
        var movedAppData = Path.Combine(_root, "moved-app-data");
        var transport = new ControlledTransport();
        var manager = new BtDownloadManager(transport, () => Path.Combine(originalAppData, "bt-downloads"),
            appDataDirectory: () => originalAppData);
        _managers.Add(manager);
        var request = Request() with
        {
            MagnetUri = null, Metadata = [1, 2, 3], OutputDirectory = Path.Combine(originalAppData, "acquisition", "contents"),
            IdempotencyKey = "workflow:42", SourceKind = "workflow", WorkflowRunId = 42
        };
        var managed = await manager.EnsureQueuedAsync(request);
        (await transport.NextAsync()).Succeed();
        await manager.WaitForCompletionAsync(managed.Id);
        var external = await manager.EnsureQueuedAsync(Request("external"));
        (await transport.NextAsync()).Succeed();
        await manager.WaitForCompletionAsync(external.Id);
        await manager.ShutdownAsync();
        var saved = await File.ReadAllTextAsync(Path.Combine(originalAppData, "bt-downloads", managed.Id + ".json"));
        Assert.IsFalse(saved.Contains(originalAppData));
        Directory.Move(originalAppData, movedAppData);
        var restarted = new BtDownloadManager(new ControlledTransport(), () => Path.Combine(movedAppData, "bt-downloads"),
            appDataDirectory: () => movedAppData);
        _managers.Add(restarted);
        var restored = await restarted.GetAsync(managed.Id);
        Assert.AreEqual(Path.Combine(movedAppData, "acquisition", "contents"), restored!.OutputDirectory);
        Assert.AreEqual(Path.Combine(movedAppData, "acquisition", "contents", "payload.bin"), restored.Files.Single());
        Assert.AreEqual(external.OutputDirectory, (await restarted.GetAsync(external.Id))!.OutputDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(movedAppData, "bt-downloads", "metadata", managed.Id + ".torrent")));
        Assert.AreEqual(managed.Id, (await restarted.EnsureQueuedAsync(request with { OutputDirectory = restored.OutputDirectory })).Id);
    }

    private BtDownloadManager Create(ControlledTransport transport, Func<int>? limit = null)
    {
        var manager = new BtDownloadManager(transport, () => Path.Combine(_root, "store"), limit);
        _managers.Add(manager);
        return manager;
    }

    private BtDownloadRequest Request(string name = "example") => new()
    {
        Name = name, MagnetUri = "magnet:?xt=urn:btih:" + name,
        OutputDirectory = Path.Combine(_root, "output", name)
    };

    private static async Task UntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledTransport : ITorrentDownloader
    {
        private readonly Channel<Transfer> _started = Channel.CreateUnbounded<Transfer>();
        private readonly ConcurrentBag<Transfer> _transfers = [];
        private int _active;
        private int _startedCount;
        private int _maximumActive;
        public bool DelayCancellation { get; init; }
        public bool IgnoreCancellation { get; init; }
        public int StartedCount => _startedCount;
        public int MaximumActive => _maximumActive;

        public async Task<Transfer> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _started.Reader.ReadAsync(timeout.Token);
        }

        public void ReleaseAllCancellation()
        {
            foreach (var transfer in _transfers) transfer.ReleaseCancellation();
        }

        public async Task<TorrentDownloadResult> DownloadAsync(TorrentDownloadRequest request,
            Func<TorrentTransferProgress, Task>? progress, CancellationToken ct)
        {
            var transfer = new Transfer(request, progress);
            _transfers.Add(transfer);
            Interlocked.Increment(ref _startedCount);
            var active = Interlocked.Increment(ref _active);
            InterlockedExtensions.Max(ref _maximumActive, active);
            await _started.Writer.WriteAsync(transfer, ct);
            try
            {
                return IgnoreCancellation ? await transfer.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5)) : await transfer.Completion.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (DelayCancellation) await transfer.CancellationReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
                throw;
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Transfer(TorrentDownloadRequest request, Func<TorrentTransferProgress, Task>? progress)
    {
        public TorrentDownloadRequest Request { get; } = request;
        public TaskCompletionSource<TorrentDownloadResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ReportAsync(TorrentTransferProgress value) => progress?.Invoke(value) ?? Task.CompletedTask;
        public void Succeed() => Completion.TrySetResult(new TorrentDownloadResult(Request.WorkingDirectory, [Path.Combine(Request.WorkingDirectory, "payload.bin")]));
        public void Fail(Exception error) => Completion.TrySetException(error);
        public void ReleaseCancellation() => CancellationReleased.TrySetResult();
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            int current;
            do { current = target; if (current >= value) return; }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }
    }
}
