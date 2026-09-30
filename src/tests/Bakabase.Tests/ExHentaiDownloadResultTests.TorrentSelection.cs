using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public async Task TorrentSelectionPrefersSourcesThenContentSizeAndSupportsMissingCounts(int scenario)
    {
        var candidates = scenario switch
        {
            // A larger dead swarm must not beat a smaller seeded one.
            0 => new[] {new TorrentCandidate(1, 9000, 0, 0), new TorrentCandidate(2, 1000, 1, 0)},
            // Among seeded swarms, larger content wins even with fewer seeds.
            1 => new[] {new TorrentCandidate(1, 1000, 50, 20), new TorrentCandidate(2, 2000, 1, 0)},
            // Active peers are useful even when the seed count is unavailable.
            2 => new[] {new TorrentCandidate(1, 9000, 0, 0), new TorrentCandidate(2, 1000, null, 3)},
            // Older windows without either field retain content-size preference.
            3 => new[] {new TorrentCandidate(1, 1000), new TorrentCandidate(2, 2000)},
            // A complete source wins over a larger peer-only swarm.
            4 => new[] {new TorrentCandidate(1, 9000, 0, 50), new TorrentCandidate(2, 1000, 1, 0)},
            // Peer-only swarms also prioritize content size over source count.
            5 => new[] {new TorrentCandidate(1, 1000, 0, 50), new TorrentCandidate(2, 2000, null, 1)},
            _ => throw new AssertFailedException("Unknown selection scenario.")
        };
        using var handler = new TorrentSelectionHandler(candidates, _metadata);
        using var producer = await BuildTorrentSelectionProducer(handler);

        await RunTorrentSelectionProducer(producer);

        CollectionAssert.AreEqual(new[] {2}, handler.Attempts.ToArray());
        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(DownloadResultKind.TorrentMetadata, result.Kind);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(result.Path));
    }

    [TestMethod]
    public async Task TorrentSelectionUsesStableSourceSizeAndMetadataTieBreakers()
    {
        var candidates = new[]
        {
            new TorrentCandidate(1, 500, 0, 0),
            new TorrentCandidate(2, 500, null, null),
            new TorrentCandidate(3, 400, null, 1),
            new TorrentCandidate(4, 400, 0, 2),
            new TorrentCandidate(5, 300, 1, 0, 100, 1),
            new TorrentCandidate(6, 300, 1, 0, 200, 2),
            new TorrentCandidate(7, 300, 1, 0, 200, 3),
            new TorrentCandidate(8, 300, 1, 1),
            new TorrentCandidate(9, 300, 2, 0),
            new TorrentCandidate(10, 1000, 1, 0),
            new TorrentCandidate(11, 500, null, null)
        };
        using var handler = new TorrentSelectionHandler(candidates, _metadata)
        {
            Respond = (_, _) => Task.FromResult(TorrentHtmlError())
        };
        using var producer = await BuildTorrentSelectionProducer(handler);

        await Assert.ThrowsExceptionAsync<AggregateException>(() => RunTorrentSelectionProducer(producer));

        CollectionAssert.AreEqual(new[] {10, 9, 8, 7, 6, 5, 4, 3, 1, 2, 11}, handler.Attempts.ToArray());
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    [DataRow("html")]
    [DataRow("http")]
    [DataRow("bencode")]
    [DataRow("engine")]
    [DataRow("timeout")]
    public async Task TorrentCandidateFailureTriesTheNextAndRecordsOnlyItsValidatedResult(string failure)
    {
        var candidates = new[] {new TorrentCandidate(1, 2000, 1, 0), new TorrentCandidate(2, 1000, 1, 0)};
        using var handler = new TorrentSelectionHandler(candidates, _metadata)
        {
            Respond = (candidate, _) => candidate.Id == 1
                ? Task.FromResult(failure switch
                {
                    "html" => TorrentHtmlError(),
                    "http" => new HttpResponseMessage(HttpStatusCode.NotFound),
                    "bencode" => new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent("invalid"u8.ToArray())},
                    // Valid bencode with an info dictionary, but unusable by the engine.
                    "engine" => new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent("d4:infod4:name1:xee"u8.ToArray())},
                    _ => throw new TaskCanceledException("The request timed out.", new TimeoutException())
                })
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(_metadata)})
        };
        using var producer = await BuildTorrentSelectionProducer(handler);
        var checkpoints = new List<string>();
        var completed = 0;
        var progress = new List<decimal>();

        await RunTorrentSelectionProducer(producer, checkpoint => {checkpoints.Add(checkpoint); return Task.CompletedTask;},
            () => {completed++; return Task.CompletedTask;}, value => {progress.Add(value); return Task.CompletedTask;});

        CollectionAssert.AreEqual(new[] {1, 2}, handler.Attempts.ToArray());
        var result = (await _results.GetByTaskAsync(10)).Single();
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(result.Path));
        CollectionAssert.AreEqual(new[] {"completed"}, checkpoints.ToArray());
        CollectionAssert.AreEqual(new[] {100m}, progress.ToArray());
        Assert.AreEqual(1, completed);
        Assert.AreEqual(1, Directory.GetFiles(_root, "*.torrent", SearchOption.AllDirectories)
            .Count(file => !file.StartsWith(Path.Combine(_root, "appdata"), StringComparison.Ordinal)));
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.download", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task AllTorrentCandidatesFailWithoutReplacingExistingFilesOrCompletingTheTask()
    {
        var candidates = new[] {new TorrentCandidate(1, 2000, 1, 0), new TorrentCandidate(2, 1000, 1, 0)};
        using var handler = new TorrentSelectionHandler(candidates, _metadata)
        {
            Respond = (_, _) => Task.FromResult(TorrentHtmlError())
        };
        using var producer = await BuildTorrentSelectionProducer(handler);
        var oldFile = Path.Combine(_root, "12345", "Selection gallery.torrent");
        Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
        await File.WriteAllBytesAsync(oldFile, _metadata);
        var completed = 0;

        var error = await Assert.ThrowsExceptionAsync<AggregateException>(() => RunTorrentSelectionProducer(producer,
            _ => throw new AssertFailedException("A failed candidate must not update the checkpoint."),
            () => {completed++; return Task.CompletedTask;}));

        CollectionAssert.AreEqual(new[] {1, 2}, handler.Attempts.ToArray());
        Assert.AreEqual(2, error.InnerExceptions.Count);
        Assert.IsTrue(error.InnerExceptions.All(inner => inner is InvalidDataException));
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(0, completed);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(oldFile));
        CollectionAssert.AreEqual(new[] {oldFile}, Directory.GetFiles(_root, "*.torrent", SearchOption.AllDirectories));
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.download", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task ASingleFailedTorrentCandidateRetainsItsOriginalExceptionType()
    {
        using var handler = new TorrentSelectionHandler([new TorrentCandidate(1, 1000, 1, 0)], _metadata)
        {
            Respond = (_, _) => Task.FromResult(TorrentHtmlError())
        };
        using var producer = await BuildTorrentSelectionProducer(handler);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => RunTorrentSelectionProducer(producer));

        CollectionAssert.AreEqual(new[] {1}, handler.Attempts.ToArray());
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task AllCandidateFailuresRetainAnEarlierTransientHttpFailureForTaskRetry()
    {
        var candidates = new[] {new TorrentCandidate(1, 2000, 1, 0), new TorrentCandidate(2, 1000, 1, 0)};
        using var handler = new TorrentSelectionHandler(candidates, _metadata)
        {
            Respond = (candidate, _) => Task.FromResult(candidate.Id == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : TorrentHtmlError())
        };
        using var producer = await BuildTorrentSelectionProducer(handler);

        var error = await Assert.ThrowsExceptionAsync<AggregateException>(() => RunTorrentSelectionProducer(producer));

        CollectionAssert.AreEqual(new[] {1, 2}, handler.Attempts.ToArray());
        Assert.AreEqual(HttpStatusCode.TooManyRequests, ((HttpRequestException)error.InnerExceptions[0]).StatusCode);
        Assert.IsInstanceOfType<InvalidDataException>(error.InnerExceptions[1]);
        Assert.IsTrue(TransientNetworkError.IsTransient(error));
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task ALocalTorrentWriteFailureDoesNotTryAnotherRemoteCandidate()
    {
        var candidates = new[] {new TorrentCandidate(1, 2000, 1, 0), new TorrentCandidate(2, 1000, 1, 0)};
        var downloadDirectory = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        using var handler = new TorrentSelectionHandler(candidates, _metadata)
        {
            Respond = async (_, _) =>
            {
                // After the directory has been resolved but before the downloaded bytes are
                // written, replace that directory with a file to provoke real filesystem I/O.
                Directory.Delete(downloadDirectory);
                await File.WriteAllTextAsync(downloadDirectory, "The output directory became unavailable.");
                return new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(_metadata)};
            }
        };
        using var producer = await BuildTorrentSelectionProducer(handler);

        await Assert.ThrowsAsync<IOException>(() => RunTorrentSelectionProducer(producer,
            downloadPath: downloadDirectory));

        CollectionAssert.AreEqual(new[] {1}, handler.Attempts.ToArray());
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
        Assert.IsTrue(File.Exists(downloadDirectory));
    }

    [TestMethod]
    public async Task CancelingTheFirstTorrentCandidateStopsBeforeTryingAnotherOrRecordingAResult()
    {
        using var cancellation = new CancellationTokenSource();
        var candidates = new[] {new TorrentCandidate(1, 2000, 1, 0), new TorrentCandidate(2, 1000, 1, 0)};
        using var handler = new TorrentSelectionHandler(candidates, _metadata)
        {
            Respond = (_, ct) =>
            {
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
                throw new AssertFailedException("Cancellation must interrupt the current candidate.");
            }
        };
        using var producer = await BuildTorrentSelectionProducer(handler);

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunTorrentSelectionProducer(producer,
            _ => throw new AssertFailedException("Cancellation must not complete a checkpoint."),
            () => throw new AssertFailedException("Cancellation must not report a torrent result."),
            ct: cancellation.Token));

        CollectionAssert.AreEqual(new[] {1}, handler.Attempts.ToArray());
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.torrent", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);
    }

    private async Task<ExHentaiSingleWorkDownloader> BuildTorrentSelectionProducer(TorrentSelectionHandler handler)
    {
        var provider = await TestServiceBuilder.BuildServiceProvider(services => services.AddSingleton(_results));
        provider.GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value.NamingConvention =
            "{GalleryId}/{PageTitle}{Extension}";
        var client = new ExHentaiClient(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);
        return new ExHentaiSingleWorkDownloader(provider, provider.GetRequiredService<IStringLocalizer<SharedResource>>(),
            client, provider.GetRequiredService<ITextVocabularyService>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
    }

    private Task RunTorrentSelectionProducer(ExHentaiSingleWorkDownloader producer,
        Func<string, Task>? checkpoint = null, Func<Task>? torrentCompleted = null,
        Func<decimal, Task>? progress = null, CancellationToken ct = default, string? downloadPath = null)
    {
        var method = typeof(AbstractExHentaiDownloader).GetMethod("DownloadSingleWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task) method.Invoke(producer, [10, "https://exhentai.org/g/12345/abcdef0123/", null, downloadPath ?? _root,
            (Func<string, Task>)(_ => Task.CompletedTask), (Func<string, Task>)(_ => Task.CompletedTask),
            progress ?? (_ => Task.CompletedTask), checkpoint ?? (_ => Task.CompletedTask), ct,
            true, false, null, null, torrentCompleted, null])!;
    }

    private static HttpResponseMessage TorrentHtmlError() => new(HttpStatusCode.OK)
        {Content = new StringContent("<html>The torrent file could not be found.</html>")};

    private sealed record TorrentCandidate(int Id, long Size, int? Seeds = null, int? Peers = null,
        long Added = 1770000000, int Downloaded = 1)
    {
        public string Hash => Id.ToString("x40", CultureInfo.InvariantCulture);
        public string Url => $"https://exhentai.org/torrent/12345/{Hash}.torrent";
    }

    private sealed class TorrentSelectionHandler(TorrentCandidate[] candidates, byte[] metadata) : HttpMessageHandler
    {
        public List<int> Attempts { get; } = new();
        public Func<TorrentCandidate, CancellationToken, Task<HttpResponseMessage>>? Respond { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.AbsoluteUri == ExHentaiClient.ApiUrl)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        gmetadata = new[]
                        {
                            new {gid = 12345, token = "abcdef0123", title = "Selection gallery", title_jpn = "",
                                category = "Misc", posted = 1770000000, filecount = 1, rating = 4.5,
                                torrentcount = candidates.Length, tags = Array.Empty<string>(),
                                torrents = candidates.Select(candidate => new
                                    {hash = candidate.Hash, fsize = candidate.Size, added = candidate.Added}).ToArray()}
                        }
                    }), Encoding.UTF8, "application/json")
                };
            if (uri.AbsolutePath == "/gallerytorrents.php")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Concat(candidates.Select(candidate =>
                        $"<form><table><tr><td>Size: 1 KiB</td><td>Downloads: {candidate.Downloaded}</td><td>Posted: 2026-09-01</td>" +
                        (candidate.Seeds.HasValue ? $"<td>Seeds: {candidate.Seeds}</td>" : "") +
                        (candidate.Peers.HasValue ? $"<td>Peers: {candidate.Peers}</td>" : "") +
                        $"</tr><tr><td>Description</td></tr><tr><td><a href='{candidate.Url}'>Download</a></td></tr></table></form>")))
                };
            var chosen = candidates.SingleOrDefault(candidate => candidate.Url == uri.AbsoluteUri);
            Assert.IsNotNull(chosen, "Candidate fallback must not switch to image downloads or unrelated requests.");
            Attempts.Add(chosen.Id);
            return Respond == null
                ? new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(metadata)}
                : await Respond(chosen, ct);
        }
    }
}
