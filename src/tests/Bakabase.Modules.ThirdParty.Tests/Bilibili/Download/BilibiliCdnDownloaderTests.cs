using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili.Download;

[TestClass]
public class BilibiliCdnDownloaderTests
{
    // Signed-looking URLs: any leak of a query (deadline/upsig/oi) into a message or log line is caught.
    private const string Query = "?deadline=1700000000&gen=playurlv3&os=upos&oi=12345678&upsig=0123456789abcdef&uparams=e,deadline";
    private const string Base = "https://upos-sz-example.bilivideo.com/upgcxcode/00/00/5001/5001-1-30080.m4s" + Query;
    private const string Backup = "https://cn-example-ct-01-01.bilivideo.com/upgcxcode/00/00/5001/5001-1-30080.m4s" + Query;
    private const string Pcdn = "https://xy0x0x0x0xy.mcdn.bilivideo.cn:8082/v1/resource/5001-1-30080.m4s" + Query;
    private const string Fresh = "https://upos-sz-example2.bilivideo.com/upgcxcode/00/00/5001/5001-1-30080.m4s" + Query;
    private const string Identity = "5001-v80-c7";

    private string _dir = null!;
    private List<TimeSpan> _delays = null!;
    private CapturingLoggerFactory _logs = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bili-cdn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _delays = [];
        _logs = new CapturingLoggerFactory();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }

        // Nothing signed ever reaches a log line.
        foreach (var line in _logs.Lines)
        {
            BilibiliSecrets.AssertClean(line);
        }
    }

    private string Dest => Path.Combine(_dir, "v.m4s");
    private string Part => Dest + ".part";
    private string Sidecar => Dest + ".part.json";

    private BilibiliCdnDownloaderSettings Settings(Func<BilibiliCdnDownloaderSettings, BilibiliCdnDownloaderSettings>? tweak = null)
    {
        var settings = new BilibiliCdnDownloaderSettings
        {
            HeaderTimeout = TimeSpan.FromSeconds(2),
            StallTimeout = TimeSpan.FromMilliseconds(200),
            ProgressInterval = TimeSpan.Zero,
            BufferSize = 4096,
            Delay = (d, _) =>
            {
                lock (_delays)
                {
                    _delays.Add(d);
                }

                return Task.CompletedTask;
            },
        };
        return tweak?.Invoke(settings) ?? settings;
    }

    private BilibiliCdnDownloader Create(ScriptedCdn cdn, BilibiliCdnDownloaderSettings? settings = null,
        TimeProvider? time = null) =>
        new(new SingleClientFactory(new HttpClient(cdn)), settings ?? Settings(), time ?? TimeProvider.System,
            _logs.CreateLogger<BilibiliCdnDownloader>());

    private BilibiliCdnTransfer Transfer(IReadOnlyList<string> urls, Func<CancellationToken, Task<IReadOnlyList<string>?>>? refresh = null,
        string identity = Identity) =>
        new(Dest, identity, urls, refresh ?? (_ => Task.FromResult<IReadOnlyList<string>?>(urls)));

    private void WritePartial(byte[] bytes, string identity, long? total)
    {
        File.WriteAllBytes(Part, bytes);
        File.WriteAllText(Sidecar, JsonSerializer.Serialize(new {identity, total}));
    }

    private static T AssertThrows<T>(Func<Task> action) where T : Exception
    {
        try
        {
            action().GetAwaiter().GetResult();
        }
        catch (T e)
        {
            return e;
        }

        Assert.Fail($"Expected {typeof(T).Name}.");
        return null!;
    }

    [TestMethod]
    public async Task HappyPath_OneRequestWithCdnHeaders_FileMovedIntoPlace()
    {
        var content = Cdn.Payload(50_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None);

        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
        Assert.IsFalse(File.Exists(Part));
        Assert.IsFalse(File.Exists(Sidecar));
        var request = cdn.Requests.Single();
        Assert.AreEqual(Base, request.Url);
        Assert.IsNull(request.RangeFrom);
        Assert.AreEqual("https://www.bilibili.com/", request.Referer);
        Assert.AreEqual(InternalOptions.DefaultHttpUserAgent, request.UserAgent);
        Assert.IsFalse(request.HasCookie);
        Assert.AreEqual("identity", request.AcceptEncoding);
        Assert.IsTrue(_logs.Lines.Any(l => l.Contains(Identity) && l.Contains("finished")));
    }

    [TestMethod]
    public async Task ExistingDestination_NoRequest()
    {
        await File.WriteAllBytesAsync(Dest, [1, 2, 3]);
        var cdn = new ScriptedCdn();

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(0, cdn.Requests.Count);
    }

    [TestMethod]
    public async Task Resume_SendsRangeAndAppends()
    {
        var content = Cdn.Payload(40_000);
        WritePartial(content[..12_345], Identity, content.Length);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(12_345, cdn.Requests.Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task RangeIgnored_TruncatesAndRewrites()
    {
        var content = Cdn.Payload(30_000);
        WritePartial(content[..10_000], Identity, content.Length);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, ignoreRange: true));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(10_000, cdn.Requests.Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task ContentRangeStartMismatch_RestartsFromZero()
    {
        var content = Cdn.Payload(30_000);
        WritePartial(content[..10_000], Identity, content.Length);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, rangeStartOverride: 9_000), Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        var requests = cdn.RequestsTo(Base);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(10_000, requests[0].RangeFrom);
        Assert.IsNull(requests[1].RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task TotalMismatch_RestartsFromZero()
    {
        var content = Cdn.Payload(30_000);
        WritePartial(content[..10_000], Identity, content.Length);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, totalOverride: 31_000), Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.IsNull(cdn.RequestsTo(Base)[1].RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task SidecarIdentityMismatch_PartialDiscarded()
    {
        var content = Cdn.Payload(20_000);
        WritePartial(Cdn.Payload(5_000, seed: 99), "5001-v64-c7", content.Length);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.IsNull(cdn.Requests.Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task PartialWithoutSidecar_Discarded()
    {
        var content = Cdn.Payload(20_000);
        await File.WriteAllBytesAsync(Part, Cdn.Payload(5_000, seed: 3));
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.IsNull(cdn.Requests.Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task Forbidden_BackupUsed_BaseNotRetried()
    {
        var content = Cdn.Payload(20_000);
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Status(HttpStatusCode.Forbidden))
            .On(Backup, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None);

        Assert.AreEqual(1, cdn.RequestsTo(Base).Count);
        Assert.AreEqual(1, cdn.RequestsTo(Backup).Count);
        Assert.AreEqual(0, _delays.Count, "a dead URL is not waited for");
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task HtmlBody_IsADeadUrl()
    {
        var content = Cdn.Payload(20_000);
        var cdn = new ScriptedCdn().On(Pcdn, Cdn.Html()).On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Pcdn, Base]), null, CancellationToken.None);

        Assert.AreEqual(1, cdn.RequestsTo(Pcdn).Count);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    public void TransientStatusWithAnHtmlPage_IsRetried_ThenTransient(HttpStatusCode status)
    {
        // nginx/Tengine answer 5xx/429 with an HTML page: the status decides, not the page.
        var cdn = new ScriptedCdn().On(Base, Cdn.Html(status));

        var e = AssertThrows<BilibiliTemporarilyUnavailableException>(() =>
            Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None));

        Assert.AreEqual(BilibiliTemporaryFailureKind.CdnUnavailable, e.Kind);
        Assert.IsTrue(TransientNetworkError.IsTransient(e));
        Assert.AreEqual(9, cdn.RequestsTo(Base).Count, "3 attempts in each of 3 rounds");
        Assert.IsTrue(_delays.Count > 0, "retried with backoff");
    }

    [TestMethod]
    [DataRow("application/json")]
    [DataRow("text/plain")]
    public async Task ErrorBodyWithACorrectLength_IsADeadUrl(string mediaType)
    {
        var content = Cdn.Payload(20_000);
        var cdn = new ScriptedCdn()
            .On(Pcdn, Cdn.Bytes("""{"code":-403,"message":"forbidden"}"""u8.ToArray(), mediaType))
            .On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Pcdn, Base]), null, CancellationToken.None);

        Assert.AreEqual(1, cdn.RequestsTo(Pcdn).Count);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task EmptyBody_IsNeverComplete_AndKeepsThePartial()
    {
        var content = Cdn.Payload(20_000);
        WritePartial(content[..8_192], Identity, content.Length);
        // A 200 with Content-Length: 0 (which ignores the Range), then a proper answer.
        var cdn = new ScriptedCdn().On(Base, Cdn.Bytes([], "video/mp4"), Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        var requests = cdn.RequestsTo(Base);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(8_192, requests[1].RangeFrom, "the partial file survived the empty answer");
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public void EmptyBodyEveryTime_IsTransient_NeverAnEmptyFile()
    {
        var cdn = new ScriptedCdn().On(Base, Cdn.Bytes([], "video/mp4", announceLength: false));

        AssertThrows<BilibiliTemporarilyUnavailableException>(() =>
            Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None));

        Assert.IsFalse(File.Exists(Dest));
    }

    [TestMethod]
    public void AttemptsWithoutBytes_StillReportSignsOfLife()
    {
        var cdn = new ScriptedCdn().On(Base, Cdn.Status(HttpStatusCode.ServiceUnavailable));
        var reports = 0;
        var refreshes = 0;

        AssertThrows<BilibiliTemporarilyUnavailableException>(() => Create(cdn).DownloadAsync(Transfer([Base], _ =>
        {
            refreshes++;
            return Task.FromResult<IReadOnlyList<string>?>([Base]);
        }), (_, _) => Interlocked.Increment(ref reports), CancellationToken.None));

        // At least one per attempt, per wait and around each refresh, although no byte ever arrived.
        Assert.IsTrue(reports >= cdn.RequestsTo(Base).Count + _delays.Count + refreshes * 2,
            $"{reports} reports for {cdn.RequestsTo(Base).Count} attempts");
    }

    [TestMethod]
    public async Task AllForbidden_RefreshesThenSucceeds()
    {
        var content = Cdn.Payload(20_000);
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Status(HttpStatusCode.Forbidden))
            .On(Backup, Cdn.Status(HttpStatusCode.Forbidden))
            .On(Fresh, Cdn.Serve(content));
        var refreshes = 0;

        await Create(cdn).DownloadAsync(Transfer([Base, Backup], _ =>
        {
            refreshes++;
            return Task.FromResult<IReadOnlyList<string>?>([Fresh]);
        }), null, CancellationToken.None);

        Assert.AreEqual(1, refreshes);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public void AlwaysDead_AfterRefreshes_CdnDeadException_NotTransient()
    {
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Status(HttpStatusCode.Forbidden))
            .On(Backup, Cdn.Status(HttpStatusCode.NotFound));
        var refreshes = 0;

        var e = AssertThrows<BilibiliCdnDeadException>(() => Create(cdn).DownloadAsync(Transfer([Base, Backup], _ =>
        {
            refreshes++;
            return Task.FromResult<IReadOnlyList<string>?>([Base, Backup]);
        }), null, CancellationToken.None));

        Assert.AreEqual(2, refreshes);
        Assert.AreEqual(3, cdn.RequestsTo(Base).Count, "one try per round, three rounds");
        Assert.AreEqual(Identity, e.IdentityKey);
        Assert.AreEqual(404, e.HttpStatus);
        Assert.IsFalse(TransientNetworkError.IsTransient(e));
        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
    }

    [TestMethod]
    public void AlwaysNetworkFailures_TemporarilyUnavailable_Transient()
    {
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Status(HttpStatusCode.BadGateway))
            .On(Backup, Cdn.Throw(new HttpRequestException(HttpRequestError.ConnectionError,
                "Connection refused (upos-sz-example.bilivideo.com:443) " + Backup)));

        var e = AssertThrows<BilibiliTemporarilyUnavailableException>(() =>
            Create(cdn).DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None));

        Assert.AreEqual(BilibiliTemporaryFailureKind.CdnUnavailable, e.Kind);
        Assert.IsTrue(TransientNetworkError.IsTransient(e));
        // 3 rounds (initial + 2 refreshes) × 3 attempts per URL.
        Assert.AreEqual(9, cdn.RequestsTo(Base).Count);
        Assert.AreEqual(9, cdn.RequestsTo(Backup).Count);
        Assert.IsTrue(_delays.Count > 0);
        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
    }

    [TestMethod]
    public void MixedDeadAndNetwork_IsTransient()
    {
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Status(HttpStatusCode.Forbidden))
            .On(Backup, Cdn.Status(HttpStatusCode.ServiceUnavailable));

        var e = AssertThrows<BilibiliTemporarilyUnavailableException>(() =>
            Create(cdn, Settings(s => s with {MaxUrlRefreshes = 0}))
                .DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None));

        Assert.AreEqual(503, e.HttpStatus);
    }

    [TestMethod]
    public void RefreshReturnsNull_StreamChanged()
    {
        var cdn = new ScriptedCdn().On(Base, Cdn.Status(HttpStatusCode.Forbidden));

        var e = AssertThrows<BilibiliStreamChangedException>(() =>
            Create(cdn).DownloadAsync(Transfer([Base], _ => Task.FromResult<IReadOnlyList<string>?>(null)), null,
                CancellationToken.None));

        Assert.AreEqual(Identity, e.IdentityKey);
    }

    [TestMethod]
    public async Task DropMidBody_ResumesOnSameUrlWithRange()
    {
        var content = Cdn.Payload(40_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, failAt: 16_384), Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None);

        var requests = cdn.RequestsTo(Base);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(16_384, requests[1].RangeFrom);
        Assert.AreEqual(0, cdn.RequestsTo(Backup).Count);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task TenDropsEachWithProgress_StillSucceeds()
    {
        var content = Cdn.Payload(60_000);
        var responders = Enumerable.Range(1, 10).Select(i => Cdn.Serve(content, failAt: i * 5_000L))
            .Append(Cdn.Serve(content)).ToArray();
        var cdn = new ScriptedCdn().On(Base, responders);

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(11, cdn.RequestsTo(Base).Count);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task DropsWithoutProgress_AbandonUrlAfterBudget()
    {
        var content = Cdn.Payload(30_000);
        // Always breaks off at the same byte: the first attempt makes progress, the next three do not.
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Serve(content, failAt: 8_192))
            .On(Backup, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None);

        Assert.AreEqual(4, cdn.RequestsTo(Base).Count);
        Assert.AreEqual(8_192, cdn.RequestsTo(Backup).Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task Stall_IsRetried()
    {
        var content = Cdn.Payload(30_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, stallAt: 4_096), Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(4_096, cdn.RequestsTo(Base)[1].RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task ClockHoursAhead_UrlsAreStillTried()
    {
        // The URL's deadline (1700000000) is long past for this clock, and for the real one: it is never judged.
        var content = Cdn.Payload(10_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));

        await Create(cdn, time: new ShiftedTimeProvider(TimeSpan.FromHours(8)))
            .DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(1, cdn.Requests.Count);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public void WallClockCap_TransientAndPartialKept()
    {
        var content = Cdn.Payload(30_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, failAt: 8_192));
        var settings = Settings(s => s with {MaxStreamDuration = TimeSpan.FromMinutes(30)});
        // Every timestamp is 10 minutes after the previous one.
        var time = new ShiftedTimeProvider(TimeSpan.Zero, TimeSpan.FromMinutes(10));

        var e = AssertThrows<BilibiliTemporarilyUnavailableException>(() =>
            Create(cdn, settings, time).DownloadAsync(Transfer([Base]), null, CancellationToken.None));

        Assert.AreEqual(BilibiliTemporaryFailureKind.CdnUnavailable, e.Kind);
        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
    }

    [TestMethod]
    public async Task CancelledMidBody_PartialAndSidecarKept()
    {
        var content = Cdn.Payload(30_000);
        using var cts = new CancellationTokenSource();
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content, failAt: 8_192, onReachedFailPoint: cts.Cancel));

        try
        {
            await Create(cdn).DownloadAsync(Transfer([Base]), null, cts.Token);
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.AreEqual(8_192, new FileInfo(Part).Length);
        Assert.IsTrue(File.ReadAllText(Sidecar).Contains(Identity));
        Assert.IsFalse(File.Exists(Dest));
        Assert.AreEqual(1, cdn.Requests.Count);
    }

    [TestMethod]
    public async Task Progress_MonotonicAndEndsAtTotal()
    {
        var content = Cdn.Payload(50_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));
        var reports = new List<(long Done, long? Total)>();

        await Create(cdn).DownloadAsync(Transfer([Base]), (d, t) => reports.Add((d, t)), CancellationToken.None);

        Assert.IsTrue(reports.Count > 1);
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.IsTrue(reports[i].Done >= reports[i - 1].Done);
        }

        Assert.AreEqual((50_000L, (long?) 50_000L), reports[^1]);
    }

    [TestMethod]
    public async Task RangeNotSatisfiable_WithCompletePartial_Completes()
    {
        var content = Cdn.Payload(20_000);
        // Total unknown in the sidecar (the first answer had no length): the CDN's 416 settles it.
        WritePartial(content, Identity, null);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(20_000, cdn.Requests.Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task CompletePartialWithKnownTotal_NoRequest()
    {
        var content = Cdn.Payload(20_000);
        WritePartial(content, Identity, content.Length);
        var cdn = new ScriptedCdn();

        await Create(cdn).DownloadAsync(Transfer([Base]), null, CancellationToken.None);

        Assert.AreEqual(0, cdn.Requests.Count);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public async Task ResumeAcrossUrlRefresh()
    {
        var content = Cdn.Payload(40_000);
        var cdn = new ScriptedCdn()
            .On(Base, Cdn.Serve(content, failAt: 12_288), Cdn.Status(HttpStatusCode.Forbidden))
            .On(Fresh, Cdn.Serve(content));

        await Create(cdn).DownloadAsync(
            Transfer([Base], _ => Task.FromResult<IReadOnlyList<string>?>([Fresh])), null, CancellationToken.None);

        Assert.AreEqual(12_288, cdn.RequestsTo(Fresh).Single().RangeFrom);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Dest));
    }

    [TestMethod]
    public void DiskFullWhileWriting_DiskWriteException_NotRetried()
    {
        var content = Cdn.Payload(40_000);
        var cdn = new ScriptedCdn().On(Base, Cdn.Serve(content)).On(Backup, Cdn.Serve(content));
        var settings = Settings(s => s with
        {
            OpenPartFile = (path, append) =>
                new DiskFullStream(new FileStream(path, append ? FileMode.Append : FileMode.Create), 10_000),
        });

        var e = AssertThrows<DiskWriteException>(() =>
            Create(cdn, settings).DownloadAsync(Transfer([Base, Backup]), null, CancellationToken.None));

        Assert.IsTrue(e.IsDiskFull);
        Assert.IsInstanceOfType<IUserActionableException>(e);
        Assert.IsFalse(TransientNetworkError.IsTransient(e));
        Assert.AreEqual(1, cdn.Requests.Count, "a disk error is never retried");
    }

    [TestMethod]
    public async Task GetSmall_ReturnsBytesAndEncoding_NullOn404()
    {
        var body = new byte[] {1, 2, 3, 4};
        var danmaku = "https://comment.bilibili.com/5001.xml";
        var missing = "https://comment.bilibili.com/5002.xml";
        var cdn = new ScriptedCdn()
            .On(danmaku, Cdn.Bytes(body, "text/xml", "deflate"))
            .On(missing, Cdn.Status(HttpStatusCode.NotFound));
        var downloader = Create(cdn);

        var small = await downloader.GetSmallAsync(danmaku, CancellationToken.None);
        Assert.IsNotNull(small);
        CollectionAssert.AreEqual(body, small.Bytes);
        CollectionAssert.AreEqual(new[] {"deflate"}, small.ContentEncodings.ToArray());
        Assert.AreEqual("text/xml", small.MediaType);
        Assert.IsNull(await downloader.GetSmallAsync(missing, CancellationToken.None));
        var request = cdn.RequestsTo(danmaku).Single();
        Assert.IsFalse(request.HasCookie);
        Assert.AreEqual("https://www.bilibili.com/", request.Referer);
    }

    [TestMethod]
    public void GetSmall_CapEnforced_EvenWithoutContentLength()
    {
        const string subtitle = "https://aisubtitle.hdslb.com/bfs/subtitle/abc.json?auth_key=1-2-0-3";
        var cdn = new ScriptedCdn().On(subtitle, Cdn.Bytes(new byte[5_000], announceLength: false));

        var e = AssertThrows<InvalidDataException>(() =>
            Create(cdn, Settings(s => s with {MaxSmallBodyBytes = 1_000})).GetSmallAsync(subtitle, CancellationToken.None));

        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
    }

    [TestMethod]
    public void GetSmall_Failures_AreRedactedHttpRequestExceptions()
    {
        const string subtitle = "https://aisubtitle.hdslb.com/bfs/subtitle/abc.json?auth_key=1-2-0-3";
        var cdn = new ScriptedCdn().On(subtitle, Cdn.Status(HttpStatusCode.Forbidden),
            Cdn.Throw(new HttpRequestException("socket closed while GET " + subtitle)),
            Cdn.Serve(new byte[100_000], stallAt: 0));
        var downloader = Create(cdn);

        var forbidden = AssertThrows<HttpRequestException>(() => downloader.GetSmallAsync(subtitle, CancellationToken.None));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var network = AssertThrows<HttpRequestException>(() => downloader.GetSmallAsync(subtitle, CancellationToken.None));
        var stalled = AssertThrows<HttpRequestException>(() => downloader.GetSmallAsync(subtitle, CancellationToken.None));

        foreach (var e in new Exception[] {forbidden, network, stalled})
        {
            BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
        }
    }

    [TestMethod]
    public void ExceptionsFromNetworkFailures_CarryOnlyRedactedUrls()
    {
        var cdn = new ScriptedCdn().On(Base, Cdn.Throw(new HttpRequestException("failed for " + Base)));

        var e = AssertThrows<BilibiliTemporarilyUnavailableException>(() =>
            Create(cdn, Settings(s => s with {MaxUrlRefreshes = 0}))
                .DownloadAsync(Transfer([Base]), null, CancellationToken.None));

        Assert.IsInstanceOfType<BilibiliCdnTransferException>(e.InnerException);
        StringAssert.Contains(e.ToString(), BilibiliCdnUrls.Redact(Base));
        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
    }
}
