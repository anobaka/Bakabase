using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    private const string RecoveryUrl = "https://exhentai.org/g/12345/abcdef0123/";

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteAvailableImages_AreReusedWithoutAnyNetworkRequests(bool preferOriginal)
    {
        var handler = new GalleryHandler {ImagesPerPage = 2};
        using var producer = await BuildProducer(handler, configure: preferOriginal ? OriginalOptions : null);
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false);
        var result = (await _results.GetByTaskAsync(10)).Single();
        var requests = handler.Requests;
        handler.RejectRequests = true;
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(requests, handler.Requests);
        Assert.AreEqual(result.Id, (await _results.GetByTaskAsync(10)).Single().Id);
        Assert.AreEqual(2, handler.ImageRequests, "Only the initial pass acquires image bytes.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingRecordedImages_RedownloadOnlyMissingPages_AndRepairTheSameResult(bool preferOriginal)
    {
        var handler = new GalleryHandler {ImagesPerPage = 2};
        using var producer = await BuildProducer(handler, configure: preferOriginal ? OriginalOptions : null);
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false);
        var original = (await _results.GetByTaskAsync(10)).Single();
        var files = JsonSerializer.Deserialize<string[]>(original.FilesJson)!;
        File.Delete(files[0]);
        handler.ImageRequests = 0;
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(1, handler.ImageRequests, "The surviving page must be retained.");
        Assert.IsTrue(files.All(File.Exists));
        Assert.AreEqual(original.Id, (await _results.GetByTaskAsync(10)).Single().Id);
    }

    [TestMethod]
    public async Task MovedImages_ReuseExactAvailableWorkflowContents_WithoutGalleryRequests()
    {
        var handler = new GalleryHandler();
        using var producer = await BuildProducer(handler, configure: OriginalOptions);
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false);
        var result = (await _results.GetByTaskAsync(10)).Single();
        var source = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        var finalDirectory = Path.Combine(_root, "library", "Gallery");
        Directory.CreateDirectory(finalDirectory);
        var placed = Path.Combine(finalDirectory, "001.jpg");
        File.Move(source, placed);
        _db.Set<DownloadResultProcessingDbModel>().Add(new()
        {
            DownloadResultId = result.Id, WorkflowRunId = 19, ContentsDirectory = finalDirectory,
            ContentsFilesJson = JsonSerializer.Serialize(new[] {placed}), ContentsReadyAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        handler.RejectRequests = true;
        string? accounted = null;
        producer.OnFileDownloaded += (path, _) => { accounted = path; return Task.CompletedTask; };
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(placed, accounted);
        Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(19, (await _db.Set<DownloadResultProcessingDbModel>().AsNoTracking().SingleAsync()).WorkflowRunId);
    }

    [TestMethod]
    public async Task DamagedUserTorrent_IsRestoredFromVerifiedCache_WithoutNetwork()
    {
        var original = Path.Combine(_root, "Gallery.torrent");
        await File.WriteAllBytesAsync(original, _metadata);
        var result = await _results.RecordTorrentAsync(10, ThirdParty, "12345/abcdef0123", "Gallery", original, null);
        await File.WriteAllTextAsync(original, "not a torrent");
        using var producer = await BuildProducer(new GalleryHandler {RejectRequests = true});
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(original));
        Assert.AreEqual(result.Id, (await _results.GetByTaskAsync(10)).Single().Id);
    }

    [TestMethod]
    public async Task MalformedTorrentOutputManifest_IsReacquiredAndRepaired()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask);
        var result = (await _results.GetByTaskAsync(10)).Single();
        await _db.DownloadResults.Where(x => x.Id == result.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FilesJson, "invalid-json"));
        handler.TorrentRequests = 0;
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask);
        Assert.AreEqual(1, handler.TorrentRequests);
        var repaired = (await _results.GetAsync(result.Id))!;
        Assert.IsTrue(JsonSerializer.Deserialize<string[]>(repaired.FilesJson)!.All(File.Exists));
    }

    [TestMethod]
    public async Task MissingManagedTorrent_RedownloadsAndRepairsMetadata_InsteadOfFalseSuccess()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask);
        var result = (await _results.GetByTaskAsync(10)).Single();
        File.Delete(result.Path);
        handler.TorrentRequests = 0;
        await RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask);
        Assert.AreEqual(1, handler.TorrentRequests);
        Assert.AreEqual(result.Id, (await _results.GetByTaskAsync(10)).Single().Id);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(result.Path));
    }

    [TestMethod]
    public async Task EmptyGalleryImageListing_CannotPersistACompletedResult()
    {
        var handler = new GalleryHandler {EmptyImageEntries = true};
        using var producer = await BuildProducer(handler);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            RunProducer(producer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [DataTestMethod]
    [DataRow("12347-", false, false)]
    [DataRow("12347-12345", false, false)]
    [DataRow("12347-", true, false)]
    [DataRow("12347-12345", true, false)]
    [DataRow("12347-", true, true)]
    [DataRow("12347-12345", true, true)]
    public async Task ListCheckpoint_RecoversMissingRecordedWork_WithoutDownloadingIntentionallySkippedWork(string checkpoint,
        bool torrentResult, bool damagedUserCopy)
    {
        var handler = new GalleryHandler {ListGalleryIds = [12347, 12346, 12345], TorrentBytes = torrentResult ? _metadata : null};
        using var firstProducer = await BuildProducer(handler);
        await RunProducer(firstProducer, RecoveryUrl, _ => Task.CompletedTask, preferTorrent: torrentResult);
        var lost = (await _results.GetByTaskAsync(10)).Single();
        var lostFile = torrentResult ? ExHentaiDownloadResultHelper.GetTorrentDownloadPath(lost)!
            : JsonSerializer.Deserialize<string[]>(lost.FilesJson)!.Single();
        if (damagedUserCopy) await File.WriteAllTextAsync(lostFile, "not a torrent");
        else File.Delete(lostFile);
        var valid = Path.Combine(_root, "already-downloaded.jpg");
        await File.WriteAllTextAsync(valid, "valid recorded work");
        await _results.RecordFilesAsync(10, ThirdParty, "12347/abcdef0123", "Valid", _root, [valid], null);
        handler.ImageRequests = 0;
        handler.TorrentRequests = 0;
        var provider = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton(_results));
        using var list = new ExHentaiListDownloader(provider,
            provider.GetRequiredService<IStringLocalizer<SharedResource>>(),
            new ExHentaiClient(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance),
            provider.GetRequiredService<ITextVocabularyService>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
        var method = typeof(ExHentaiListDownloader).GetMethod("StartCore",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        await (Task)method.Invoke(list, [new DownloadTask
        {
            Id = 10, Key = "https://exhentai.org/?f_search=test", DownloadPath = _root, Checkpoint = checkpoint
        }, new ExHentaiTaskOptions {PreferTorrent = torrentResult}, CancellationToken.None])!;
        Assert.AreEqual(torrentResult ? 0 : 1, handler.ImageRequests);
        Assert.AreEqual(0, handler.TorrentRequests, "A missing user copy must be restored from the valid metadata cache.");
        Assert.IsTrue(File.Exists(lostFile));
        if (torrentResult) CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(lostFile));
        var results = await _results.GetByTaskAsync(10);
        Assert.AreEqual(2, results.Count);
        Assert.IsFalse(results.Any(x => x.SourceKey == "12346/abcdef0123"));
        Assert.IsTrue(await _results.CanReuseAsync((await _results.GetAsync(lost.Id))!));
    }
}
