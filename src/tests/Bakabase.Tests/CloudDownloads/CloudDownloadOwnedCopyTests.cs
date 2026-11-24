using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Downloader;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudDownloadOwnedCopyTests
{
    private readonly OwnedProvider _provider = new();
    private string _root = null!;
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private CloudDownloadOwnedCopyService _copies = null!;
    private DownloadTaskDbModel _task = null!;
    private CloudDownloadRequest _request = null!;
    private string _file = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Directory.CreateTempSubdirectory("owned-cloud-copy-").FullName;
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<ICloudDownloadProvider>();
            services.AddSingleton<ICloudDownloadProvider>(_provider);
            services.AddTransient(p => new BakabaseLocalizer(p.GetRequiredService<IStringLocalizer<SharedResource>>()));
        });
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _copies = _services.GetRequiredService<CloudDownloadOwnedCopyService>();
        _request = new("operation", new() {Id = "share", Kind = "share", ProviderId = "pikpak", MethodId = "pikpak.share",
            Url = "https://mypikpak.com/s/example", AccountId = "account", AccountVersion = "version"}, "account", null, _root);
        _task = new() {Key = "source", ThirdPartyId = ThirdPartyId.PikPak, Type = 1, DownloadPath = _root,
            Status = DownloadTaskDbModelStatus.InProgress, Options = JsonSerializer.Serialize(_request, JsonSerializerOptions.Web)};
        _db.DownloadTasks.Add(_task); await _db.SaveChangesAsync();
        _file = Path.Combine(_root, "media.txt");
        await File.WriteAllTextAsync(_file, "fixture");
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    [DataTestMethod]
    [DataRow("trashed")]
    [DataRow("released")]
    [DataRow("retained")]
    public async Task CompletedNativeReceiptHandsOffTheExactVerifiedFileSetAndPersistsOutcome(string finalState)
    {
        await Prepare();
        var result = await Record();
        Assert.IsFalse(await _copies.ProcessNextAsync(default));
        Assert.AreEqual(0, _provider.ReleaseCalls);
        await Complete();
        _provider.Result = new(finalState);
        // The worker gets a new scope and does not require a running acquisition workflow.
        using var worker = _services.CreateScope();
        var copies = worker.ServiceProvider.GetRequiredService<CloudDownloadOwnedCopyService>();
        Assert.IsTrue(await copies.ProcessNextAsync(default));
        Assert.IsFalse(await copies.ProcessNextAsync(default));
        var view = await copies.GetAsync(_task.Id, default);
        Assert.AreEqual(finalState, view.Status);
        Assert.IsFalse(view.CanRetry);
        var local = _provider.Verified!;
        Assert.AreEqual(_task.Id, local.DownloadTaskId);
        Assert.AreEqual(result.Id, local.DownloadResultId);
        Assert.AreEqual(result.Fingerprint, local.Fingerprint);
        Assert.AreEqual(_root, local.Directory);
        Assert.AreEqual(_file, local.VerifiedFiles.Single().Path);
        Assert.AreEqual(File.GetLastWriteTimeUtc(_file), local.VerifiedFiles.Single().LastWriteTimeUtc);
        Assert.AreEqual(Bakabase.InsideWorld.Business.Components.Artifacts.ArtifactFileIdentity.ReadContentGeneration(_file),
            local.VerifiedFiles.Single().ContentGeneration);
        Assert.AreEqual(new FileInfo(_file).Length, local.VerifiedFiles.Single().Length);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(_file))),
            local.VerifiedFiles.Single().Sha256, true);
        var center = worker.ServiceProvider.GetRequiredService<DownloadCenterService>();
        Assert.AreEqual(finalState, (await center.GetAsync("site:" + _task.Id))!.OwnedCopy!.Status);
        Assert.AreEqual(1, _provider.ReleaseCalls);
        Assert.IsTrue(File.Exists(_file));
    }

    [TestMethod]
    public async Task NativeSiteDownloaderEnrolsTheCopyAndRecordsItsOwnSuccessfulResult()
    {
        _provider.DownloadedFile = _file;
        using var site = new PikPakCloudDownloader(_services);
        var finished = new TaskCompletionSource<DownloaderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        site.OnStatusChanged += () =>
        {
            if (site.Status is DownloaderStatus.Complete or DownloaderStatus.Failed or DownloaderStatus.Stopped)
                finished.TrySetResult(site.Status);
            return Task.CompletedTask;
        };
        Assert.IsTrue(await site.Start(new DownloadTask {Id = _task.Id, Key = _task.Key, ThirdPartyId = _task.ThirdPartyId,
            Type = 1, Options = _task.Options, DownloadPath = _task.DownloadPath}));
        Assert.AreEqual(DownloaderStatus.Complete, await finished.Task.WaitAsync(TimeSpan.FromSeconds(15)), site.Message);
        Assert.AreEqual(1, _provider.PrepareCalls);
        Assert.AreEqual("pending", (await _copies.GetAsync(_task.Id, default)).Status);
        Assert.AreEqual(1, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(0, _provider.ReleaseCalls);
        await Complete(); await _copies.ProcessNextAsync(default);
        Assert.AreEqual("trashed", (await _copies.GetAsync(_task.Id, default)).Status);
    }

    [TestMethod]
    public async Task OptOutAndRemoteInputsNeverBecomeCleanupCandidates()
    {
        _provider.OptIn = false;
        await Prepare();
        Assert.AreEqual("notApplicable", (await _copies.GetAsync(_task.Id, default)).Status);
        _provider.OptIn = true;
        await _copies.PrepareAsync(_task.Id, _request with {Source = _request.Source with {Kind = "remote"}}, _provider, default);
        await _copies.PrepareAsync(_task.Id, _request with {RemoteEntry = new("pikpak", "account", "/borrowed")}, _provider, default);
        Assert.AreEqual(0, _provider.PrepareCalls);
        await Record(); await Complete();
        Assert.IsFalse(await _copies.ProcessNextAsync(default));
        Assert.AreEqual(0, _provider.ReleaseCalls);
    }

    [DataTestMethod]
    [DataRow("content", "CLOUD_OWNED_COPY_LOCAL_CHANGED")]
    [DataRow("source", "CLOUD_OWNED_COPY_DOWNLOAD_CHANGED")]
    [DataRow("receipt", "CLOUD_OWNED_COPY_RECEIPT_CHANGED")]
    public async Task ChangedFilesSourceOrReceiptRequireReviewAndLeaveDownloadComplete(string changed, string reason)
    {
        await Prepare(); var result = await Record(); await Complete();
        switch (changed)
        {
            case "content": await File.WriteAllTextAsync(_file, "altered"); break;
            case "source":
                var edited = _request with {Source = _request.Source with {Url = "https://mypikpak.com/s/changed"}};
                await _db.DownloadTasks.Where(t => t.Id == _task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Options,
                    JsonSerializer.Serialize(edited, JsonSerializerOptions.Web))); break;
            case "receipt":
                await _db.DownloadResults.Where(r => r.Id == result.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Fingerprint, "changed")); break;
        }
        Assert.IsTrue(await _copies.ProcessNextAsync(default));
        var view = await _copies.GetAsync(_task.Id, default);
        Assert.AreEqual("reviewRequired", view.Status);
        Assert.AreEqual(reason, view.ReasonCode);
        Assert.IsFalse(view.CanRetry);
        Assert.AreEqual(0, _provider.ReleaseCalls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    public async Task FailedReleaseCanBeIndependentlyRetriedWithoutSubmittingAnotherDownload()
    {
        await Prepare(); await Record(); await Complete();
        _provider.ThrowOnRelease = true;
        await _copies.ProcessNextAsync(default);
        var failed = await _copies.GetAsync(_task.Id, default);
        Assert.IsTrue(failed.CanRetry);
        Assert.AreEqual("CLOUD_OWNED_COPY_RELEASE_FAILED", failed.ReasonCode);
        Assert.IsFalse(await _copies.ProcessNextAsync(default));
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
        _provider.ThrowOnRelease = false;
        Assert.AreEqual("pending", (await _copies.RetryAsync(_task.Id, default)).Status);
        Assert.AreEqual("pending", (await _copies.RetryAsync(_task.Id, default)).Status);
        await _copies.ProcessNextAsync(default);
        Assert.AreEqual("trashed", (await _copies.GetAsync(_task.Id, default)).Status);
        Assert.AreEqual(1, _provider.PrepareCalls);
        Assert.AreEqual(2, _provider.ReleaseCalls);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InterruptedReceiptHandoffRecoversOnlyAnUnambiguousNewReceipt(bool newReceipt)
    {
        if (!newReceipt) await Record(bind: false);
        await Prepare();
        if (newReceipt) await Record(bind: false);
        await Complete();
        Assert.IsTrue(await _copies.ProcessNextAsync(default));
        Assert.AreEqual(newReceipt ? "trashed" : "reviewRequired", (await _copies.GetAsync(_task.Id, default)).Status);
        Assert.AreEqual(newReceipt ? 1 : 0, _provider.ReleaseCalls);
    }

    [TestMethod]
    public async Task DeletingTheNativeTaskDoesNotTransferTheSameRemoteOperationToAnotherOwner()
    {
        await Prepare();
        await _db.DownloadTasks.Where(t => t.Id == _task.Id).ExecuteDeleteAsync();
        await _copies.RetireDeletedOwnersAsync(default);
        Assert.IsNull((await _db.CloudDownloadOwnedCopies.AsNoTracking().SingleAsync()).CopyJson);
        Assert.AreEqual("notApplicable", (await _copies.GetAsync(_task.Id, default)).Status);
        var rejected = await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            _copies.PrepareAsync(_task.Id + 1, _request, _provider, default));
        Assert.AreEqual("CLOUD_OWNED_COPY_IN_USE", rejected.Message);
        Assert.AreEqual(1, _provider.PrepareCalls);
        Assert.IsFalse(await _copies.ProcessNextAsync(default));
    }

    private Task Prepare() => _copies.PrepareAsync(_task.Id, _request, _provider, default);
    private Task Complete() => _db.DownloadTasks.Where(t => t.Id == _task.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
    private async Task<DownloadResultDbModel> Record(bool bind = true)
    {
        var result = await _services.GetRequiredService<DownloadResultService>().RecordFilesAsync(_task.Id,
            _task.ThirdPartyId, _task.Key, "fixture", _root, [_file], null);
        if (bind) await _copies.DownloadVerifiedAsync(_task.Id, _request, result);
        return result;
    }

    private sealed class OwnedProvider : ICloudDownloadProvider, ICloudOwnedCopyProvider
    {
        public string ProviderId => "pikpak";
        public string MethodId => "pikpak.share";
        internal bool OptIn = true, ThrowOnRelease;
        internal int PrepareCalls, ReleaseCalls;
        internal CloudVerifiedLocalDownload? Verified;
        internal string? DownloadedFile;
        internal CloudOwnedCopyRelease Result = new("trashed");
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) => Task.FromResult(new CloudReadiness(true));
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
        {
            if (DownloadedFile == null) throw new AssertFailedException("Releasing a cloud copy cannot run a download.");
            Assert.AreEqual(1, PrepareCalls);
            return Task.FromResult(new CloudDownloadResult([DownloadedFile]));
        }
        public Task<bool> IsOwnedCopyEnabledAsync(CloudDownloadRequest request, CancellationToken ct) => Task.FromResult(OptIn);
        public Task<CloudOwnedCopyReceipt?> PrepareOwnedCopyAsync(CloudDownloadRequest request, CancellationToken ct)
        {
            PrepareCalls++;
            return Task.FromResult<CloudOwnedCopyReceipt?>(OptIn ? new("account", "version", "remote-container", "manifest") : null);
        }
        public Task<CloudOwnedCopyRelease> ReleaseOwnedCopyAsync(CloudDownloadRequest request, CloudOwnedCopyReceipt copy,
            CloudVerifiedLocalDownload local, CancellationToken ct)
        {
            ReleaseCalls++; Verified = local;
            if (ThrowOnRelease) throw new IOException("example private details");
            return Task.FromResult(Result);
        }
    }
}
