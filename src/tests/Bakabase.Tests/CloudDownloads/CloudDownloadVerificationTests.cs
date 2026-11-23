using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudDownloadVerificationTests
{
    private string _root = null!;
    private BakabaseDbContext _db = null!;
    private DownloadResultService _results = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "cloud-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options);
        await _db.Database.EnsureCreatedAsync();
        _results = new(_db, () => Path.Combine(_root, "appdata"));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task Provider_completion_enters_local_verification_and_successful_hash_reads_keep_the_downloader_alive()
    {
        var directory = Path.Combine(_root, "payload"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "one-large-file.bin");
        var bytes = new byte[2 * ArtifactContentHash.BufferSize + 17]; new Random(19).NextBytes(bytes);
        await File.WriteAllBytesAsync(file, bytes);
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(Probe), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.BaiduNetdisk, TaskType = 1,
            EnumTaskType = BaiduCloudTaskType.RemoteFileOrDirectory,
            Name = "Verification fixture", DownloaderType = typeof(Probe), HelperType = typeof(object),
            DefaultConvention = string.Empty
        });
        var services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "services"), collection =>
        {
            collection.RemoveAll<ICloudDownloadProvider>();
            collection.AddSingleton<ICloudDownloadProvider>(new Provider(file, bytes.Length));
            collection.AddSingleton(_results);
        });
        try
        {
            using var downloader = new Probe(services);
            var stale = DateTime.Now.AddHours(-1);
            var enteredVerification = false;
            downloader.OnCurrentChanged += () =>
            {
                if (downloader.Current == "Verifying")
                {
                    enteredVerification = true;
                    Assert.IsNull(downloader.TransferProgress, "Local hashing must not claim network transfer bytes.");
                    Assert.IsNull(downloader.DownloadSpeedBytesPerSecond);
                    // Isolate the hash-read signal from the one-time stage transition and the
                    // final 100% event. The queue watchdog reads this exact property.
                    typeof(AbstractDownloader<BaiduCloudTaskType>).GetProperty(nameof(downloader.LastActivityAt))!
                        .SetValue(downloader, stale);
                }
                return Task.CompletedTask;
            };
            var checkedBeforePublication = false;
            _db.SavingChanges += (_, _) =>
            {
                checkedBeforePublication = true;
                Assert.IsTrue(enteredVerification);
                Assert.AreEqual("Verifying", downloader.Current);
                Assert.IsTrue(downloader.LastActivityAt > stale,
                    "Actual reads must refresh liveness before the receipt is saved or final progress is reported.");
                Assert.IsNull(downloader.TransferProgress);
            };
            var request = new CloudDownloadRequest("fixture", new CloudSource
                {Id = "source", ProviderId = "baidu", Kind = "remote", Url = "/fixture"}, "account", null, directory);
            await downloader.RunCoreAsync(new DownloadTask
            {
                Id = 17, ThirdPartyId = ThirdPartyId.BaiduNetdisk, Key = "fixture", Name = "fixture",
                DownloadPath = directory, Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)
            });
            Assert.IsTrue(checkedBeforePublication);
            var result = await _db.DownloadResults.SingleAsync();
            var manifest = await ArtifactManifestFiles.CaptureAsync(directory, [file]);
            Assert.AreEqual(manifest.Fingerprint, result.Fingerprint);
        }
        finally { if (services is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Local_receipt_rechecks_a_completed_member_on_timestamp_drift(bool changeContent)
    {
        if (OperatingSystem.IsWindows()) return;
        var file = Path.Combine(_root, "completed");
        await File.WriteAllTextAsync(file, "original");
        var before = File.GetLastWriteTimeUtc(file);
        var reads = 0;
        var recording = _results.RecordFilesAsync(17, ThirdPartyId.BaiduNetdisk, "fixture", "fixture", _root,
            [file], null, _ =>
            {
                if (++reads != 1) return;
                if (changeContent) File.WriteAllText(file, "modified");
                File.SetLastWriteTimeUtc(file, before.AddMinutes(1));
            }, default);
        if (changeContent)
        {
            var error = await Assert.ThrowsExactlyAsync<IOException>(() => recording);
            StringAssert.Contains(error.Message, "(content)");
            Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        }
        else
        {
            var result = await recording;
            var manifest = await ArtifactManifestFiles.CaptureAsync(_root, [file]);
            Assert.AreEqual(manifest.Fingerprint, result.Fingerprint);
            Assert.AreEqual(1, await _db.DownloadResults.CountAsync());
        }
        Assert.AreEqual(2, reads);
    }

    [TestMethod]
    public async Task Cancelling_during_local_verification_never_publishes_a_partial_download_receipt()
    {
        var file = Path.Combine(_root, "payload.bin");
        await File.WriteAllBytesAsync(file, new byte[2 * ArtifactContentHash.BufferSize + 1]);
        using var cancellation = new CancellationTokenSource();
        var reports = 0;
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => _results.RecordFilesAsync(
            17, ThirdPartyId.BaiduNetdisk, "fixture", "fixture", _root, [file], null,
            read => { Assert.IsTrue(read > 0); reports++; cancellation.Cancel(); }, cancellation.Token));
        Assert.AreEqual(1, reports);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        Assert.IsTrue(File.Exists(file));
    }

    private sealed class Probe(IServiceProvider services) : CloudSiteDownloader<BaiduCloudTaskType>(services)
    {
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.BaiduNetdisk;
        public override BaiduCloudTaskType EnumTaskType => BaiduCloudTaskType.RemoteFileOrDirectory;
        internal Task RunCoreAsync(DownloadTask task)
        { Status = DownloaderStatus.Downloading; return StartCore(task, default); }
    }

    private sealed class Provider(string file, long bytes) : ICloudDownloadProvider
    {
        public string ProviderId => "baidu";
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) => Task.FromResult(new CloudReadiness(true));
        public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
            Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
        {
            await progress(new(0, 0, bytes, "Downloading"));
            await progress(new(100, bytes, bytes, "Downloading"));
            return new([file]);
        }
    }
}
