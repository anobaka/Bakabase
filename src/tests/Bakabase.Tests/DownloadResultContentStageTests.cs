using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Steps;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.View;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadResultContentStageTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;
    private byte[] _metadata = null!;
    private FakeTorrent _torrent = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "download-result-stages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "fixture.txt");
        await File.WriteAllTextAsync(source, "source contents");
        _metadata = (await new TorrentCreator(TorrentType.V1Only) {PieceLength = 16384}
            .CreateAsync(new TorrentFileSource(source))).Encode();
        _torrent = new FakeTorrent();
        _sp = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.AddSingleton<ITorrentDownloader>(_torrent);
            s.AddSingleton<IExHentaiAcquisitionQueue, UnusedQueue>();
        });
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.LibraryRootDirectory = Path.Combine(_root, "library");
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private AcquisitionStepContext Context(string? config = null) =>
        new(_sp, NullLogger.Instance, (_, _) => Task.CompletedTask, Path.Combine(_root, "working"),
            config, AcquisitionTaskId: 7, WorkflowRunId: 19);

    private AcquisitionWorkItem Item(DownloadResultDbModel result) => new()
    {
        ResourceId = 11, LeadKind = AcquisitionLeadKind.PlatformHolding, LeadValue = "ExHentai:123/abc",
        WorkingDirectory = Path.Combine(_root, "working"), WorkingName = "Gallery",
        Variables = new Dictionary<string, string>
        {
            [FetchExHentaiStep.ResultIdVariable] = result.Id.ToString(),
            [FetchExHentaiStep.ResultKindVariable] = result.Kind.ToString()
        }
    };

    private async Task<DownloadResultDbModel> Result(bool owned = true, bool local = false)
    {
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        DownloadResultDbModel result;
        var records = _sp.GetRequiredService<DownloadResultService>();
        if (local)
        {
            var file = Path.Combine(source, "page.txt");
            await File.WriteAllTextAsync(file, "this work");
            await File.WriteAllTextAsync(Path.Combine(source, "another-work.txt"), "not this work");
            result = await records.RecordFilesAsync(17, ThirdPartyId.ExHentai, "123/abc", "Gallery", source, [file], null);
        }
        else
        {
            var torrent = Path.Combine(source, "gallery.torrent");
            await File.WriteAllBytesAsync(torrent, _metadata);
            result = await records.RecordTorrentAsync(17, ThirdPartyId.ExHentai, "123/abc", "Gallery", torrent, null);
        }
        if (owned)
            db.Set<DownloadResultOwnerDbModel>().Add(new()
                {DownloadTaskId = 17, AcquisitionTaskId = 7, WorkflowRunId = 19, ResourceId = 11});
        else
            db.Set<DownloadResultProcessingDbModel>().Add(new()
                {DownloadResultId = result.Id, WorkflowRunId = 19, ResourceId = 11});
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<DownloadResultViewModel> View()
    {
        var controller = new DownloadResultController(_sp.GetRequiredService<BakabaseDbContext>(),
            _sp.GetRequiredService<DownloadResultWorkflowService>())
        {ControllerContext = new ControllerContext {HttpContext = new DefaultHttpContext()}};
        return (await controller.Get(17)).Data!.Single();
    }

    private Task<DownloadResultProcessingDbModel> State(int id) => _sp.GetRequiredService<BakabaseDbContext>()
        .Set<DownloadResultProcessingDbModel>().AsNoTracking().SingleAsync(p => p.DownloadResultId == id);

    private async Task<AcquisitionWorkItem> Fetch(DownloadResultDbModel result)
    {
        var reference = await _sp.GetRequiredService<IAcquisitionTorrentMetadataStore>().SaveAsync(_metadata);
        var item = Item(result) with {Links = [new(reference)], SelectedLinkIndex = 0};
        return ((AcquisitionStepOutcome.Continue) await new FetchResultTorrentStep()
            .ExecuteAsync(Context(), item, default)).Item;
    }

    [TestMethod]
    public async Task OwnedTorrent_IsReadyWhilePlacementWaits_WithoutResourceMaterialization()
    {
        var result = await Result();
        var item = await Fetch(result);
        var conflict = Path.Combine(_root, "library", "Gallery");
        Directory.CreateDirectory(conflict);
        await File.WriteAllTextAsync(Path.Combine(conflict, "existing.txt"), "existing");
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Suspend>(await new PlaceStep().ExecuteAsync(Context(), item, default));
        var state = await State(result.Id);
        Assert.IsNotNull(state.ContentsReadyAt);
        Assert.IsNull(state.ResourceId, "Actual resource association still belongs to materialization.");
        var view = await View();
        Assert.IsTrue(view.ContentsReady);
        Assert.AreEqual(item.ExtractedDirectory, view.ContentsDirectory);
        Assert.AreEqual(1, _torrent.Downloads);
    }

    [TestMethod]
    public async Task OwnedLocalFiles_AreReadyAsSoonAsThePlatformResultResumes()
    {
        var result = await Result(local: true);
        var outcome = await new FetchExHentaiStep().ResumeAsync(Context(), Item(result),
            new(AcquisitionWaitReason.PlatformFetch, null), default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome,
            (outcome as AcquisitionStepOutcome.Fail)?.Message);
        Assert.IsTrue((await View()).ContentsReady);
        var state = await State(result.Id);
        Assert.IsNull(state.ResourceId);
        Assert.AreEqual(1, JsonSerializer.Deserialize<string[]>(state.ContentsFilesJson!)!.Length);
        Assert.AreEqual(0, _torrent.Downloads);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PlacementTracksOnlyMovedFiles_BeforeMaterialization(bool owned)
    {
        var result = await Result(owned);
        var item = await Fetch(result);
        var target = Path.Combine(_root, "library", "Gallery");
        Directory.CreateDirectory(target);
        var neighbour = Path.Combine(target, "previous-resource.txt");
        await File.WriteAllTextAsync(neighbour, "existing library file");
        var config = JsonSerializer.Serialize(new PlaceStep.Config {OnConflict = PlacementConflictPolicy.Merge});
        var placed = (AcquisitionStepOutcome.Continue) await new PlaceStep().ExecuteAsync(Context(config), item, default);
        var state = await State(result.Id);
        var recordedFiles = JsonSerializer.Deserialize<string[]>(state.ContentsFilesJson!)!;
        Assert.AreEqual(target, state.ContentsDirectory);
        Assert.AreEqual(1, recordedFiles.Length);
        Assert.IsFalse(recordedFiles.Contains(neighbour), "Pre-existing merge destination files are not download outputs.");
        CollectionAssert.AreEqual(placed.Item.Files.ToArray(), recordedFiles);
        var view = await View();
        Assert.IsTrue(view.ContentsReady, "Moving completed contents must not temporarily make the result unready.");
        Assert.AreEqual(target, view.ContentsDirectory);
        File.Delete(recordedFiles.Single());
        Assert.IsFalse((await View()).ContentsReady, "An empty surviving directory is not completed content.");
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ContentsCannotBeRetargetedByAnotherRunOrResource(bool owned)
    {
        var result = await Result(owned, local: true);
        var files = JsonSerializer.Deserialize<string[]>(result.FilesJson)!;
        var service = _sp.GetRequiredService<DownloadResultWorkflowService>();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.RecordContentsAsync(result.Id, 20, 11, result.Path, files));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.RecordContentsAsync(result.Id, 19, 12, result.Path, files));
        await service.RecordContentsAsync(result.Id, 19, 11, result.Path, files);
        Assert.IsTrue((await View()).ContentsReady);
    }

    private sealed class UnusedQueue : IExHentaiAcquisitionQueue
    {
        public Task<DownloadTaskDbModel> BuildAsync(string key, string? name, string directory, CancellationToken ct) =>
            throw new NotSupportedException("This test resumes an already recorded result.");
        public Task StartAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task StopAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeTorrent : ITorrentDownloader
    {
        public int Downloads;
        public async Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Downloads++;
            var directory = Path.Combine(workingDirectory, "torrent-data");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "page.txt");
            await File.WriteAllTextAsync(file, "completed download", ct);
            return new(directory, [file]);
        }
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadMagnetAsync(string url, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}
