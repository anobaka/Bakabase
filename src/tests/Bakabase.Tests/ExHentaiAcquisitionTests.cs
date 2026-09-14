using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.Acquisition.Steps;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiAcquisitionTests
{
    private BakabaseDbContext _db = null!;
    private ServiceProvider _services = null!;
    private Queue _queue = null!;
    private Torrent _torrent = null!;
    private ExHentaiAcquisitionService _owned = null!;
    private string _root = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "exhentai-acquisition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Set<AcquisitionTaskDbModel>().Add(new()
        {
            Id = 7, ResourceId = 11, WorkflowRunId = 19,
            Status = AcquisitionStatus.Running, LeadKind = AcquisitionLeadKind.PlatformHolding,
            LeadValue = "ExHentai:123/abc", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        _queue = new Queue(_db);
        _torrent = new Torrent();
        _owned = new ExHentaiAcquisitionService(_db, _queue);
        _services = new ServiceCollection().AddSingleton(_owned)
            .AddSingleton<IAcquisitionTorrentMetadataStore, Metadata>()
            .AddSingleton<ITorrentDownloader>(_torrent).BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _services.DisposeAsync();
        await _db.DisposeAsync();
        Directory.Delete(_root, true);
    }

    private AcquisitionStepContext Context() => new(_services, NullLogger.Instance,
        (_, _) => Task.CompletedTask, _root, AcquisitionTaskId: 7, WorkflowRunId: 19);
    private AcquisitionWorkItem Item() => new()
    {
        ResourceId = 11, LeadKind = AcquisitionLeadKind.PlatformHolding,
        LeadValue = "ExHentai:123/abc", WorkingDirectory = _root, WorkingName = "Gallery"
    };

    [TestMethod]
    public async Task RepeatedExecutionKeepsOneOwnedPlatformTask()
    {
        var step = new FetchExHentaiStep();
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Suspend>(await step.ExecuteAsync(Context(), Item(), default));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Suspend>(await step.ExecuteAsync(Context(), Item(), default));
        Assert.AreEqual(1, _queue.Builds);
        Assert.AreEqual(1, _queue.Starts);
        Assert.AreEqual(1, await _db.Set<DownloadTaskDbModel>().CountAsync());
        var owner = await _db.Set<DownloadResultOwnerDbModel>().SingleAsync();
        Assert.AreEqual(7, owner.AcquisitionTaskId);
        Assert.AreEqual(19, owner.WorkflowRunId);
        Assert.AreEqual(11, owner.ResourceId);
        Assert.IsTrue(_queue.OwnerExistedBeforeStart);
    }

    [TestMethod]
    public async Task TorrentResultResumesSameResourceAndPassesMetadataToSharedDownloader()
    {
        var step = new FetchExHentaiStep();
        await step.ExecuteAsync(Context(), Item(), default);
        var metadataPath = Path.Combine(_root, "download.torrent");
        await File.WriteAllBytesAsync(metadataPath, [1, 2, 3]);
        var result = await AddResult(DownloadResultKind.TorrentMetadata, metadataPath, []);
        var resumed = (AcquisitionStepOutcome.Continue) await step.ResumeAsync(Context(), Item(),
            new(AcquisitionWaitReason.PlatformFetch, null), default);
        Assert.AreEqual(11, resumed.Item.ResourceId);
        Assert.AreEqual(result.Id.ToString(), resumed.Item.Variables[FetchExHentaiStep.ResultIdVariable]);
        Assert.IsNull(resumed.Item.TargetDirectory);
        Assert.AreEqual(0, resumed.Item.Files.Count);
        Assert.IsTrue(AcquisitionTorrentMetadataStore.IsManagedReference(resumed.Item.SelectedLink!.Url));
        var fetched = (AcquisitionStepOutcome.Continue) await new FetchResultTorrentStep()
            .ExecuteAsync(Context(), resumed.Item, default);
        Assert.AreEqual(1, _torrent.Downloads);
        Assert.AreEqual(11, fetched.Item.ResourceId);
        Assert.IsTrue(fetched.Item.PreserveDirectoryStructure);
        Assert.AreEqual(1, fetched.Item.Files.Count);
        Assert.AreEqual(1, await _db.Set<AcquisitionTaskDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task FileResultResumesWithoutStartingBitTorrent()
    {
        var step = new FetchExHentaiStep();
        await step.ExecuteAsync(Context(), Item(), default);
        var directory = Path.Combine(_root, "gallery");
        Directory.CreateDirectory(Path.Combine(directory, "chapter"));
        var file = Path.Combine(directory, "chapter", "page.jpg");
        await File.WriteAllTextAsync(file, "image fixture");
        await AddResult(DownloadResultKind.LocalFiles, directory, [file]);
        var resumed = (AcquisitionStepOutcome.Continue) await step.ResumeAsync(Context(), Item(),
            new(AcquisitionWaitReason.PlatformFetch, null), default);
        var skipped = await new FetchResultTorrentStep().ExecuteAsync(Context(), resumed.Item, default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(skipped);
        Assert.AreEqual(0, _torrent.Downloads);
        Assert.AreEqual(directory, resumed.Item.ExtractedDirectory);
        Assert.AreEqual(file, resumed.Item.Files.Single());
        Assert.AreEqual(11, resumed.Item.ResourceId);
    }

    [DataTestMethod]
    [DataRow(DownloadTaskDbModelStatus.Failed)]
    [DataRow(DownloadTaskDbModelStatus.Disabled)]
    [DataRow(DownloadTaskDbModelStatus.Complete)]
    public async Task AChildThatEndsWithoutResultFailsRatherThanWaitingForever(DownloadTaskDbModelStatus status)
    {
        var step = new FetchExHentaiStep();
        await step.ExecuteAsync(Context(), Item(), default);
        await _db.Set<DownloadTaskDbModel>().ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
        var outcome = await step.ResumeAsync(Context(), Item(), new(AcquisitionWaitReason.PlatformFetch, null), default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
        Assert.AreEqual(1, _queue.Starts);
    }

    [TestMethod]
    public async Task CancellationStopsOnlyOwnedTaskAndPreventsRestart()
    {
        await new FetchExHentaiStep().ExecuteAsync(Context(), Item(), default);
        _db.Set<DownloadTaskDbModel>().Add(new() {Key = "other", DownloadPath = _root});
        await _db.SaveChangesAsync();
        await _db.Set<AcquisitionTaskDbModel>().ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AcquisitionStatus.Cancelled));
        await _owned.StopAbandonedAsync(default);
        Assert.AreEqual(1, _queue.Stops.Count);
        var owner = await _db.Set<DownloadResultOwnerDbModel>().SingleAsync();
        Assert.AreEqual(owner.DownloadTaskId, _queue.Stops.Single());
        Assert.AreEqual(DownloadTaskDbModelStatus.InProgress,
            (await _db.Set<DownloadTaskDbModel>().AsNoTracking().SingleAsync(t => t.Key == "other")).Status);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(
            await new FetchExHentaiStep().ExecuteAsync(Context(), Item(), default));
        Assert.AreEqual(1, _queue.Starts);
    }

    [TestMethod]
    public async Task ResultFilesOutsideTheirRootAreRejected()
    {
        var step = new FetchExHentaiStep();
        await step.ExecuteAsync(Context(), Item(), default);
        var directory = Path.Combine(_root, "gallery");
        Directory.CreateDirectory(directory);
        var outside = Path.Combine(_root, "other.jpg");
        await File.WriteAllTextAsync(outside, "not this gallery");
        await AddResult(DownloadResultKind.LocalFiles, directory, [outside]);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await step.ResumeAsync(Context(), Item(),
            new(AcquisitionWaitReason.PlatformFetch, null), default));
    }

    [TestMethod]
    public void ExHentaiDefaultIsSpecificAndPreservesUserOverrides()
    {
        var options = new AcquisitionOptions();
        Assert.AreEqual(BuiltinAcquisitionRecipes.ExHentaiDownload,
            BuiltinAcquisitionRecipes.DefaultRecipeNameFor(AcquisitionLeadKind.PlatformHolding, "ExHentai:123/abc", options));
        Assert.AreEqual(BuiltinAcquisitionRecipes.PlatformFetch,
            BuiltinAcquisitionRecipes.DefaultRecipeNameFor(AcquisitionLeadKind.PlatformHolding, "Steam:123", options));
        options.RecipeByLeadKind[AcquisitionLeadKind.PlatformHolding] = "Mine";
        Assert.AreEqual("Mine",
            BuiltinAcquisitionRecipes.DefaultRecipeNameFor(AcquisitionLeadKind.PlatformHolding, "ExHentai:123/abc", options));
    }

    private async Task<DownloadResultDbModel> AddResult(DownloadResultKind kind, string path, string[] files)
    {
        var owner = await _db.Set<DownloadResultOwnerDbModel>().SingleAsync();
        var result = new DownloadResultDbModel
        {
            DownloadTaskId = owner.DownloadTaskId, SourceKey = "123/abc", ThirdPartyId = ThirdPartyId.ExHentai,
            Kind = kind, Path = path, DownloadDirectory = _root, FilesJson = JsonSerializer.Serialize(files),
            Fingerprint = Guid.NewGuid().ToString("N"), DeduplicationKey = Guid.NewGuid().ToString("N")
        };
        _db.Set<DownloadResultDbModel>().Add(result);
        await _db.SaveChangesAsync();
        return result;
    }

    private sealed class Queue(BakabaseDbContext db) : IExHentaiAcquisitionQueue
    {
        public int Builds;
        public int Starts;
        public bool OwnerExistedBeforeStart;
        public List<int> Stops { get; } = [];
        public Task<DownloadTaskDbModel> BuildAsync(string sourceKey, string? name, string directory, CancellationToken ct)
        {
            Builds++;
            return Task.FromResult(new DownloadTaskDbModel
            {
                Key = sourceKey, Name = name, DownloadPath = directory, ThirdPartyId = ThirdPartyId.ExHentai,
                Status = DownloadTaskDbModelStatus.Disabled
            });
        }
        public async Task StartAsync(int downloadTaskId, CancellationToken ct)
        {
            Starts++;
            OwnerExistedBeforeStart = await db.Set<DownloadResultOwnerDbModel>().AnyAsync(o => o.DownloadTaskId == downloadTaskId, ct);
            await db.Set<DownloadTaskDbModel>().Where(t => t.Id == downloadTaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.InProgress), ct);
        }
        public async Task StopAsync(int downloadTaskId, CancellationToken ct)
        {
            Stops.Add(downloadTaskId);
            await db.Set<DownloadTaskDbModel>().Where(t => t.Id == downloadTaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Disabled), ct);
        }
    }

    private sealed class Metadata : IAcquisitionTorrentMetadataStore
    {
        private byte[] _bytes = [];
        public Task<string> SaveAsync(byte[] metadata, CancellationToken ct = default)
        {
            _bytes = metadata;
            return Task.FromResult(AcquisitionTorrentMetadataStore.ReferencePrefix + new string('a', 64));
        }
        public Task<byte[]> ReadAsync(string reference, CancellationToken ct = default) => Task.FromResult(_bytes);
    }

    private sealed class Torrent : ITorrentDownloader
    {
        public int Downloads;
        public async Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Downloads++;
            var directory = Path.Combine(workingDirectory, "torrent-data", "chapter");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "page.jpg");
            await File.WriteAllTextAsync(file, "downloaded", ct);
            return new TorrentDownloadResult(Path.GetDirectoryName(directory)!, [file]);
        }
        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}
