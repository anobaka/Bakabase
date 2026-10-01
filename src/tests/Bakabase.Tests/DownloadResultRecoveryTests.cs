using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadResultRecoveryTests
{
    private const int TaskId = 10;
    private const string SourceKey = "4171374/abcdef0123";
    private string _root = null!;
    private BakabaseDbContext _db = null!;
    private DownloadResultService _results = null!;
    private byte[] _metadata = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseResultRecovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options);
        await _db.Database.EnsureCreatedAsync();
        _results = new DownloadResultService(_db, () => Path.Combine(_root, "appdata"));
        _metadata = await CreateMetadata("fixture.txt", "Original gallery contents.");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IdenticalRedownload_RefreshesOutputPathsWithoutRetargetingItsResult(bool handedOff)
    {
        var oldDirectory = Path.Combine(_root, "old-output");
        var oldFiles = await WriteGallery(oldDirectory);
        var first = await _results.RecordFilesAsync(TaskId, ThirdPartyId.ExHentai, SourceKey,
            "Old name", oldDirectory, oldFiles, 3);
        var processing = new DownloadResultProcessingDbModel
        {
            DownloadResultId = first.Id, WorkflowRunId = 19, ResourceId = 11,
            DispatchError = "Existing continuation", FilterDidNotMatch = true,
            LastAttemptAt = new DateTime(2026, 9, 30, 1, 2, 3, DateTimeKind.Utc)
        };
        if (handedOff)
        {
            processing.ContentsDirectory = Path.Combine(_root, "library");
            processing.ContentsFilesJson = JsonSerializer.Serialize(await WriteGallery(processing.ContentsDirectory));
            processing.ContentsReadyAt = new DateTime(2026, 9, 30, 1, 3, 4, DateTimeKind.Utc);
        }
        _db.Set<DownloadResultProcessingDbModel>().Add(processing);
        _db.Set<DownloadResultOwnerDbModel>().Add(new()
            {DownloadTaskId = TaskId, AcquisitionTaskId = 7, WorkflowRunId = 19, ResourceId = 11});
        await _db.SaveChangesAsync();
        Directory.Delete(oldDirectory, true);
        Assert.AreEqual(handedOff, await _results.CanReuseAsync(first, CancellationToken.None));

        var newDirectory = Path.Combine(_root, "new-output");
        var newFiles = await WriteGallery(newDirectory);
        var nextService = new DownloadResultService(_db, () => Path.Combine(_root, "appdata"));
        var recovered = await nextService.RecordFilesAsync(TaskId, ThirdPartyId.ExHentai, SourceKey,
            "Recovered name", newDirectory, newFiles, 99);
        var saved = (await nextService.GetAsync(recovered.Id))!;

        Assert.AreEqual(first.Id, saved.Id, "Recovery must preserve the result referenced by its continuation.");
        Assert.AreEqual(first.Fingerprint, saved.Fingerprint);
        Assert.AreEqual(first.DeduplicationKey, saved.DeduplicationKey);
        Assert.AreEqual(first.CreatedAt.Ticks, saved.CreatedAt.Ticks);
        Assert.AreEqual(3, saved.WorkflowDefinitionId, "Re-recording must not replace the frozen binding.");
        Assert.AreEqual("Recovered name", saved.Name);
        Assert.AreEqual(Path.GetFullPath(newDirectory), saved.Path);
        Assert.AreEqual(Path.GetFullPath(newDirectory), saved.DownloadDirectory);
        CollectionAssert.AreEquivalent(newFiles, JsonSerializer.Deserialize<string[]>(saved.FilesJson)!);
        Assert.IsTrue(await nextService.CanReuseAsync(saved, CancellationToken.None));
        Assert.AreEqual(1, await _db.DownloadResults.CountAsync());

        var savedProcessing = await _db.Set<DownloadResultProcessingDbModel>().AsNoTracking()
            .SingleAsync(p => p.DownloadResultId == saved.Id);
        Assert.AreEqual(processing.WorkflowRunId, savedProcessing.WorkflowRunId);
        Assert.AreEqual(processing.ResourceId, savedProcessing.ResourceId);
        Assert.AreEqual(processing.ContentsDirectory, savedProcessing.ContentsDirectory);
        Assert.AreEqual(processing.ContentsFilesJson, savedProcessing.ContentsFilesJson);
        Assert.AreEqual(processing.ContentsReadyAt?.Ticks, savedProcessing.ContentsReadyAt?.Ticks);
        Assert.AreEqual(processing.DispatchError, savedProcessing.DispatchError);
        Assert.AreEqual(processing.FilterDidNotMatch, savedProcessing.FilterDidNotMatch);
        Assert.AreEqual(processing.LastAttemptAt?.Ticks, savedProcessing.LastAttemptAt?.Ticks);
        var owner = await _db.Set<DownloadResultOwnerDbModel>().AsNoTracking().SingleAsync();
        Assert.AreEqual(7, owner.AcquisitionTaskId);
        Assert.AreEqual(19, owner.WorkflowRunId);
        Assert.AreEqual(11, owner.ResourceId);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RecordedMovedContents_TakePriorityEvenWithoutResourceMaterialization(bool deleteSource)
    {
        var result = await RecordGallery();
        var directory = Path.Combine(_root, "placed");
        var files = await WriteGallery(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "unrelated-neighbour.txt"), "Not a download output.");
        await RecordProcessing(result, directory, JsonSerializer.Serialize(files));
        if (deleteSource) Directory.Delete(result.Path, true);

        var contents = await _results.GetAvailableContentsAsync(result, CancellationToken.None);

        Assert.IsNotNull(contents);
        Assert.AreEqual(directory, contents.Directory);
        CollectionAssert.AreEquivalent(files, contents.Files.ToArray());
        Assert.IsTrue(await _results.CanReuseAsync(result, CancellationToken.None));
        Assert.IsNull((await _db.Set<DownloadResultProcessingDbModel>().AsNoTracking().SingleAsync()).ResourceId,
            "Placement can durably hand off contents before materialization creates a resource association.");
    }

    [DataTestMethod]
    [DataRow("missing-file")]
    [DataRow("partial-files")]
    [DataRow("missing-directory")]
    [DataRow("empty")]
    [DataRow("null")]
    [DataRow("malformed")]
    [DataRow("wrong-shape")]
    [DataRow("unready")]
    [DataRow("outside-directory")]
    public async Task InvalidProcessingContents_FallBackToTheVerifiedSource(string invalidity)
    {
        var result = await RecordGallery();
        var sourceFiles = JsonSerializer.Deserialize<string[]>(result.FilesJson)!;
        var directory = Path.Combine(_root, "placed");
        var files = await WriteGallery(directory);
        var json = JsonSerializer.Serialize(files);
        switch (invalidity)
        {
            case "missing-file":
                foreach (var file in files) File.Delete(file);
                break;
            case "partial-files": File.Delete(files[1]); break;
            case "missing-directory": Directory.Delete(directory, true); break;
            case "empty": json = "[]"; break;
            case "null": json = "null"; break;
            case "malformed": json = "["; break;
            case "wrong-shape": json = "{\"files\":[]}"; break;
            case "outside-directory": json = JsonSerializer.Serialize(sourceFiles); break;
        }
        await RecordProcessing(result, directory, json, ready: invalidity != "unready");

        var contents = await _results.GetAvailableContentsAsync(result, CancellationToken.None);

        Assert.IsNotNull(contents);
        Assert.AreEqual(result.Path, contents.Directory);
        CollectionAssert.AreEquivalent(sourceFiles, contents.Files.ToArray());
        Assert.IsTrue(await _results.CanReuseAsync(result, CancellationToken.None));
    }

    [DataTestMethod]
    [DataRow("complete", true)]
    [DataRow("partial-files", false)]
    [DataRow("missing-file", false)]
    [DataRow("missing-directory", false)]
    [DataRow("empty", false)]
    [DataRow("null", false)]
    [DataRow("malformed", false)]
    [DataRow("wrong-shape", false)]
    [DataRow("outside-directory", false)]
    public async Task SourceAvailability_RequiresEveryExplicitFileRatherThanASurvivingDirectory(
        string state, bool available)
    {
        var result = await RecordGallery();
        var files = JsonSerializer.Deserialize<string[]>(result.FilesJson)!;
        await File.WriteAllTextAsync(Path.Combine(result.Path, "unrelated-neighbour.txt"), "Not a download output.");
        switch (state)
        {
            case "partial-files": File.Delete(files[1]); break;
            case "missing-file":
                foreach (var file in files) File.Delete(file);
                break;
            case "missing-directory": Directory.Delete(result.Path, true); break;
            case "empty": result.FilesJson = "[]"; break;
            case "null": result.FilesJson = "null"; break;
            case "malformed": result.FilesJson = "["; break;
            case "wrong-shape": result.FilesJson = "{\"files\":[]}"; break;
            case "outside-directory":
                var outside = Path.Combine(_root, "outside.txt");
                await File.WriteAllTextAsync(outside, "Not inside this gallery.");
                result.FilesJson = JsonSerializer.Serialize(new[] {outside});
                break;
        }
        _db.Update(result);
        await _db.SaveChangesAsync();
        result = (await _results.GetAsync(result.Id))!;

        var contents = await _results.GetAvailableContentsAsync(result, CancellationToken.None);

        Assert.AreEqual(available, contents != null);
        Assert.AreEqual(available, await _results.CanReuseAsync(result, CancellationToken.None));
        if (available)
        {
            Assert.AreEqual(result.Path, contents!.Directory);
            CollectionAssert.AreEquivalent(files, contents.Files.ToArray());
        }
    }

    [TestMethod]
    public async Task OwnershipAndAnUnreadyContinuation_DoNotProveMissingSourceFilesWereHandedOff()
    {
        var result = await RecordGallery(workflowId: 3);
        var directory = Path.Combine(_root, "unconfirmed-contents");
        await RecordProcessing(result, directory, JsonSerializer.Serialize(await WriteGallery(directory)), ready: false);
        _db.Set<DownloadResultOwnerDbModel>().Add(new()
            {DownloadTaskId = TaskId, AcquisitionTaskId = 7, WorkflowRunId = 19, ResourceId = 11});
        await _db.SaveChangesAsync();
        Directory.Delete(result.Path, true);

        Assert.IsNull(await _results.GetAvailableContentsAsync(result, CancellationToken.None));
        Assert.IsFalse(await _results.CanReuseAsync(result, CancellationToken.None));
    }

    [DataTestMethod]
    [DataRow("valid", true)]
    [DataRow("missing", false)]
    [DataRow("corrupt", false)]
    [DataRow("oversized", false)]
    [DataRow("changed-valid-metadata", false)]
    public async Task TorrentReuse_RequiresValidFingerprintMatchingManagedMetadata(string state, bool reusable)
    {
        var userCopy = Path.Combine(_root, "saved.torrent");
        await File.WriteAllBytesAsync(userCopy, _metadata);
        var result = await _results.RecordTorrentAsync(TaskId, ThirdPartyId.ExHentai, SourceKey,
            "Gallery", userCopy, null);
        File.Delete(userCopy);
        switch (state)
        {
            case "missing": File.Delete(result.Path); break;
            case "corrupt": await File.WriteAllTextAsync(result.Path, "<html>Login required</html>"); break;
            case "oversized": await File.WriteAllBytesAsync(result.Path, new byte[TorrentMetadata.MaxMetadataBytes + 1]); break;
            case "changed-valid-metadata":
                var changed = await CreateMetadata("different-fixture.txt", "Different gallery contents.");
                TorrentMetadata.Validate(changed);
                Assert.IsFalse(_metadata.SequenceEqual(changed));
                await File.WriteAllBytesAsync(result.Path, changed);
                break;
        }

        Assert.IsNull(await _results.GetAvailableContentsAsync(result, CancellationToken.None),
            "Metadata and a user torrent copy are not downloaded gallery contents.");
        Assert.AreEqual(reusable, await _results.CanReuseAsync(result, CancellationToken.None),
            "Deleting only the user copy must remain recoverable through the managed cache.");
    }

    [DataTestMethod]
    [DataRow("valid", DownloadTaskPrecheckOutcome.AlreadySatisfied)]
    [DataRow("missing-user-copy", DownloadTaskPrecheckOutcome.Run)]
    [DataRow("corrupt-user-copy", DownloadTaskPrecheckOutcome.Run)]
    [DataRow("missing-managed-cache", DownloadTaskPrecheckOutcome.Run)]
    [DataRow("corrupt-managed-cache", DownloadTaskPrecheckOutcome.Run)]
    public async Task TorrentCompletionStamp_OnlySkipsTheProducerWhileBothRecordedCopiesAreValid(
        string state, DownloadTaskPrecheckOutcome expected)
    {
        var userCopy = Path.Combine(_root, "saved.torrent");
        await File.WriteAllBytesAsync(userCopy, _metadata);
        var result = await _results.RecordTorrentAsync(TaskId, ThirdPartyId.ExHentai, SourceKey,
            "Gallery", userCopy, null);
        switch (state)
        {
            case "missing-user-copy": File.Delete(userCopy); break;
            case "corrupt-user-copy": await File.WriteAllTextAsync(userCopy, "<html>Login required</html>"); break;
            case "missing-managed-cache": File.Delete(result.Path); break;
            case "corrupt-managed-cache": await File.WriteAllTextAsync(result.Path, "not torrent metadata"); break;
        }
        await using var provider = new ServiceCollection().AddSingleton(_results).BuildServiceProvider();
        var precheck = BuildRecoveryPrecheck(provider.GetRequiredService<IServiceScopeFactory>());

        var verdicts = await precheck.EvaluateAsync([NewStampedTorrentTask()], CancellationToken.None);

        var outcome = verdicts.TryGetValue(TaskId, out var verdict) ? verdict.Outcome : DownloadTaskPrecheckOutcome.Run;
        Assert.AreEqual(expected, outcome,
            "An old completion stamp must not prevent the producer from repairing missing or damaged torrent files.");
    }

    [TestMethod]
    public async Task FinalTorrentCompletionCheck_BypassesASatisfiedSnapshotAfterTheUserCopyDisappears()
    {
        var userCopy = Path.Combine(_root, "saved.torrent");
        await File.WriteAllBytesAsync(userCopy, _metadata);
        await _results.RecordTorrentAsync(TaskId, ThirdPartyId.ExHentai, SourceKey, "Gallery", userCopy, null);
        await using var provider = new ServiceCollection().AddSingleton(_results).BuildServiceProvider();
        var precheck = BuildRecoveryPrecheck(provider.GetRequiredService<IServiceScopeFactory>());
        var runner = new DownloadTaskPrecheckRunner([precheck], NullLogger<DownloadTaskPrecheckRunner>.Instance);
        var task = NewStampedTorrentTask();
        var first = await runner.EvaluateAsync([task]);
        Assert.AreEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, first[TaskId].Outcome);
        File.Delete(userCopy);

        var cached = await runner.EvaluateAsync([task]);
        var current = await runner.EvaluateAsync([task], useCache: false);

        Assert.AreEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, cached[TaskId].Outcome,
            "The ordinary queue ordering pass retains its bulk snapshot.");
        var outcome = current.TryGetValue(TaskId, out var verdict) ? verdict.Outcome : DownloadTaskPrecheckOutcome.Run;
        Assert.AreEqual(DownloadTaskPrecheckOutcome.Run, outcome,
            "The final completion check must observe deleted outputs even while the queue snapshot is fresh.");
    }

    private DownloadTask NewStampedTorrentTask()
    {
        var task = new DownloadTask
        {
            Id = TaskId, ThirdPartyId = ThirdPartyId.ExHentai,
            Type = (int) ExHentaiDownloadTaskType.SingleWork,
            Key = $"https://exhentai.org/g/{SourceKey}/", Name = "Gallery", DownloadPath = _root
        };
        task.SetTypedOptions(new ExHentaiTaskOptions
            {PreferTorrent = true, TorrentDownloadedAt = DateTime.UtcNow});
        return task;
    }

    private static ExHentaiDownloadTaskPrecheck BuildRecoveryPrecheck(IServiceScopeFactory scopes) => new(
        new RecoveryOptions(new ExHentaiOptions {PrioritizeTasksWithTorrent = false}),
        new EmptyTorrentVerdicts(), NullLogger<ExHentaiDownloadTaskPrecheck>.Instance, scopes);

    private sealed class RecoveryOptions(ExHentaiOptions options) : IBOptions<ExHentaiOptions>
    {
        public ExHentaiOptions Value { get; } = options;
    }

    private sealed class EmptyTorrentVerdicts : ITransientTorrentVerdictCache
    {
        public bool IsKnownNoTorrent(int taskId) => false;
    }

    private async Task<DownloadResultDbModel> RecordGallery(int? workflowId = null)
    {
        var directory = Path.Combine(_root, "source");
        return await _results.RecordFilesAsync(TaskId, ThirdPartyId.ExHentai, SourceKey, "Gallery",
            directory, await WriteGallery(directory), workflowId);
    }

    private static async Task<string[]> WriteGallery(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, "chapter"));
        var files = new[] {Path.Combine(directory, "page.txt"), Path.Combine(directory, "chapter", "page.txt")};
        await File.WriteAllTextAsync(files[0], "First gallery page.");
        await File.WriteAllTextAsync(files[1], "Second gallery page.");
        return files;
    }

    private async Task RecordProcessing(DownloadResultDbModel result, string directory, string json, bool ready = true)
    {
        _db.Set<DownloadResultProcessingDbModel>().Add(new()
        {
            DownloadResultId = result.Id, WorkflowRunId = 19, ContentsDirectory = directory,
            ContentsFilesJson = json, ContentsReadyAt = ready ? DateTime.UtcNow : null
        });
        await _db.SaveChangesAsync();
    }

    private async Task<byte[]> CreateMetadata(string name, string content)
    {
        var payload = Path.Combine(_root, name);
        await File.WriteAllTextAsync(payload, content);
        return (await new TorrentCreator(TorrentType.V1Only) {PieceLength = 16384}
            .CreateAsync(new TorrentFileSource(payload))).Encode();
    }
}
