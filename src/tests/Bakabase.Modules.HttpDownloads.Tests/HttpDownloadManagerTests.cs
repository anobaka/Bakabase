using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.HttpDownloads.Components;
using Bakabase.Modules.HttpDownloads.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Request = Bakabase.Modules.HttpDownloads.Models.HttpDownloadRequest;
using TransportRequest = Bakabase.Modules.Downloader.Models.HttpDownloadRequest;
using Server = Bakabase.Modules.HttpDownloads.Tests.HttpTestServer.Server;

namespace Bakabase.Modules.HttpDownloads.Tests;

[TestClass]
public sealed class HttpDownloadManagerTests
{
    private string _root = null!;
    private readonly List<HttpDownloadManager> _managers = [];
    private readonly List<ServiceProvider> _providers = [];
    private string Store => Path.Combine(_root, "store");
    private string Output => Path.Combine(_root, "output");

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "http-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        foreach (var manager in _managers) await manager.DisposeAsync();
        foreach (var provider in _providers) await provider.DisposeAsync();
        Directory.Delete(_root, true);
    }

    private HttpDownloadManager Create(IHttpDownloader? transport = null, string? store = null, string? appData = null,
        Func<HttpDownloadJob, CancellationToken, Task<bool>>? resume = null)
    {
        if (transport == null)
        {
            var provider = new ServiceCollection().AddDownloader(_ => Path.Combine(_root, "cache")).BuildServiceProvider();
            _providers.Add(provider);
            transport = provider.GetRequiredService<IHttpDownloader>();
        }
        var manager = new HttpDownloadManager(transport, () => store ?? Store, () => 2,
            appData == null ? null : () => appData, resume);
        _managers.Add(manager);
        return manager;
    }

    private Request NewRequest(string url = "https://example.invalid/file.bin") => new()
    {
        Url = url, OutputDirectory = Output, ParallelConnections = 1, MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(15)
    };

    [TestMethod, Timeout(20000)]
    public async Task RealHttpPublishesSafeServerNameAndPrivateHistory()
    {
        await using var server = new Server { ContentDisposition = "attachment; filename=\"../../payload.bin\"" };
        var manager = Create();
        var job = await manager.EnsureQueuedAsync(NewRequest(server.Url + "?token=private-secret"));
        var done = await manager.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(HttpDownloadStatus.Completed, done.Status);
        Assert.AreEqual(100, done.Percentage);
        Assert.AreEqual((long)server.Body.Length, done.CompletedBytes);
        Assert.AreEqual((long)server.Body.Length, done.TotalBytes);
        Assert.AreEqual(Output, Path.GetDirectoryName(done.Files.Single()));
        CollectionAssert.AreEqual(server.Body, await File.ReadAllBytesAsync(done.Files.Single()));
        Assert.IsFalse(JsonSerializer.Serialize(done).Contains("private-secret"));
        Assert.IsFalse(Directory.EnumerateFiles(Output).Any(p => p.EndsWith(".tmp")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(Store, job.Id + ".json")));
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Store));
        }
    }

    [DataTestMethod, Timeout(30000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoppedHttpPersistsAndResumesOrRestartsWhenRangesIgnored(bool ignoreRanges)
    {
        await using var server = new Server { Slow = true };
        var manager = Create();
        var job = await manager.EnsureQueuedAsync(NewRequest(server.Url));
        await Until(() => Task.FromResult(Interlocked.Read(ref server.BytesWritten) > 256 * 1024));
        await manager.StopAsync(job.Id);
        Assert.AreEqual(HttpDownloadStatus.Stopped, (await manager.GetAsync(job.Id))!.Status);
        Assert.IsFalse(Directory.Exists(Output) && Directory.EnumerateFiles(Output).Any());
        await manager.ShutdownAsync();
        await server.DrainPayloads();
        server.Requests.Clear();
        server.Slow = false;
        server.IgnoreRanges = ignoreRanges;
        var restarted = Create();
        await restarted.InitializeAsync();
        Assert.AreEqual(HttpDownloadStatus.Stopped, (await restarted.GetAsync(job.Id))!.Status);
        Assert.AreEqual(0, server.Requests.Count);
        await restarted.ResumeAsync(job.Id);
        var completed = await restarted.WaitForCompletionAsync(job.Id);
        CollectionAssert.AreEqual(server.Body, await File.ReadAllBytesAsync(completed.Files.Single()));
        if (!ignoreRanges) Assert.IsTrue(server.Requests.All(request => request.Start > 0), "Verified partial bytes should be reused after a new manager starts.");
        else Assert.IsTrue(server.Requests.Any(request => request.Start == 0));
    }

    [TestMethod, Timeout(30000)]
    public async Task ShutdownQueuesInterruptedTransferAndStartupPolicyCanKeepItStopped()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(NewRequest());
        await transport.Next();
        await manager.ShutdownAsync();
        var restarted = Create(transport, resume: (_, _) => Task.FromResult(false));
        await restarted.InitializeAsync();
        Assert.AreEqual(HttpDownloadStatus.Stopped, (await restarted.GetAsync(job.Id))!.Status);
        Assert.AreEqual(1, transport.Calls);
    }

    [TestMethod, Timeout(15000)]
    public async Task StructuredProgressAndAttemptOptionsReachExistingTransport()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = NewRequest() with
        { HttpClientName = "authenticated", ParallelConnections = 3, MaxRetries = 2, MaximumBytesPerSecond = 2048 };
        var job = await manager.EnsureQueuedAsync(request);
        var transfer = await transport.Next();
        Assert.AreEqual(3, transfer.Request.ParallelConnections);
        Assert.AreEqual(2, transfer.Request.MaxRetries);
        Assert.AreEqual(2048L, transfer.Request.MaximumBytesPerSecond);
        Assert.AreEqual("authenticated", transfer.Request.HttpClientName);
        Assert.IsFalse(transfer.Request.AllowOverwrite);
        await transfer.Progress!(new HttpTransferProgress { Percentage = 42, CompletedBytes = 420, TotalBytes = 1000, SpeedBytesPerSecond = 2048 });
        var snapshot = (await manager.GetAsync(job.Id))!;
        Assert.AreEqual(42, snapshot.Percentage);
        Assert.AreEqual(420L, snapshot.CompletedBytes);
        Assert.AreEqual(2048d, snapshot.SpeedBytesPerSecond);
        transfer.Finish();
        var done = await manager.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(4L, done.CompletedBytes);
        Assert.AreEqual(4L, done.TotalBytes);
    }

    [TestMethod, Timeout(15000)]
    public async Task IdempotencyValidatesOwnershipAndSourceAndNeverAutomaticallyResumes()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var request = NewRequest() with { IdempotencyKey = "site:1", DownloadTaskId = 1 };
        var jobs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.EnsureQueuedAsync(request)));
        Assert.AreEqual(1, jobs.Select(job => job.Id).Distinct().Count());
        await transport.Next();
        await manager.StopAsync(jobs[0].Id);
        var stopped = await manager.EnsureQueuedAsync(request with { MaxRetries = 5 });
        Assert.AreEqual(HttpDownloadStatus.Stopped, stopped.Status);
        Assert.AreEqual(1, transport.Calls);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(request with { Url = "https://example.invalid/other" }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.EnsureQueuedAsync(request with { DownloadTaskId = 2 }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => manager.DeleteAsync(stopped.Id));
        await manager.ResumeAsync(stopped.Id);
        var retry = await transport.Next();
        Assert.AreEqual(5, retry.Request.MaxRetries);
        retry.Finish();
        await manager.WaitForCompletionAsync(stopped.Id);
    }

    [TestMethod, Timeout(15000)]
    public async Task ConflictingOutputsSerializeAndNeverOverwriteExistingFile()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var first = await manager.EnsureQueuedAsync(NewRequest());
        var firstTransfer = await transport.Next();
        var second = await manager.EnsureQueuedAsync(NewRequest("https://example.invalid/other"));
        await Task.Delay(300);
        Assert.AreEqual(HttpDownloadStatus.Queued, (await manager.GetAsync(second.Id))!.Status);
        Assert.AreEqual(1, transport.Calls);
        firstTransfer.Finish();
        var firstDone = await manager.WaitForCompletionAsync(first.Id);
        var secondTransfer = await transport.Next();
        secondTransfer.Finish([8, 8]);
        var failure = await Assert.ThrowsExceptionAsync<HttpDownloadJobException>(() => manager.WaitForCompletionAsync(second.Id));
        StringAssert.Contains(failure.Message, "already exists");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(firstDone.Files.Single()));
        await manager.DeleteAsync(first.Id);
        Assert.IsTrue(File.Exists(firstDone.Files.Single()));
    }

    [TestMethod, Timeout(15000)]
    public async Task FailedNetworkMessagesDoNotExposePrivateUrl()
    {
        await using var server = new Server { FailRequests = true };
        var manager = Create();
        var job = await manager.EnsureQueuedAsync(NewRequest(server.Url + "?token=private-secret"));
        var failure = await Assert.ThrowsExceptionAsync<HttpDownloadJobException>(() => manager.WaitForCompletionAsync(job.Id));
        Assert.AreEqual(HttpDownloadStatus.Failed, failure.Job.Status);
        Assert.IsFalse(JsonSerializer.Serialize(failure.Job).Contains("private-secret"));
        Assert.IsTrue(failure.Job.CanRetry);
    }

    [DataTestMethod]
    [DataRow("ftp://example.com/a", "fine.bin")]
    [DataRow("https://user:secret@example.com/a", "fine.bin")]
    [DataRow("https://example.com/a", "../escape.bin")]
    [DataRow("https://example.com/a", "CON.txt")]
    public async Task InvalidPayloadsAreRejectedBeforeRecordCreation(string url, string filename)
    {
        var manager = Create(new ControlledTransport());
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => manager.EnsureQueuedAsync(NewRequest(url) with { FileName = filename }));
        Assert.IsFalse(Directory.Exists(Store));
    }

    [TestMethod, Timeout(15000)]
    public async Task AppDataRelativePathsSurviveRelocationAndPublishedCrashRecovery()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport, appData: _root);
        var job = await manager.EnsureQueuedAsync(NewRequest());
        (await transport.Next()).Finish();
        var completed = await manager.WaitForCompletionAsync(job.Id);
        await manager.ShutdownAsync();
        var recordPath = Path.Combine(Store, job.Id + ".json");
        var record = JsonNode.Parse(await File.ReadAllTextAsync(recordPath))!;
        Assert.IsTrue(record["OutputDirectoryIsAppDataRelative"]!.GetValue<bool>());
        // Simulate a crash after rename, before saving the terminal state.
        record["Job"]!["Status"] = (int)HttpDownloadStatus.Running;
        record["Job"]!["Files"] = new JsonArray();
        await File.WriteAllTextAsync(recordPath, record.ToJsonString());
        var relocated = Path.Combine(_root, "relocated");
        Directory.CreateDirectory(relocated);
        Directory.Move(Output, Path.Combine(relocated, "output"));
        Directory.Move(Store, Path.Combine(relocated, "store"));
        var restarted = Create(transport, Path.Combine(relocated, "store"), relocated);
        var recovered = await restarted.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(1, transport.Calls, "Published verified bytes should not be downloaded again.");
        Assert.AreEqual(Path.Combine(relocated, "output", "payload.bin"), recovered.Files.Single());
    }

    [TestMethod, Timeout(15000)]
    public async Task TamperedStoredFileNameCannotEscapeOutputDirectory()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(NewRequest());
        await transport.Next();
        await manager.StopAsync(job.Id);
        await manager.ShutdownAsync();
        var recordPath = Path.Combine(Store, job.Id + ".json");
        var record = JsonNode.Parse(await File.ReadAllTextAsync(recordPath))!;
        record["FileName"] = "../escape";
        await File.WriteAllTextAsync(recordPath, record.ToJsonString());
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Create(transport).InitializeAsync());
    }

    [TestMethod, Timeout(15000)]
    public async Task RecheckReusesVerifiedOutputAndRestoresMissingOutputWithoutOverwritingReplacement()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(NewRequest() with { FileName = "owned.bin" });
        (await transport.Next()).Finish();
        var completed = await manager.WaitForCompletionAsync(job.Id);
        await manager.RecheckAsync(job.Id);
        await manager.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(1, transport.Calls);
        File.Delete(completed.Files.Single());
        await manager.RecheckAsync(job.Id);
        (await transport.Next()).Finish();
        await manager.WaitForCompletionAsync(job.Id);
        Assert.AreEqual(2, transport.Calls);
        await File.WriteAllBytesAsync(completed.Files.Single(), [9, 9, 9]);
        await manager.RecheckAsync(job.Id);
        await Assert.ThrowsExceptionAsync<HttpDownloadJobException>(() => manager.WaitForCompletionAsync(job.Id));
        Assert.AreEqual(2, transport.Calls);
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(completed.Files.Single()));
    }

    [TestMethod, Timeout(15000)]
    public async Task CallerCancellationCanStopOwnedTransferWhileAnObserverCanCancelIndependently()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var job = await manager.EnsureQueuedAsync(NewRequest());
        await transport.Next();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => manager.WaitForCompletionAsync(job.Id, ct: cancelled.Token));
        Assert.AreEqual(HttpDownloadStatus.Running, (await manager.GetAsync(job.Id))!.Status);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => manager.WaitForCompletionAsync(job.Id, stopOnCancellation: true, ct: cancelled.Token));
        Assert.AreEqual(HttpDownloadStatus.Stopped, (await manager.GetAsync(job.Id))!.Status);
    }

    [TestMethod, Timeout(15000)]
    public async Task DistinctExplicitFilesInSameDirectoryCanTransferConcurrently()
    {
        var transport = new ControlledTransport();
        var manager = Create(transport);
        var first = await manager.EnsureQueuedAsync(NewRequest() with { FileName = "first.bin" });
        var firstTransfer = await transport.Next();
        var second = await manager.EnsureQueuedAsync(NewRequest() with { FileName = "second.bin" });
        var secondTransfer = await transport.Next();
        Assert.AreEqual(HttpDownloadStatus.Running, (await manager.GetAsync(first.Id))!.Status);
        Assert.AreEqual(HttpDownloadStatus.Running, (await manager.GetAsync(second.Id))!.Status);
        firstTransfer.Finish();
        secondTransfer.Finish();
        await manager.WaitForCompletionAsync(first.Id);
        await manager.WaitForCompletionAsync(second.Id);
        Assert.AreEqual(2, Directory.GetFiles(Output).Length);
    }

    private static async Task Until(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(20, deadline.Token);
    }

    private sealed class ControlledTransport : IHttpDownloader
    {
        private readonly Channel<Transfer> _transfers = Channel.CreateUnbounded<Transfer>();
        public int Calls;
        public async Task<Transfer> Next() => await _transfers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        public Task<string> DownloadAsync(TransportRequest request, Func<int, string?, Task>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<string> DownloadWithProgressAsync(TransportRequest request, Func<HttpTransferProgress, Task>? progress, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var transfer = new Transfer(request, progress);
            await _transfers.Writer.WriteAsync(transfer, ct);
            var bytes = await transfer.Completion.Task.WaitAsync(ct);
            Directory.CreateDirectory(request.Directory);
            var file = Path.Combine(request.Directory, request.FileName ?? "payload.bin");
            await File.WriteAllBytesAsync(file, bytes, ct);
            return file;
        }
    }

    private sealed record Transfer(TransportRequest Request, Func<HttpTransferProgress, Task>? Progress)
    {
        public TaskCompletionSource<byte[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish(byte[]? bytes = null) => Completion.TrySetResult(bytes ?? [1, 2, 3, 4]);
    }
}
