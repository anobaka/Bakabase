using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Models.Input;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadCenterTests
{
    private string _root = null!;
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private DownloadCenterService _center = null!;
    private IBtDownloadManager _bt = null!;
    private IHttpDownloadManager _http = null!;
    private WaitingHttp _httpTransport = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "download-center-" + Guid.NewGuid().ToString("N"));
        _httpTransport = new WaitingHttp();
        _services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "db"), services =>
        {
            services.AddSingleton<ITorrentDownloader>(new WaitingTorrent());
            services.AddSingleton<IHttpDownloader>(_httpTransport);
            services.AddTransient(provider => new BakabaseLocalizer(provider.GetRequiredService<IStringLocalizer<SharedResource>>()));
        });
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _center = _services.GetRequiredService<DownloadCenterService>();
        _bt = _services.GetRequiredService<IBtDownloadManager>();
        _http = _services.GetRequiredService<IHttpDownloadManager>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_bt != null) await _bt.ShutdownAsync();
        if (_http != null) await _http.ShutdownAsync();
        if (_services is IAsyncDisposable disposable) await disposable.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task ReconciliationPreservesMoreThanFiveHundredSourcesAndStableIdsWithoutStartingThem()
    {
        var sources = Enumerable.Range(1, 698).Select(i => NewSource("source-" + i, DownloadTaskDbModelStatus.Disabled)).ToArray();
        _db.DownloadTasks.AddRange(sources);
        await _db.SaveChangesAsync();
        var first = await _center.ListAsync();
        var second = await _center.ListAsync();
        Assert.AreEqual(698, first.Count);
        CollectionAssert.AreEquivalent(first.Select(t => t.Id).ToArray(), second.Select(t => t.Id).ToArray());
        Assert.IsTrue(first.All(t => t.Category == "site" && t.Provider == "site" && t.Status == "stopped"));
        Assert.AreEqual(698, await _db.ManagedDownloadTasks.CountAsync());
        Assert.AreEqual(698, await _db.DownloadTasks.CountAsync(t => t.Status == DownloadTaskDbModelStatus.Disabled));
        Assert.AreEqual(0, (await _bt.ListAsync()).Count);
        Assert.AreEqual(0, (await _http.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ManualBtAndHttpShareCommandsAndDoNotExposePrivateSourcePayloads()
    {
        var torrent = await _center.CreateAsync(new DownloadCenterCreateInputModel
        { Provider = "bt", TorrentUrl = "https://example.test/private.torrent?secret=private-bt", Name = "BT" }, Path.Combine(_root, "bt-output"), CancellationToken.None);
        var http = await _center.CreateAsync(new DownloadCenterCreateInputModel
        { Provider = "http", Url = "https://example.test/private.zip?secret=private-http", Name = "HTTP" }, Path.Combine(_root, "http-output"), CancellationToken.None);
        Assert.IsTrue(torrent.Id.StartsWith("bt:", StringComparison.Ordinal));
        Assert.IsTrue(http.Id.StartsWith("http:", StringComparison.Ordinal));
        Assert.AreEqual("general", torrent.Category);
        Assert.AreEqual("general", http.Category);
        Assert.IsNull(torrent.ParentId);
        Assert.IsNull(http.ParentId);
        var snapshot = JsonSerializer.Serialize(await _center.ListAsync());
        Assert.IsFalse(snapshot.Contains("private-bt", StringComparison.Ordinal));
        Assert.IsFalse(snapshot.Contains("private-http", StringComparison.Ordinal));
        foreach (var task in new[] { torrent, http })
        {
            var stopped = await _center.ExecuteAsync(task.Id, "stop");
            Assert.AreEqual("stopped", stopped!.Status);
            Assert.IsTrue(await _db.ManagedDownloadTasks.AsNoTracking().AnyAsync(t => t.Id == task.Id && t.UserStopped));
            var resumed = await _center.ExecuteAsync(task.Id, "start");
            Assert.IsTrue(resumed!.Status is "queued" or "running");
            Assert.IsFalse(await _db.ManagedDownloadTasks.AsNoTracking().AnyAsync(t => t.Id == task.Id && t.UserStopped));
        }
    }

    [TestMethod]
    public async Task SourceCompletionDoesNotHideItsChildTransferOrClaimContentCompletion()
    {
        var result = await NewResult();
        var job = await Queue(result);
        var tasks = await _center.ListAsync();
        var parent = tasks.Single(t => t.Id == $"site:{result.DownloadTaskId}");
        var child = tasks.Single(t => t.Id == $"bt:{job.Id}");
        Assert.AreEqual(parent.Id, child.ParentId);
        Assert.AreEqual(result.Id, child.DownloadResultId);
        Assert.AreEqual("download-result", child.OriginKind);
        Assert.IsTrue(parent.Status is "queued" or "running");
        Assert.IsNotNull(parent.Execution);
        Assert.AreEqual(1, parent.Execution.TotalCount);
        Assert.AreEqual(0, parent.Execution.CompletedCount);
        Assert.IsNull(parent.CompletedAt);
        Assert.AreEqual(parent.Id, (await _db.ManagedDownloadTasks.AsNoTracking().SingleAsync(t => t.Id == child.Id)).ParentId);
    }

    [TestMethod]
    public async Task ExplicitChildStopSurvivesParentContinuationAndReconciliationUntilExplicitChildStart()
    {
        var result = await NewResult();
        var job = await Queue(result);
        var id = $"bt:{job.Id}";
        await _center.ExecuteAsync(id, "stop");
        await _center.ReconcileAsync();
        await ActivatorUtilities.CreateInstance<DownloadTaskExecutionLifecycle>(_services).OnStartedAsync([result.DownloadTaskId]);
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _bt.GetAsync(job.Id))!.Status);
        Assert.IsTrue(await _services.GetRequiredService<IDownloadTaskRegistry>().IsUserStoppedAsync(id));
        await _center.ExecuteAsync(id, "start");
        Assert.IsTrue((await _bt.GetAsync(job.Id))!.Status is BtDownloadStatus.Queued or BtDownloadStatus.Running);
        Assert.IsFalse(await _services.GetRequiredService<IDownloadTaskRegistry>().IsUserStoppedAsync(id));
    }

    [TestMethod]
    public async Task StoppedSourceAndSupersededResultsCannotBeResumedThroughTransferCommands()
    {
        var result = await NewResult();
        var job = await Queue(result);
        await _bt.StopAsync(job.Id);
        await _db.DownloadTasks.Where(t => t.Id == result.DownloadTaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Disabled));
        var stopped = await _center.GetAsync($"bt:{job.Id}");
        Assert.IsFalse(stopped!.AvailableActions.Contains("start"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _center.ExecuteAsync(stopped.Id, "start"));
        await _db.DownloadTasks.Where(t => t.Id == result.DownloadTaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
        _db.DownloadResults.Add(new DownloadResultDbModel
        {
            DownloadTaskId = result.DownloadTaskId, SourceKey = result.SourceKey, ThirdPartyId = result.ThirdPartyId,
            Name = "newer", Kind = result.Kind, RequiresContentsDownload = true, DownloadDirectory = _root,
            Path = result.Path, Fingerprint = Guid.NewGuid().ToString("N"), DeduplicationKey = Guid.NewGuid().ToString("N")
        });
        await _db.SaveChangesAsync();
        Assert.IsFalse((await _center.GetAsync(stopped.Id))!.AvailableActions.Contains("start"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _center.ExecuteAsync(stopped.Id, "start"));
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _bt.GetAsync(job.Id))!.Status);
    }

    [TestMethod]
    public async Task WorkflowOwnedTransfersStopTheirRunAndCannotBypassWorkflowContinuation()
    {
        var definition = new WorkflowDefinitionDbModel { Name = "Owner", TriggerKind = "manual" };
        _db.Set<WorkflowDefinitionDbModel>().Add(definition);
        await _db.SaveChangesAsync();
        var run = new WorkflowRunDbModel { WorkflowDefinitionId = definition.Id, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow };
        _db.Set<WorkflowRunDbModel>().Add(run);
        await _db.SaveChangesAsync();
        var job = await _http.EnsureQueuedAsync(new Bakabase.Modules.HttpDownloads.Models.HttpDownloadRequest
        {
            Url = "https://example.test/owned.zip", OutputDirectory = Path.Combine(_root, "owned-http"),
            SourceKind = "workflow", SourceId = run.Id.ToString(), WorkflowRunId = run.Id, WorkflowDefinitionId = definition.Id
        });
        var id = $"http:{job.Id}";
        await _center.ExecuteAsync(id, "stop");
        Assert.AreEqual(WorkflowRunStatus.Cancelled, await _db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => r.Id == run.Id).Select(r => r.Status).SingleAsync());
        var stopped = await _center.GetAsync(id);
        Assert.AreEqual("stopped", stopped!.Status);
        Assert.IsFalse(stopped.AvailableActions.Contains("start"));
        Assert.IsFalse(stopped.AvailableActions.Contains("delete"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _center.ExecuteAsync(id, "start"));
        Assert.AreEqual(HttpDownloadStatus.Stopped, (await _http.GetAsync(job.Id))!.Status);
    }

    [TestMethod]
    public async Task BatchReportsUnknownTaskFailureWithoutHidingSuccessfulStopsOrDuplicatingIds()
    {
        var job = await _center.CreateAsync(new DownloadCenterCreateInputModel
        { Provider = "http", Url = "https://example.test/manual.bin" }, Path.Combine(_root, "batch-http"), CancellationToken.None);
        var results = await _center.BatchAsync(new DownloadCenterBatchInputModel
        { Ids = [job.Id, "http:missing", job.Id], Action = "stop" }, CancellationToken.None);
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.Single(r => r.Id == job.Id).Success);
        Assert.AreEqual("stopped", results.Single(r => r.Id == job.Id).Task!.Status);
        Assert.IsFalse(results.Single(r => r.Id == "http:missing").Success);
        Assert.AreNotEqual(0, results.Single(r => r.Id == "http:missing").Code);
        Assert.IsNotNull(results.Single(r => r.Id == "http:missing").Error);
    }

    [TestMethod]
    public async Task SelectingParentAndChildForStopDoesNotPermanentlyHoldChild()
    {
        var result = await NewResult();
        var job = await Queue(result);
        var id = $"bt:{job.Id}";
        var actions = await _center.BatchAsync(new DownloadCenterBatchInputModel
        { Ids = [$"site:{result.DownloadTaskId}", id], Action = "stop" }, CancellationToken.None);
        Assert.IsTrue(actions.All(a => a.Success));
        Assert.AreEqual(BtDownloadStatus.Stopped, (await _bt.GetAsync(job.Id))!.Status);
        Assert.IsFalse(await _services.GetRequiredService<IDownloadTaskRegistry>().IsUserStoppedAsync(id));
    }

    [TestMethod]
    public async Task CompletedTransferExposesItsVerifiedFileListInTheCommonTask()
    {
        var output = Path.Combine(_root, "completed-http");
        var task = await _center.CreateAsync(new DownloadCenterCreateInputModel
        { Provider = "http", Url = "https://example.test/file.bin", FileName = "file.bin" }, output, CancellationToken.None);
        var file = Path.Combine(output, "file.bin");
        _httpTransport.Complete();
        await _http.WaitForCompletionAsync(task.ProviderTaskId).WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await _center.GetAsync(task.Id);
        Assert.AreEqual("completed", completed!.Status);
        CollectionAssert.AreEqual(new[] { file }, completed.Files.ToArray());
    }

    [TestMethod]
    public async Task DeletingManualTaskRemovesIdentityAndPreservesDownloadedFiles()
    {
        var output = Path.Combine(_root, "delete-http");
        var job = await _center.CreateAsync(new DownloadCenterCreateInputModel
        { Provider = "http", Url = "https://example.test/manual.bin" }, output, CancellationToken.None);
        await _center.ExecuteAsync(job.Id, "stop");
        Directory.CreateDirectory(output);
        var retained = Path.Combine(output, "existing.txt");
        await File.WriteAllTextAsync(retained, "keep");
        Assert.IsNull(await _center.ExecuteAsync(job.Id, "delete"));
        Assert.IsNull(await _center.GetAsync(job.Id));
        Assert.IsFalse(await _db.ManagedDownloadTasks.AnyAsync(t => t.Id == job.Id));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(retained));
    }

    [TestMethod]
    public async Task RemovingSourceDoesNotMakeItsExistingTransferDisappearOrLoseProvenance()
    {
        var result = await NewResult();
        var job = await Queue(result);
        await _center.ReconcileAsync();
        await _db.DownloadTasks.Where(t => t.Id == result.DownloadTaskId).ExecuteDeleteAsync();
        var orphan = await _center.GetAsync($"bt:{job.Id}");
        Assert.IsNotNull(orphan);
        Assert.IsNull(orphan.ParentId);
        Assert.AreEqual(result.DownloadTaskId, orphan.SiteTaskId);
        Assert.AreEqual(result.Id, orphan.DownloadResultId);
        Assert.AreEqual("download-result", orphan.OriginKind);
        Assert.IsNull((await _db.ManagedDownloadTasks.AsNoTracking().SingleAsync(t => t.Id == orphan.Id)).ParentId);
    }

    private DownloadTaskDbModel NewSource(string name, DownloadTaskDbModelStatus status = DownloadTaskDbModelStatus.Complete) => new()
    {
        Key = name, Name = name, Type = 1, ThirdPartyId = ThirdPartyId.ExHentai, DownloadPath = _root,
        Status = status, Options = "{\"downloadMode\":2}", CompletedAt = DateTime.UtcNow
    };

    private async Task<DownloadResultDbModel> NewResult()
    {
        var source = NewSource("source");
        _db.DownloadTasks.Add(source);
        await _db.SaveChangesAsync();
        var result = new DownloadResultDbModel
        {
            DownloadTaskId = source.Id, ThirdPartyId = source.ThirdPartyId, SourceKey = "gallery",
            Name = "Gallery", Kind = DownloadResultKind.TorrentMetadata, RequiresContentsDownload = true,
            Path = Path.Combine(_root, "metadata.torrent"), DownloadDirectory = _root,
            Fingerprint = Guid.NewGuid().ToString("N"), DeduplicationKey = Guid.NewGuid().ToString("N")
        };
        _db.DownloadResults.Add(result);
        await _db.SaveChangesAsync();
        return result;
    }

    private Task<BtDownloadJob> Queue(DownloadResultDbModel result) => _bt.EnsureQueuedAsync(new BtDownloadRequest
    {
        Metadata = [1], OutputDirectory = Path.Combine(_root, "bt", result.Id.ToString()), Name = result.Name,
        DownloadTaskId = result.DownloadTaskId, DownloadResultId = result.Id, SourceKind = "download-result", SourceId = result.Id.ToString()
    });

    private sealed class WaitingTorrent : ITorrentDownloader
    {
        private static async Task<TorrentDownloadResult> Wait(CancellationToken ct)
        { await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); }
        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => Wait(ct);
        public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => Wait(ct);
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => Wait(ct);
    }

    private sealed class WaitingHttp : IHttpDownloader
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _completion.TrySetResult();
        public async Task<string> DownloadAsync(Bakabase.Modules.Downloader.Models.HttpDownloadRequest request,
            Func<int, string?, Task>? progress, CancellationToken cancellationToken)
        {
            await _completion.Task.WaitAsync(cancellationToken);
            Directory.CreateDirectory(request.Directory);
            var file = Path.Combine(request.Directory, request.FileName ?? "file.bin");
            await File.WriteAllTextAsync(file, "verified contents", cancellationToken);
            return file;
        }
    }
}
