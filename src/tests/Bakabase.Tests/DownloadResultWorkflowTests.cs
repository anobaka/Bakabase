using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Downloader;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonoTorrent;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadResultWorkflowTests
{
    private IServiceProvider _sp = null!;
    private FakeTorrent _torrent = null!;
    private string _root = null!;
    private byte[] _metadata = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _torrent = new FakeTorrent();
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<ITorrentDownloader>(_torrent));
        _root = Path.Combine(Path.GetTempPath(), "download-result-flow-" + Guid.NewGuid().ToString("N"));
        var files = Path.Combine(_root, "fixture");
        Directory.CreateDirectory(Path.Combine(files, "chapter"));
        await File.WriteAllTextAsync(Path.Combine(files, "chapter", "page.txt"), "torrent fixture");
        _metadata = (await new TorrentCreator(TorrentType.V1Only) {PieceLength = 16 * 1024}
            .CreateAsync(new TorrentFileSource(files))).Encode();
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.LibraryRootDirectory = Path.Combine(_root, "library");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<int> Definition(string? filter = "{\"kinds\":[1]}", params string[] activities)
    {
        if (activities.Length == 0) activities = [DownloadResultWorkflow.FetchTorrent];
        return (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(new()
        {
            Name = "Test " + Guid.NewGuid().ToString("N"), Enabled = true,
            TriggerKind = DownloadResultWorkflow.Trigger, TriggerFilterJson = filter,
            Activities = activities.Select(kind => new WorkflowActivityInputModel
            {
                Kind = kind, ConfigJson = kind == AcquisitionStepKinds.Materialize
                    ? "{\"notify\":false}" : "{}", OnItemError = WorkflowActivityErrorBehavior.Fail
            }).ToList()
        })).Id;
    }

    private async Task<DownloadResultDbModel> TorrentResult(int? workflowId, string sourceKey = "123/abc")
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "saved.torrent");
        await File.WriteAllBytesAsync(source, _metadata);
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var task = new DownloadTaskDbModel
        {
            Key = "https://exhentai.org/g/" + sourceKey + "/", ThirdPartyId = ThirdPartyId.ExHentai,
            DownloadPath = directory, Status = DownloadTaskDbModelStatus.Complete
        };
        db.Set<DownloadTaskDbModel>().Add(task);
        await db.SaveChangesAsync();
        return await scope.ServiceProvider.GetRequiredService<DownloadResultService>().RecordTorrentAsync(
            task.Id, ThirdPartyId.ExHentai, sourceKey, "Gallery", source, workflowId);
    }

    private async Task Dispatch()
    {
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DownloadResultWorkflowService>().DispatchAsync();
    }

    private async Task<DownloadResultProcessingDbModel?> State(int id)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<DownloadResultProcessingDbModel>().AsNoTracking().SingleOrDefaultAsync(p => p.DownloadResultId == id);
    }

    private async Task<WorkflowRunDbModel> Run(int id)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task Execute(int id)
    {
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>().ExecuteAsync(id,
            new BTaskArgs(new PauseToken(), CancellationToken.None, new BTask("test", () => "test"),
                _ => Task.CompletedTask, _sp));
        await _sp.GetRequiredService<BTaskManager>().Clean($"workflow.run.{id}");
    }

    [TestMethod]
    public async Task RepeatedAndConcurrentDispatchCreatesOneRunAndOneQueuedTask()
    {
        var result = await TorrentResult(await Definition());
        await Task.WhenAll(Dispatch(), Dispatch(), Dispatch());
        await Dispatch();
        var state = (await State(result.Id))!;
        Assert.IsNotNull(state.WorkflowRunId);
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        Assert.AreEqual(1, await db.Set<WorkflowRunDbModel>().CountAsync());
        Assert.AreEqual(1, _sp.GetRequiredService<BTaskManager>().Tasks.Count(t => t.Id == $"workflow.run.{state.WorkflowRunId}"));
        await Execute(state.WorkflowRunId.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(state.WorkflowRunId.Value)).Status);
        Assert.AreEqual(1, _torrent.Downloads);
    }

    [TestMethod]
    public async Task CommittedPendingRunIsRequeuedAfterItsBackgroundTaskIsLost()
    {
        var result = await TorrentResult(await Definition());
        await Dispatch();
        var runId = (await State(result.Id))!.WorkflowRunId!.Value;
        var tasks = _sp.GetRequiredService<BTaskManager>();
        await tasks.Clean($"workflow.run.{runId}");
        Assert.IsFalse(tasks.Tasks.Any(t => t.Id == $"workflow.run.{runId}"));
        await Dispatch();
        Assert.AreEqual(runId, (await State(result.Id))!.WorkflowRunId);
        Assert.AreEqual(1, tasks.Tasks.Count(t => t.Id == $"workflow.run.{runId}"));
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status);
    }

    [TestMethod]
    public async Task OwnedResultNeverStartsAnIndependentRunEvenWhenItHasABinding()
    {
        var result = await TorrentResult(await Definition());
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            db.Set<DownloadResultOwnerDbModel>().Add(new()
            {
                DownloadTaskId = result.DownloadTaskId, AcquisitionTaskId = 7, WorkflowRunId = 19, ResourceId = 11
            });
            await db.SaveChangesAsync();
        }
        await Dispatch();
        Assert.IsNull(await State(result.Id));
        Assert.AreEqual(0, _torrent.Downloads);
        await using var check = _sp.CreateAsyncScope();
        Assert.AreEqual(0, await check.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().CountAsync());
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            check.ServiceProvider.GetRequiredService<DownloadResultWorkflowService>().RetryAsync(result.Id));
    }

    [TestMethod]
    public async Task FailedBitTorrentDownloadRetriesTheOriginalRunAndThenCompletes()
    {
        _torrent.FailuresRemaining = 1;
        var result = await TorrentResult(await Definition());
        await Dispatch();
        var runId = (await State(result.Id))!.WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await Run(runId)).Status);
        Assert.IsNull((await State(result.Id))!.ContentsReadyAt);
        await using (var scope = _sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DownloadResultWorkflowService>().RetryAsync(result.Id);
        Assert.AreEqual(runId, (await State(result.Id))!.WorkflowRunId);
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status);
        Assert.IsNotNull((await State(result.Id))!.ContentsReadyAt);
        Assert.AreEqual(2, _torrent.Downloads);
    }

    [TestMethod]
    public async Task DeletingTheOriginalTorrentCopyDoesNotLoseTheManagedMetadata()
    {
        var result = await TorrentResult(await Definition());
        File.Delete(Path.Combine(result.DownloadDirectory, "saved.torrent"));
        Assert.IsTrue(File.Exists(result.Path));
        await Dispatch();
        var runId = (await State(result.Id))!.WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status);
        CollectionAssert.AreEqual(_metadata, _torrent.LastMetadata);
    }

    [TestMethod]
    public async Task MissingBindingAndMismatchedFilterDoNotDispatch()
    {
        var unbound = await TorrentResult(null);
        var filtered = await TorrentResult(await Definition("{\"kinds\":[2]}"));
        await Dispatch();
        Assert.IsNull(await State(unbound.Id));
        Assert.IsTrue((await State(filtered.Id))!.FilterDidNotMatch);
        Assert.IsNull((await State(filtered.Id))!.WorkflowRunId);
        Assert.AreEqual(0, _torrent.Downloads);
    }

    [TestMethod]
    public async Task MissingWorkflowRecordsItsErrorWithoutBlockingAnotherResult()
    {
        var invalid = await TorrentResult(999999);
        var valid = await TorrentResult(await Definition(), "124/def");
        await Dispatch();
        Assert.IsFalse(string.IsNullOrWhiteSpace((await State(invalid.Id))!.DispatchError));
        Assert.IsNull((await State(invalid.Id))!.WorkflowRunId);
        Assert.IsNotNull((await State(valid.Id))!.WorkflowRunId);
        var runId = (await State(valid.Id))!.WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status);
    }

    [TestMethod]
    public async Task APageOfCoolingDownErrorsDoesNotStarveTheNextValidResult()
    {
        var first = await TorrentResult(999999);
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            db.Set<DownloadResultDbModel>().AddRange(Enumerable.Range(1, 99).Select(i => new DownloadResultDbModel
            {
                DownloadTaskId = first.DownloadTaskId, ThirdPartyId = first.ThirdPartyId,
                SourceKey = $"{1000 + i}/abc", Name = $"Invalid workflow {i}", Kind = first.Kind,
                Path = first.Path, DownloadDirectory = first.DownloadDirectory, FilesJson = first.FilesJson,
                Fingerprint = first.Fingerprint, DeduplicationKey = Guid.NewGuid().ToString("N"),
                WorkflowDefinitionId = 999999
            }));
            await db.SaveChangesAsync();
        }
        var valid = await TorrentResult(await Definition(), "9999/abc");
        await Dispatch();
        Assert.IsNull(await State(valid.Id), "The first page contains exactly 100 invalid results.");
        await using (var check = _sp.CreateAsyncScope())
            Assert.AreEqual(100, await check.ServiceProvider.GetRequiredService<BakabaseDbContext>()
                .Set<DownloadResultProcessingDbModel>().CountAsync(p => p.DispatchError != null));

        // No delay: the failed first page is still inside its retry cooldown.
        await Dispatch();
        var state = (await State(valid.Id))!;
        Assert.IsNotNull(state);
        Assert.IsNotNull(state.WorkflowRunId);
        await Execute(state.WorkflowRunId.Value);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(state.WorkflowRunId.Value)).Status);
        Assert.AreEqual(1, _torrent.Downloads);
    }

    [TestMethod]
    public async Task PreparingAndPlacingLocalFilesMatchesTheResourceAndLeavesNeighboursUntouched()
    {
        var definition = await Definition(null, DownloadResultWorkflow.PrepareResource,
            AcquisitionStepKinds.Place, AcquisitionStepKinds.Materialize);
        var original = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateOrMatchByExternalIdentity(
            ResourceSource.ExHentai, "123/abc", new KnownItemDetail("Gallery"));
        var shared = Path.Combine(_root, "shared");
        Directory.CreateDirectory(Path.Combine(shared, "chapter"));
        var included = Path.Combine(shared, "chapter", "page.txt");
        var neighbour = Path.Combine(shared, "other-work.txt");
        await File.WriteAllTextAsync(included, "this gallery");
        await File.WriteAllTextAsync(neighbour, "another work");
        DownloadResultDbModel result;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var task = new DownloadTaskDbModel
            {
                Key = "https://exhentai.org/g/123/abc/", DownloadPath = shared,
                ThirdPartyId = ThirdPartyId.ExHentai, Status = DownloadTaskDbModelStatus.Complete
            };
            db.Set<DownloadTaskDbModel>().Add(task);
            db.Set<ExHentaiGalleryDbModel>().Add(new() {GalleryId = 123, GalleryToken = "abc", Title = "Gallery"});
            await db.SaveChangesAsync();
            result = await scope.ServiceProvider.GetRequiredService<DownloadResultService>().RecordFilesAsync(
                task.Id, ThirdPartyId.ExHentai, "123/abc", "Gallery", shared, [included], definition);
        }
        var galleries = _sp.GetRequiredService<IExHentaiGalleryService>();
        var before = await galleries.GetByGalleryId(123, "abc");
        Assert.IsNotNull(before, "Warm the same gallery cache the platform page reads.");
        Assert.IsNull(before.LocalPath);
        Assert.IsNull(before.ResourceId);
        Assert.IsFalse(before.IsDownloaded);
        await Dispatch();
        var runId = (await State(result.Id))!.WorkflowRunId!.Value;
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status, (await Run(runId)).ErrorMessage);
        var state = (await State(result.Id))!;
        Assert.AreEqual(original.ResourceId, state.ResourceId);
        Assert.IsTrue(File.Exists(Path.Combine(state.ContentsDirectory!, "chapter", "page.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(state.ContentsDirectory!, "other-work.txt")));
        Assert.AreEqual("another work", await File.ReadAllTextAsync(neighbour));
        Assert.IsTrue(File.Exists(included), "A shared source directory remains under the source downloader's ownership.");
        var resource = (await _sp.GetRequiredService<IResourceService>().GetAll(r => r.Id == original.ResourceId)).Single();
        Assert.IsTrue(resource.HasLocalPath);
        var gallery = await galleries.GetByGalleryId(123, "abc");
        Assert.IsNotNull(gallery);
        Assert.AreEqual(state.ContentsDirectory, gallery.LocalPath);
        Assert.AreEqual(original.ResourceId, gallery.ResourceId);
        Assert.IsTrue(gallery.IsDownloaded);
    }

    private sealed class FakeTorrent : ITorrentDownloader
    {
        public int Downloads;
        public int FailuresRemaining;
        public byte[] LastMetadata = [];
        public async Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Downloads++;
            LastMetadata = metadata;
            if (FailuresRemaining-- > 0) throw new IOException("The test peer disconnected.");
            var root = Path.Combine(workingDirectory, "torrent-data");
            Directory.CreateDirectory(Path.Combine(root, "chapter"));
            var file = Path.Combine(root, "chapter", "page.txt");
            await File.WriteAllTextAsync(file, "downloaded torrent contents", ct);
            return new(root, [file]);
        }
        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}
