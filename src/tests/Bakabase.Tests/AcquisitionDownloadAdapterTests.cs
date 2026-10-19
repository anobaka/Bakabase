using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.Acquisition.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionDownloadAdapterTests
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789012345678901234567890123456789";
    private readonly Downloads _downloads = new();
    private readonly MetadataStore _store = new();
    private readonly BtManager _bt = new();
    private readonly List<int> _progress = [];
    private ServiceProvider _services = null!;
    private string _working = null!;

    [TestInitialize]
    public void Setup()
    {
        _working = Path.Combine(Path.GetTempPath(), "BakabaseDownloadAdapter_" + Guid.NewGuid().ToString("N"));
        _services = new ServiceCollection()
            .AddSingleton<IHttpDownloader>(_downloads)
            .AddSingleton<IBtDownloadManager>(_bt)
            .AddSingleton<IAria2Downloader>(_downloads)
            .AddSingleton<IAcquisitionTorrentMetadataStore>(_store)
            .BuildServiceProvider();
        _downloads.Result = new TorrentDownloadResult(Path.Combine(_working, "torrent-data"),
            [Path.Combine(_working, "torrent-data", "nested", "payload.bin")]);
        _bt.Result = _downloads.Result;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        if (Directory.Exists(_working)) Directory.Delete(_working, true);
    }

    private AcquisitionStepContext Context(string? config = null) => new(_services, NullLogger.Instance,
        (percent, _) => { _progress.Add(percent); return Task.CompletedTask; }, _working, config,
        AcquisitionTaskId: 7, WorkflowRunId: 19, WorkflowDefinitionId: 23);

    private AcquisitionWorkItem Item(AcquisitionDriveKind kind = AcquisitionDriveKind.DirectUrl) => new()
    {
        ResourceId = 9,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.invalid/sharing-page",
        Links = [new("https://example.invalid/ignored", DriveKind: kind),
            new(kind == AcquisitionDriveKind.Magnet ? Magnet : "https://example.invalid/chosen", DriveKind: kind)],
        SelectedLinkIndex = 1,
        WorkingDirectory = _working,
        Files = ["already-fetched.bin"]
    };

    [TestMethod]
    public async Task Http_DelegatesTheSelectedUrlAndTimeout_WithoutCreatingItsOwnClientOrDirectory()
    {
        var item = Item() with {Files = ["already-fetched.bin", _downloads.HttpResult]};
        using var cancellation = new CancellationTokenSource();
        var outcome = await new FetchHttpStep().ExecuteAsync(Context(
            """{"timeoutMinutes":7,"parallelConnections":8,"maxRetries":5,"speedLimitKiB":1048576}"""),
            item, cancellation.Token);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual("https://example.invalid/chosen", _downloads.HttpRequest!.Url);
        Assert.AreEqual(_working, _downloads.HttpRequest.Directory);
        Assert.AreEqual(TimeSpan.FromMinutes(7), _downloads.HttpRequest.Timeout);
        Assert.AreEqual(8, _downloads.HttpRequest.ParallelConnections);
        Assert.AreEqual(5, _downloads.HttpRequest.MaxRetries);
        Assert.AreEqual(1073741824L, _downloads.HttpRequest.MaximumBytesPerSecond);
        Assert.IsNull(_downloads.HttpRequest.HttpClientName, "acquisition must use the neutral module's default client");
        Assert.IsNull(_downloads.HttpRequest.FileName);
        Assert.AreEqual(cancellation.Token, _downloads.Token);
        CollectionAssert.AreEqual(new[] {25}, _progress);
        CollectionAssert.AreEqual(item.Files.ToArray(), ((AcquisitionStepOutcome.Continue) outcome).Item.Files.ToArray());
        Assert.IsFalse(Directory.Exists(_working), "the adapter must leave filesystem work to the downloader");
    }

    [TestMethod]
    public async Task UnsupportedAndMissingLinks_DoNotStartAnyDownloader()
    {
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(await new FetchHttpStep()
            .ExecuteAsync(Context(), Item(AcquisitionDriveKind.Baidu), CancellationToken.None));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(await new FetchMagnetStep()
            .ExecuteAsync(Context(), Item(), CancellationToken.None));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await new FetchHttpStep()
            .ExecuteAsync(Context(), Item() with {Links = []}, CancellationToken.None));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await new FetchTorrentStep()
            .ExecuteAsync(Context(), Item() with {Links = [], LeadKind = AcquisitionLeadKind.Torrent, LeadValue = "/private/data.torrent"}, CancellationToken.None));
        Assert.IsNull(_downloads.Method);
        Assert.IsNull(_bt.Request);
        Assert.IsFalse(Directory.Exists(_working));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Magnet_MapsExistingHandlerConfigurationAndPreservesTheDownloadedTree(bool aria2)
    {
        var config = aria2
            ? """{"handler":1,"rpcUrl":"https://aria2.example.invalid/jsonrpc","secret":"saved-token","timeoutMinutes":7,"pollSeconds":11}"""
            : """{"handler":0,"timeoutMinutes":7}""";
        var item = Item(AcquisitionDriveKind.Magnet) with {Files = ["existing", _downloads.Result.Files[0]]};
        var outcome = await new FetchMagnetStep().ExecuteAsync(Context(config), item, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        if (aria2)
        {
            Assert.IsNull(_bt.Request);
            Assert.AreEqual("aria2", _downloads.Method);
            Assert.AreEqual(Magnet, _downloads.Url);
            Assert.AreEqual(_working, _downloads.Directory);
            Assert.AreEqual(TimeSpan.FromMinutes(7), _downloads.Timeout);
            Assert.AreEqual("https://aria2.example.invalid/jsonrpc", _downloads.Aria2Options!.RpcUrl);
            Assert.AreEqual("saved-token", _downloads.Aria2Options.Secret);
            Assert.AreEqual(TimeSpan.FromSeconds(11), _downloads.Aria2Options.PollInterval);
        }
        else
        {
            Assert.IsNull(_downloads.Method, "Built-in BT steps must use the independent manager.");
            Assert.AreEqual(Magnet, _bt.Request!.MagnetUri);
            Assert.AreEqual(Path.Combine(_working, "torrent-data"), _bt.Request.OutputDirectory);
            Assert.AreEqual(TimeSpan.FromMinutes(7), _bt.Request.Timeout);
            Assert.IsTrue(_bt.StopOnCancellation);
        }
        var result = ((AcquisitionStepOutcome.Continue) outcome).Item;
        Assert.IsTrue(result.PreserveDirectoryStructure);
        Assert.AreEqual(_downloads.Result.Directory, result.ExtractedDirectory);
        CollectionAssert.AreEqual(item.Files.ToArray(), result.Files.ToArray());
        CollectionAssert.AreEqual(new[] {25}, _progress);
    }

    [TestMethod]
    public async Task TorrentUrl_FromASelectedSharingLink_IsQueuedWithTheIndependentManager()
    {
        var outcome = await new FetchTorrentStep().ExecuteAsync(Context("""{"timeoutMinutes":7}"""),
            Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.IsNull(_downloads.Method);
        Assert.AreEqual("https://example.invalid/chosen", _bt.Request!.TorrentUrl);
        Assert.AreEqual(Path.Combine(_working, "torrent-data"), _bt.Request.OutputDirectory);
        Assert.AreEqual(TimeSpan.FromMinutes(7), _bt.Request.Timeout);
        Assert.IsNull(_store.ReadReference);
        var result = ((AcquisitionStepOutcome.Continue) outcome).Item;
        Assert.IsTrue(result.PreserveDirectoryStructure);
        Assert.AreEqual(_downloads.Result.Directory, result.ExtractedDirectory);
        CollectionAssert.AreEqual(new[] {"already-fetched.bin"}.Concat(_downloads.Result.Files).ToArray(), result.Files.ToArray());
    }

    [TestMethod]
    public async Task UploadedTorrent_ResolvesOnlyTheManagedReference_ThenDelegatesItsBytes()
    {
        var reference = AcquisitionTorrentMetadataStore.ReferencePrefix + new string('a', 64);
        var item = Item() with {Links = [], LeadKind = AcquisitionLeadKind.Torrent, LeadValue = reference};
        var outcome = await new FetchTorrentStep().ExecuteAsync(Context(), item, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(reference, _store.ReadReference);
        Assert.IsNull(_downloads.Method);
        CollectionAssert.AreEqual(_store.Bytes, _bt.Request!.Metadata);
        Assert.AreEqual(TimeSpan.FromHours(4), _bt.Request.Timeout);
    }

    [DataTestMethod]
    [DataRow("http")]
    [DataRow("magnet")]
    [DataRow("aria2")]
    [DataRow("torrent")]
    public async Task TimeoutIsAFailedStep_ButCallerCancellationStillPropagates(string kind)
    {
        var step = Step(kind);
        var item = Item(kind is "magnet" or "aria2" ? AcquisitionDriveKind.Magnet : AcquisitionDriveKind.DirectUrl);
        var context = Context(kind == "aria2"
            ? """{"handler":1,"timeoutMinutes":7}"""
            : """{"timeoutMinutes":7}""");
        _downloads.Failure = _bt.Failure = new TimeoutException("The download deadline elapsed.");
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await step.ExecuteAsync(context, item, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _downloads.Failure = _bt.Failure = new OperationCanceledException(cancellation.Token);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => step.ExecuteAsync(context, item, cancellation.Token));
    }

    [TestMethod]
    public async Task ManagedBt_ReentryUsesAStablePrivateIdentityAndRecordsWorkflowAndDownloadOwnership()
    {
        var item = Item(AcquisitionDriveKind.Magnet) with
        {
            Title = "A gallery", Variables = new Dictionary<string, string>
            {
                ["downloadResultId"] = "31", ["downloadTaskId"] = "32"
            }
        };
        var step = new FetchMagnetStep();
        await step.ExecuteAsync(Context(), item, CancellationToken.None);
        var first = _bt.Request!;
        await step.ExecuteAsync(Context(), item, CancellationToken.None);
        Assert.AreEqual(first.IdempotencyKey, _bt.Request!.IdempotencyKey);
        Assert.IsFalse(first.IdempotencyKey!.Contains(Magnet));
        Assert.AreEqual("workflow", first.SourceKind);
        Assert.AreEqual("19", first.SourceId);
        Assert.AreEqual(19, first.WorkflowRunId);
        Assert.AreEqual(23, first.WorkflowDefinitionId);
        Assert.AreEqual(31, first.DownloadResultId);
        Assert.AreEqual(32, first.DownloadTaskId);
        Assert.AreEqual("A gallery", first.Name);
        await step.ExecuteAsync(Context() with {WorkflowRunId = 20}, item, CancellationToken.None);
        Assert.AreNotEqual(first.IdempotencyKey, _bt.Request!.IdempotencyKey);
    }

    [TestMethod]
    public async Task WorkflowActivityPassesItsActualRunAndDefinitionToBtManagement()
    {
        await new AcquisitionStepActivity(new FetchMagnetStep()).ProcessItemAsync(new WorkflowExecutionContext
        {
            RunId = 41, WorkflowDefinitionId = 42, TriggerKind = "test", Payload = new object(),
            ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance
        }, Item(AcquisitionDriveKind.Magnet), CancellationToken.None);
        Assert.AreEqual(41, _bt.Request!.WorkflowRunId);
        Assert.AreEqual(42, _bt.Request.WorkflowDefinitionId);
    }

    [DataTestMethod]
    [DataRow(BtDownloadStatus.Stopped)]
    [DataRow(BtDownloadStatus.Failed)]
    [DataRow(BtDownloadStatus.Stopping)]
    public async Task WorkflowRetryContinuesTheExistingManagedBtJob(BtDownloadStatus status)
    {
        _bt.Existing = _bt.Job(status);
        var outcome = await new FetchMagnetStep().ExecuteAsync(Context(), Item(AcquisitionDriveKind.Magnet), CancellationToken.None);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(1, _bt.ResumeCount);
        Assert.AreEqual(0, _bt.RecheckCount);
        Assert.AreEqual("managed-bt", _bt.WaitedId);
        Assert.AreEqual(status == BtDownloadStatus.Stopping ? 1 : 0, _bt.StopCount);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ACompletedTransferIsRecheckedOnlyWhenItsFilesAreMissing(bool filePresent)
    {
        _bt.Existing = _bt.Job(BtDownloadStatus.Completed);
        if (filePresent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_bt.Result.Files[0])!);
            await File.WriteAllTextAsync(_bt.Result.Files[0], "verified payload");
        }
        var outcome = await new FetchTorrentStep().ExecuteAsync(Context(), Item(), CancellationToken.None);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(filePresent ? 0 : 1, _bt.RecheckCount);
        Assert.AreEqual(0, _bt.ResumeCount);
        Assert.AreEqual("managed-bt", _bt.WaitedId);
    }

    [TestMethod]
    public async Task CancellingAWorkflowStopsItsOwnedManagedTransfer()
    {
        using var cancellation = new CancellationTokenSource();
        _bt.Waiting = ct => { cancellation.Cancel(); return Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => new FetchMagnetStep()
            .ExecuteAsync(Context(), Item(AcquisitionDriveKind.Magnet), cancellation.Token));
        Assert.IsTrue(_bt.StopOnCancellation);
        Assert.AreEqual(1, _bt.StopCount);
    }

    [TestMethod]
    public async Task CancellationDuringResumeAlsoStopsTheJobBeforeTheWaitBegins()
    {
        using var cancellation = new CancellationTokenSource();
        _bt.Existing = _bt.Job(BtDownloadStatus.Stopped);
        _bt.Resuming = ct => { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new FetchMagnetStep()
            .ExecuteAsync(Context(), Item(AcquisitionDriveKind.Magnet), cancellation.Token));
        Assert.AreEqual(1, _bt.StopCount);
        Assert.IsNull(_bt.WaitedId);
    }

    [TestMethod]
    public async Task AFailedWorkflowProgressCallbackStopsTheOwnedTransfer()
    {
        var context = Context() with
        {
            ReportProgress = (_, _) => throw new InvalidOperationException("The workflow is no longer available.")
        };
        var outcome = await new FetchMagnetStep().ExecuteAsync(context,
            Item(AcquisitionDriveKind.Magnet), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
        Assert.AreEqual("managed-bt", _bt.WaitedId);
        Assert.AreEqual(1, _bt.StopCount, "An abandoned caller must not leave its transfer running.");
    }

    private static IAcquisitionStep Step(string kind) => kind switch
    {
        "http" => new FetchHttpStep(),
        "magnet" or "aria2" => new FetchMagnetStep(),
        _ => new FetchTorrentStep()
    };

    private sealed class MetadataStore : IAcquisitionTorrentMetadataStore
    {
        public readonly byte[] Bytes = [1, 2, 3];
        public string? ReadReference;
        public Task<string> SaveAsync(byte[] metadata, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string reference, CancellationToken ct = default)
        {
            ReadReference = reference;
            return Task.FromResult(Bytes);
        }
    }

    private sealed class Downloads : IHttpDownloader, IAria2Downloader
    {
        public string? Method, Url, Directory;
        public TimeSpan Timeout;
        public CancellationToken Token;
        public HttpDownloadRequest? HttpRequest;
        public Aria2DownloadOptions? Aria2Options;
        public Exception? Failure;
        public string HttpResult = "downloaded.bin";
        public TorrentDownloadResult Result = null!;

        public async Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            HttpRequest = request;
            Method = "http";
            await Report(progress, ct);
            return HttpResult;
        }

        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnet, string workingDirectory,
            Aria2DownloadOptions options, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Aria2Options = options;
            return Download("aria2", magnet, workingDirectory, options.Timeout, progress, ct);
        }

        private async Task<TorrentDownloadResult> Download(string method, string? url, string directory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Method = method;
            Url = url;
            Directory = directory;
            Timeout = timeout;
            await Report(progress, ct);
            return Result;
        }

        private async Task Report(Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Token = ct;
            if (Failure != null) throw Failure;
            if (progress != null) await progress(25, "Downloading");
        }
    }

    private sealed class BtManager : IBtDownloadManager
    {
        public BtDownloadRequest? Request;
        public BtDownloadJob? Existing;
        public TorrentDownloadResult Result = null!;
        public Exception? Failure;
        public Func<CancellationToken, Task>? Waiting, Resuming;
        public int ResumeCount, RecheckCount, StopCount;
        public string? WaitedId;
        public bool StopOnCancellation;

        public BtDownloadJob Job(BtDownloadStatus status) => new()
        {
            Id = "managed-bt", Name = "BT download", OutputDirectory = Result.Directory, Files = Result.Files,
            Status = status, Stage = status.ToString(), Percentage = status == BtDownloadStatus.Completed ? 100 : 25
        };
        public Task<BtDownloadJob> EnsureQueuedAsync(BtDownloadRequest request, CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult(Existing ?? Job(BtDownloadStatus.Queued));
        }
        public async Task<BtDownloadJob> ResumeAsync(string id, CancellationToken ct = default)
        {
            ResumeCount++;
            if (Resuming != null) await Resuming(ct);
            return Job(BtDownloadStatus.Queued);
        }
        public Task<BtDownloadJob> RecheckAsync(string id, CancellationToken ct = default)
        {
            RecheckCount++;
            return Task.FromResult(Job(BtDownloadStatus.Queued));
        }
        public Task<BtDownloadJob> StopAsync(string id, CancellationToken ct = default)
        {
            StopCount++;
            return Task.FromResult(Job(BtDownloadStatus.Stopped));
        }
        public async Task<BtDownloadJob> WaitForCompletionAsync(string id, Func<BtDownloadJob, Task>? progress = null,
            bool stopOnCancellation = false, CancellationToken ct = default)
        {
            WaitedId = id;
            StopOnCancellation = stopOnCancellation;
            try
            {
                if (Failure != null) throw Failure;
                if (Waiting != null) await Waiting(ct);
                if (progress != null) await progress(Job(BtDownloadStatus.Running));
                return Job(BtDownloadStatus.Completed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (stopOnCancellation) await StopAsync(id, CancellationToken.None);
                throw;
            }
        }
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<BtDownloadJob>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BtDownloadJob?> GetAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BtDownloadJob?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
