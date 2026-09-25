using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Media;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Tests.Bilibili.Shared;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Bakabase.TestKit.Implementations;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Bakabase.Tests.Bilibili;

/// <summary>
/// The favorites downloader end to end, through the real DI graph: both Bilibili HTTP clients are faked at their
/// primary handler (which also takes the rate limiter out), ffmpeg is a stub merger.
/// </summary>
[TestClass]
public sealed class BilibiliDownloaderTests
{
    private const string FolderId = "100";
    private const string VideoFile = "5001-1-100026-80.m4s";
    private const string AudioFile = "5001-1-30280.m4s";
    private const string DurlFile = "25540578-1-16.mp4";
    private const string Quality = "1080P 高清";

    private sealed class TestBilibiliDownloader(
        IServiceProvider serviceProvider,
        BilibiliClient client,
        BilibiliVideoDownloadService videoService,
        FfMpegService ffMpegService,
        IDownloaderLocalizer localizer)
        : BilibiliDownloader(serviceProvider, client, videoService, ffMpegService, localizer)
    {
        public readonly ConcurrentQueue<TimeSpan> Waits = new();
        public Func<TimeSpan, CancellationToken, Task> Wait = (_, _) => Task.CompletedTask;
        public TimeSpan StepRefresh = TimeSpan.Zero;

        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct)
        {
            Waits.Enqueue(delay);
            return Wait(delay, ct);
        }

        protected override TimeSpan TransientRetryStepRefreshInterval => StepRefresh;
    }

    private FakeBilibiliHttp _http = null!;
    private StubMerger _merger = null!;
    private CapturingLoggerProvider _logs = null!;
    private IServiceProvider _services = null!;
    private ControllableFfMpegService _ffMpeg = null!;
    private string _root = null!;
    private CultureInfo _uiCulture = null!;
    private CultureInfo _culture = null!;

    [ClassInitialize]
    public static void RegisterTestDownloader(TestContext _)
    {
        // The real definition (naming convention, fields, helper), only the type differs.
        DownloaderInternals.DownloaderTypeDefinitionMap[typeof(TestBilibiliDownloader)] =
            DownloaderInternals.DownloaderTypeDefinitionMap[typeof(BilibiliDownloader)] with
            {
                DownloaderType = typeof(TestBilibiliDownloader),
            };
    }

    // Synchronous on purpose: the cultures are async-local, so a culture set in an async initializer is undone
    // when it returns and the tests would run (and assert the English texts) under the machine's culture.
    [TestInitialize]
    public void Setup()
    {
        _uiCulture = CultureInfo.CurrentUICulture;
        _culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en-US");
        _root = Path.Combine(Path.GetTempPath(), "BakabaseBilibiliTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _http = new FakeBilibiliHttp();
        _http.Cdn.ServeDanmakuAndCovers();
        _merger = new StubMerger();
        _logs = new CapturingLoggerProvider();
        _services = TestServiceBuilder.BuildServiceProvider(services =>
        {
            // As the app host configures it (AppHost / AppUtils), so the real resx texts are used.
            services.AddLocalization(o => o.ResourcesPath = "Resources");
            services.AddLogging(b => b
                .SetMinimumLevel(LogLevel.Trace)
                .AddFilter<ConsoleLoggerProvider>(level => level >= LogLevel.Warning)
                .AddProvider(_logs));

            foreach (var (name, handler) in new[]
                     {
                         (InternalOptions.HttpClientNames.Bilibili, _http.ApiHandler),
                         (InternalOptions.HttpClientNames.BilibiliCdn, _http.CdnHandler),
                     })
            {
                // Drop the production handlers (cookie, rate limiter, proxy) before adding the fakes.
                services.Configure<HttpClientFactoryOptions>(name, o => o.HttpMessageHandlerBuilderActions.Clear());
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => handler);
            }

            services.Replace(ServiceDescriptor.Singleton<IMediaMerger>(_merger));
            services.Replace(ServiceDescriptor.Singleton<FfMpegService>(sp =>
                ActivatorUtilities.CreateInstance<ControllableFfMpegService>(sp)));
            services.Replace(ServiceDescriptor.Singleton(new BilibiliCdnDownloaderSettings
            {
                Delay = (_, _) => Task.CompletedTask,
                HeaderTimeout = TimeSpan.FromSeconds(5),
                StallTimeout = TimeSpan.FromSeconds(5),
                ProgressInterval = TimeSpan.FromMilliseconds(20),
            }));
            // A downloader that read the account cookie itself could leak it; the sentinel would show.
            services.Replace(ServiceDescriptor.Singleton<IBOptionsManager<BilibiliOptions>>(
                new TestBOptionsManager<BilibiliOptions>(new BilibiliOptions {Cookie = FakeBilibiliHttp.Cookie})));
        }).GetAwaiter().GetResult();
        _ffMpeg = (ControllableFfMpegService) _services.GetRequiredService<FfMpegService>();

        _http.Api
            .On(FakeApi.MyInfo, "myinfo-16digit.json")
            .On(FakeApi.Folders, BilibiliJson.Folders(12))
            .On(FakeApi.Naming, "playurl-16-naming.json")
            .On(FakeApi.PlayUrl, "playurl-durl-mp4.json")
            .On(FakeApi.DmView, "dmview-null.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.CurrentCulture = _culture;
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    #region Harness

    private TestBilibiliDownloader NewDownloader() =>
        ActivatorUtilities.CreateInstance<TestBilibiliDownloader>(_services);

    private DownloadTask NewTask(string? checkpoint = null, string key = FolderId) => new()
    {
        Id = 1,
        Key = key,
        Name = "默认收藏夹",
        ThirdPartyId = ThirdPartyId.Bilibili,
        Type = (int) BilibiliDownloadTaskType.Favorites,
        DownloadPath = _root,
        Checkpoint = checkpoint,
    };

    private sealed record Run(TestBilibiliDownloader Downloader, List<string> Checkpoints, List<string?> Steps);

    private async Task<Run> RunAsync(DownloadTask task, Action<TestBilibiliDownloader>? configure = null)
    {
        var downloader = NewDownloader();
        configure?.Invoke(downloader);
        var run = new Run(downloader, [], []);
        downloader.OnCheckpointChanged += cp =>
        {
            lock (run.Checkpoints)
            {
                run.Checkpoints.Add(cp);
            }

            return Task.CompletedTask;
        };
        downloader.OnCurrentChanged += () =>
        {
            lock (run.Steps)
            {
                run.Steps.Add(downloader.Current);
            }

            return Task.CompletedTask;
        };
        Assert.IsTrue(await downloader.Start(task));
        await WaitUntilSettled(downloader);
        AssertNothingLeaked(downloader);
        return run;
    }

    private static async Task WaitUntilSettled(TestBilibiliDownloader downloader, int seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (downloader.Status is DownloaderStatus.Starting or DownloaderStatus.Downloading
               or DownloaderStatus.Stopping)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The downloader is still {downloader.Status} ({downloader.Current}).");
            }

            await Task.Delay(10);
        }
    }

    private string ExpectedPath(string bvId, string title, int partNo, string partName) =>
        Path.Combine(_root, BilibiliJson.UploaderName, $"[{bvId}]{title}", $"{partNo}.{partName}.{Quality}.mp4");

    private static readonly string[] Secrets =
        [FakeBilibiliHttp.CookieSentinel, "SESSDATA", "upsig=", "deadline=", "oi=", "auth_key="];

    /// <summary>
    /// Cookie hygiene: the cookie only ever travels with API requests through the API client, every other host is
    /// reached through the CDN client, and neither the cookie nor a signed URL shows up in a log line or a
    /// task message.
    /// </summary>
    private void AssertNothingLeaked(TestBilibiliDownloader downloader)
    {
        foreach (var hit in _http.Hits)
        {
            var host = new Uri(hit.Url).Host;
            if (host == "api.bilibili.com")
            {
                Assert.AreEqual(InternalOptions.HttpClientNames.Bilibili, hit.Client, hit.Url);
            }
            else
            {
                Assert.AreEqual(InternalOptions.HttpClientNames.BilibiliCdn, hit.Client, $"{host} via the API client");
                Assert.IsFalse(hit.HasCookie, $"cookie sent to {host}");
            }
        }

        foreach (var text in _logs.Lines.Append(downloader.Message ?? "").Append(downloader.Current ?? ""))
        {
            foreach (var secret in Secrets)
            {
                Assert.IsFalse(text.Contains(secret, StringComparison.OrdinalIgnoreCase),
                    $"'{secret}' leaked: {text}");
            }
        }
    }

    private IReadOnlyList<long?> CidsRequested(string kind) =>
        _http.Api.Requests.Where(r => r.Kind == kind).Select(r => FakeApi.QueryLong(new Uri(r.Url), "cid")).ToList();

    #endregion

    [TestMethod]
    public async Task AMixedFolderDownloadsVideosAndNotesEverySkip()
    {
        _http.Api
            .OnListPage(1, "fav-items-mixed.json")
            .On(FakeApi.View, 1001, "view-ok-2pages.json")
            .On(FakeApi.PlayUrl, 1001, "playurl-dash.json")
            .On(FakeApi.View, 1004, "view-403.json")
            .On(FakeApi.PageList, 1004, "pagelist-ok.json")
            .On(FakeApi.View, 1005, BilibiliJson.View(1005, true, (6005, "P1")))
            .On(FakeApi.View, 1006, BilibiliJson.View(1006, false, (6006, "P1")))
            .On(FakeApi.View, 1007, BilibiliJson.View(1007, false, (6007, "P1")));

        var run = await RunAsync(NewTask());
        var downloader = run.Downloader;

        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status, downloader.Message);
        foreach (var expected in new[]
                 {
                     ExpectedPath("BV1xx411c7m1", "正常视频", 1, "第一集"),
                     ExpectedPath("BV1xx411c7m1", "正常视频", 2, "第二集"),
                     ExpectedPath("BV1xx411c7m4", "登录可见的老视频", 1, "第一集"),
                     ExpectedPath("BV1xx411c7m4", "登录可见的老视频", 2, "第二集"),
                     ExpectedPath("BV1xx411c7m6", "缺少类型字段", 1, "P1"),
                     // The title says "invalid video", the attr says otherwise: the attr wins.
                     ExpectedPath("BV1xx411c7m7", "已失效视频", 1, "P1"),
                 })
        {
            Assert.IsTrue(File.Exists(expected), $"missing {expected}");
            Assert.IsTrue(File.Exists(Path.ChangeExtension(expected, ".xml")), "danmaku next to the video");
            Assert.IsTrue(File.Exists(Path.ChangeExtension(expected, ".jpg")), "cover next to the video");
        }

        // The DASH pages were muxed from the chosen video and audio streams.
        var p1 = ExpectedPath("BV1xx411c7m1", "正常视频", 1, "第一集");
        CollectionAssert.AreEqual(FakeCdn.ContentOf(VideoFile).Concat(FakeCdn.ContentOf(AudioFile)).ToArray(),
            File.ReadAllBytes(p1));
        Assert.AreEqual(2, _merger.Calls.Count(c => c.Operation == "mux"));
        Assert.AreEqual(FakeCdn.DanmakuXml, File.ReadAllText(Path.ChangeExtension(p1, ".xml")));

        // Non-video items and invalid/interactive videos were never looked up.
        foreach (var aid in new long[] {1002, 1009, 1003, 321808, 281830, 518946, 7001, 9901})
        {
            Assert.AreEqual(0, _http.Api.Requests.Count(r => r.Aid == aid), $"request made for item {aid}");
        }

        var lines = downloader.Message!.Split('\n');
        Assert.AreEqual("9 note(s) from this task:", lines[0], downloader.Message);
        Assert.AreEqual(
            "Skipped items are not retried automatically, and the log lists every one. To check them again, clear this task's checkpoint.",
            lines[^1]);
        CollectionAssert.AreEquivalent(new[]
        {
            "- [BV1xx411c7m2] 已失效视频 — skipped: no longer available (removed or invalidated)",
            "- [BV1xx411c7m9] 被UP主删除 — skipped: no longer available (removed or invalidated)",
            "- [BV1xx411c7m3] 互动视频 — skipped: interactive videos are not supported",
            "- [BV1xx411c7m5] PGC稿件 — skipped: supporter-only (充电专属) video; this account has no access",
            "- [BV1xx411c7m8] 番剧一集 — skipped: bangumi/film episodes are not supported yet",
            "- [BV1xx411c7m0] 已下架番剧 — skipped: no longer available (removed or invalidated)",
            "- [BV1xx411c7m6] 一首歌 — skipped: audio items are not supported yet",
            "- [BV1xx411c7m1] 一个合集 — skipped: video collections are not supported yet",
            "- [BV1xx411c7m1] 未知类型 — skipped: unsupported favorites item type 99",
        }, lines[1..^1]);

        Assert.AreEqual("1001-", run.Checkpoints[^1]);
        Assert.IsTrue(run.Checkpoints.Take(run.Checkpoints.Count - 1).All(c => !c.EndsWith('-')));
        Assert.AreEqual("https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid=3546571234567890&jsonp=jsonp",
            _http.Api.Requests.Single(r => r.Kind == FakeApi.Folders).Url, "16-digit mids survive");
        Assert.IsNull(downloader.Current);
    }

    [TestMethod]
    public async Task APageAlreadyOnDiskUnderItsLegacyNameCostsOneRequestAndIsNoNotice()
    {
        _http.Api
            .OnListPage(1, BilibiliJson.List(false, 1, BilibiliJson.Item(1001, "正常视频", bvid: "BV1xx411c7m1")))
            .On(FakeApi.View, 1001, "view-ok-2pages.json");
        var existing = ExpectedPath("BV1xx411c7m1", "正常视频", 1, "第一集");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "downloaded by the old downloader");

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Complete, run.Downloader.Status, run.Downloader.Message);
        Assert.IsNull(run.Downloader.Message, "an existing file is not a notice");
        Assert.AreEqual("downloaded by the old downloader", await File.ReadAllTextAsync(existing));
        CollectionAssert.AreEqual(new long?[] {5001, 5002}, CidsRequested(FakeApi.Naming).ToList());
        CollectionAssert.AreEqual(new long?[] {5002}, CidsRequested(FakeApi.PlayUrl).ToList(),
            "no playurl request for the page on disk");
        Assert.AreEqual(0, _http.Cdn.Count("5001.xml"));
        Assert.AreEqual(1, _http.Cdn.Count(DurlFile));
        Assert.AreEqual(0, _merger.Calls.Count, "a single mp4 segment is moved, not merged");
        Assert.IsTrue(File.Exists(ExpectedPath("BV1xx411c7m1", "正常视频", 2, "第二集")));
    }

    [TestMethod]
    public async Task RiskControlWaitsTenThenThirtyMinutesThenFailsWithoutPassingTheItem()
    {
        _http.Api
            .OnListPage(1, BilibiliJson.List(false, 3,
                BilibiliJson.Item(2001, "第一个"),
                BilibiliJson.Item(2002, "失效的", attr: 1),
                BilibiliJson.Item(2003, "被风控")))
            .On(FakeApi.View, 2001, BilibiliJson.View(2001, false, (7001, "P1")))
            .On(FakeApi.View, 2003, BilibiliJson.View(2003, false, (7003, "P1")))
            .On(FakeApi.PlayUrl, 2003, "playurl-352.json");

        const string tenMinuteStep = "Bilibili is refusing requests for now (risk control); retrying in 10 min (1/2)";
        // The 10-minute wait ends once its step has been shown and refreshed, however slow the machine is.
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tenMinuteSteps = 0;
        var run = await RunAsync(NewTask(), d =>
        {
            d.StepRefresh = TimeSpan.FromMilliseconds(30);
            d.OnCurrentChanged += () =>
            {
                if (d.Current == tenMinuteStep && Interlocked.Increment(ref tenMinuteSteps) >= 2)
                {
                    refreshed.TrySetResult();
                }

                return Task.CompletedTask;
            };
            d.Wait = (delay, ct) => delay == TimeSpan.FromMinutes(10)
                ? refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10), ct)
                : Task.CompletedTask;
        });
        var downloader = run.Downloader;

        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);
        CollectionAssert.AreEqual(new[] {TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)}, downloader.Waits.ToList());
        Assert.AreEqual(3, _http.Api.Count(FakeApi.PlayUrl, 2003), "one run plus two re-runs");
        StringAssert.Contains(downloader.Message!, "Bilibili is temporarily refusing requests (risk control, code -352)");
        StringAssert.Contains(downloader.Message!,
            "- [BV1test2002] 失效的 — skipped: no longer available (removed or invalidated)");
        Assert.AreEqual("2001-2002", run.Checkpoints[^1]);
        Assert.IsFalse(run.Checkpoints.Any(c => c.Contains("2003")));
        Assert.AreEqual(1, _http.Cdn.Count(DurlFile), "the finished item is not fetched again by the re-runs");
        Assert.AreEqual(1, _http.Api.Count(FakeApi.List), "list pages are kept across re-runs");
        // The wait is shown, refreshed while it lasts (a sign of life for the queue watchdog).
        Assert.IsTrue(run.Steps.Count(s => s == tenMinuteStep) >= 2, string.Join(" | ", run.Steps));
        Assert.IsTrue(run.Steps.Contains("Bilibili is refusing requests for now (risk control); retrying in 30 min (2/2)"));
    }

    [TestMethod]
    public async Task ATransientCdnFailureReRunsAndResumesThePartialStream()
    {
        const int prefix = 16 * 1024;
        _http.Api
            .OnListPage(1, BilibiliJson.List(false, 1, BilibiliJson.Item(3001, "断线重连")))
            .On(FakeApi.View, 3001, BilibiliJson.View(3001, false, (8001, "P1")))
            .On(FakeApi.PlayUrl, 3001, "playurl-dash.json");
        var healthy = false;
        var broken = 0;
        _http.Cdn.File(VideoFile, (request, ct) =>
        {
            if (healthy)
            {
                return null;
            }

            if (Interlocked.Increment(ref broken) == 1)
            {
                var content = FakeCdn.ContentOf(VideoFile);
                return FakeCdn.Serve(request, content,
                    new ScriptedBody(content, prefix, ScriptedBody.Mode.BreakOff, ct));
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {Content = new ByteArrayContent([])};
        });

        var run = await RunAsync(NewTask(), d => d.Wait = (_, _) =>
        {
            healthy = true;
            return Task.CompletedTask;
        });

        Assert.AreEqual(DownloaderStatus.Complete, run.Downloader.Status, run.Downloader.Message);
        CollectionAssert.AreEqual(new[] {TimeSpan.FromSeconds(30)}, run.Downloader.Waits.ToList());
        var videoHits = _http.Hits.Where(h => FakeCdn.FileNameOf(h.Url) == VideoFile).ToList();
        Assert.AreEqual(prefix, videoHits.Last().RangeFrom, "the re-run resumes the partial stream");
        var target = ExpectedPath(BilibiliJson.BvId(3001), "断线重连", 1, "P1");
        CollectionAssert.AreEqual(FakeCdn.ContentOf(VideoFile).Concat(FakeCdn.ContentOf(AudioFile)).ToArray(),
            File.ReadAllBytes(target));
    }

    [TestMethod]
    public async Task AMissingFolderFailsWithTheLocalizedReason()
    {
        var run = await RunAsync(NewTask(key: "999"));

        Assert.AreEqual(DownloaderStatus.Failed, run.Downloader.Status);
        StringAssert.Contains(run.Downloader.Message!,
            "Bilibili favorites folder 999 (默认收藏夹) was not found in this account.");
        StringAssert.Contains(run.Downloader.Message!, nameof(BilibiliFavoritesNotFoundException));
        Assert.IsTrue(typeof(Bakabase.Abstractions.Exceptions.IUserActionableException)
            .IsAssignableFrom(typeof(BilibiliFavoritesNotFoundException)));
        Assert.AreEqual(0, run.Downloader.Waits.Count);
        Assert.AreEqual(0, _http.Api.Count(FakeApi.List));
    }

    [TestMethod]
    public async Task AnExpiredLoginFailsInsteadOfSkippingAndKeepsTheCheckpoint()
    {
        _http.Api
            .OnListPage(1, BilibiliJson.List(false, 2, BilibiliJson.Item(4001, "A"), BilibiliJson.Item(4002, "B")))
            .On(FakeApi.View, 4001, "view-101.json");

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Failed, run.Downloader.Status);
        StringAssert.Contains(run.Downloader.Message!, "The Bilibili cookie is not logged in or has expired.");
        Assert.AreEqual(0, run.Checkpoints.Count);
        Assert.AreEqual(0, run.Downloader.Waits.Count);
        Assert.AreEqual(0, _http.Api.Count(FakeApi.View, 4002));
    }

    [TestMethod]
    public async Task StoppingMidDownloadKeepsThePartialFileAndMergesNothing()
    {
        _http.Api
            .OnListPage(1, BilibiliJson.List(false, 1, BilibiliJson.Item(5001, "停止")))
            .On(FakeApi.View, 5001, BilibiliJson.View(5001, false, (9001, "P1")))
            .On(FakeApi.PlayUrl, 5001, "playurl-dash.json");
        var hanging = new TaskCompletionSource<ScriptedBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Cdn.File(VideoFile, (request, ct) =>
        {
            var content = FakeCdn.ContentOf(VideoFile);
            var body = new ScriptedBody(content, 8192, ScriptedBody.Mode.HangUntilCancelled, ct);
            body.PrefixDelivered.Task.ContinueWith(_ => hanging.TrySetResult(body), TaskScheduler.Default);
            return FakeCdn.Serve(request, content, body);
        });

        var downloader = NewDownloader();
        Assert.IsTrue(await downloader.Start(NewTask()));
        await hanging.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await downloader.Stop(DownloaderStopBy.ManuallyStop);
        await WaitUntilSettled(downloader);

        Assert.AreEqual(DownloaderStatus.Stopped, downloader.Status);
        var partials = Directory.GetFiles(Path.Combine(_root, "temp", FolderId), "*.part", SearchOption.AllDirectories);
        Assert.IsTrue(partials.Any(p => new FileInfo(p).Length > 0), "the partial stream is kept for the next run");
        Assert.AreEqual(0, _merger.Calls.Count);
        AssertNothingLeaked(downloader);
    }

    [TestMethod]
    public async Task BytesMovingKeepTheWatchdogAwakeWhenTheFolderPercentageDoesNot()
    {
        // 100 000 items: one page is far below 0.01 % of the folder.
        _http.Api
            .On(FakeApi.Folders, BilibiliJson.Folders(100_000))
            .OnListPage(1, BilibiliJson.List(false, 100_000, BilibiliJson.Item(6001, "大收藏夹")))
            .On(FakeApi.View, 6001, BilibiliJson.View(6001, false, (9101, "P1")))
            .On(FakeApi.PlayUrl, 6001, "playurl-dash.json");
        var trickling = new TaskCompletionSource<ScriptedBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Cdn.File(VideoFile, (request, ct) =>
        {
            var content = FakeCdn.ContentOf(VideoFile);
            var body = new ScriptedBody(content, 4096, ScriptedBody.Mode.TrickleUntilReleased, ct);
            body.PrefixDelivered.Task.ContinueWith(_ => trickling.TrySetResult(body), TaskScheduler.Default);
            return FakeCdn.Serve(request, content, body);
        });

        var downloader = NewDownloader();
        var progressReports = 0;
        downloader.OnProgress += _ =>
        {
            Interlocked.Increment(ref progressReports);
            return Task.CompletedTask;
        };
        Assert.IsTrue(await downloader.Start(NewTask()));
        var body = await trickling.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(200);
        var before = downloader.LastActivityAt;
        var reportsBefore = Volatile.Read(ref progressReports);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (downloader.LastActivityAt <= before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        var touched = downloader.LastActivityAt > before;
        var reportsWhileTrickling = Volatile.Read(ref progressReports) - reportsBefore;
        body.Release();
        await WaitUntilSettled(downloader);

        Assert.IsTrue(touched, "the transfer did not count as a sign of life");
        Assert.AreEqual(0, reportsWhileTrickling, "the folder-wide percentage did not change");
        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status, downloader.Message);
    }

    [TestMethod]
    public async Task EmptyPagesWithMoreToComeAreNotTheEnd()
    {
        _http.Api
            .On(FakeApi.Folders, BilibiliJson.Folders(2))
            .OnListPage(1, BilibiliJson.List(true, 2, BilibiliJson.Item(7101, "第一页")))
            .OnListPage(2, BilibiliJson.List(true, 2))
            .OnListPage(3, BilibiliJson.List(true, 2))
            .OnListPage(4, BilibiliJson.List(true, 2))
            .OnListPage(5, BilibiliJson.List(false, 2, BilibiliJson.Item(7105, "第五页")))
            .On(FakeApi.View, 7101, BilibiliJson.View(7101, false, (9201, "P1")))
            .On(FakeApi.View, 7105, BilibiliJson.View(7105, false, (9205, "P1")));

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Complete, run.Downloader.Status, run.Downloader.Message);
        Assert.IsTrue(File.Exists(ExpectedPath(BilibiliJson.BvId(7105), "第五页", 1, "P1")));
        CollectionAssert.AreEqual(new[] {"7101", "7101-7105", "7101-"}, run.Checkpoints);
    }

    [TestMethod]
    public async Task PagingThatNeverEndsFailsWithoutCompletingTheCheckpoint()
    {
        _http.Api
            .On(FakeApi.Folders, BilibiliJson.Folders(2))
            .OnListPage(1, BilibiliJson.List(true, 2, BilibiliJson.Item(7201, "唯一")))
            .On(FakeApi.List, BilibiliJson.List(true, 2))
            .On(FakeApi.View, 7201, BilibiliJson.View(7201, false, (9301, "P1")));

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Failed, run.Downloader.Status);
        StringAssert.Contains(run.Downloader.Message!, "has_more is still true on page 7");
        Assert.AreEqual(6, _http.Api.Count(FakeApi.List), "ceil(2 / 20) + 5 pages");
        Assert.IsFalse(run.Checkpoints.Any(c => c.EndsWith('-')));
    }

    [TestMethod]
    public async Task ItemsThatKeepComingOutUnavailableStopTheRunWithoutMovingTheCheckpoint()
    {
        var items = Enumerable.Range(1, 5).Select(i => BilibiliJson.Item(7300 + i, $"不可用{i}")).ToArray();
        _http.Api
            .On(FakeApi.Folders, BilibiliJson.Folders(5))
            .OnListPage(1, BilibiliJson.List(false, 5, items))
            .On(FakeApi.PlayUrl, "playurl-404.json");
        for (var i = 1; i <= 5; i++)
        {
            _http.Api.On(FakeApi.View, 7300 + i, BilibiliJson.View(7300 + i, false, (9400 + i, "P1")));
        }

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Failed, run.Downloader.Status);
        StringAssert.Contains(run.Downloader.Message!, "3 items in a row were unavailable");
        Assert.AreEqual(0, run.Checkpoints.Count, "the checkpoint does not pass item 1");
        Assert.AreEqual(0, _http.Api.Count(FakeApi.View, 7304));
        StringAssert.Contains(run.Downloader.Message!, "unavailable on Bilibili (code -404)");
    }

    [TestMethod]
    public async Task AnUnavailableItemAtTheEndIsCheckedAgainNextTime()
    {
        _http.Api
            .On(FakeApi.Folders, BilibiliJson.Folders(2))
            .OnListPage(1, BilibiliJson.List(false, 2, BilibiliJson.Item(7401, "好的"), BilibiliJson.Item(7402, "坏的")))
            .On(FakeApi.View, 7401, BilibiliJson.View(7401, false, (9501, "P1")))
            .On(FakeApi.View, 7402, BilibiliJson.View(7402, false, (9502, "P1")))
            .On(FakeApi.PlayUrl, 7402, "playurl-404.json");

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Complete, run.Downloader.Status, run.Downloader.Message);
        CollectionAssert.AreEqual(new[] {"7401"}, run.Checkpoints, "not completed past the unavailable item");
        StringAssert.Contains(run.Downloader.Message!, "- [BV1test7402] 坏的 — skipped: unavailable on Bilibili (code -404)");

        // Next run: the downloaded item is skipped by the checkpoint, the unavailable one is asked again.
        _http.Api.On(FakeApi.PlayUrl, 7402, "playurl-durl-mp4.json");
        var second = await RunAsync(NewTask(run.Checkpoints[^1]));
        Assert.AreEqual(DownloaderStatus.Complete, second.Downloader.Status, second.Downloader.Message);
        Assert.AreEqual(1, _http.Api.Count(FakeApi.View, 7401));
        Assert.AreEqual(2, _http.Api.Count(FakeApi.View, 7402));
        Assert.AreEqual("7401-", second.Checkpoints[^1]);
    }

    [TestMethod]
    public async Task FfmpegBeingInstalledIsWaitedFor()
    {
        _http.Api
            .OnListPage(1, BilibiliJson.List(false, 1, BilibiliJson.Item(7501, "等待")))
            .On(FakeApi.View, 7501, BilibiliJson.View(7501, false, (9601, "P1")));
        _ffMpeg.Check = s =>
        {
            if (s.Checks > 1)
            {
                s.SetStatus(DependentComponentStatus.Installed);
                return Task.CompletedTask;
            }

            s.SetStatus(DependentComponentStatus.Installing);
            throw ControllableFfMpegService.NotReady("FFmpeg is being installed");
        };

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Complete, run.Downloader.Status, run.Downloader.Message);
        CollectionAssert.AreEqual(new[] {TimeSpan.FromSeconds(30)}, run.Downloader.Waits.ToList());
        Assert.AreEqual(1, _http.Api.Count(FakeApi.Folders), "no request before ffmpeg was ready");
    }

    [TestMethod]
    public async Task MissingFfmpegFailsBeforeAnyRequest()
    {
        _ffMpeg.Check = s =>
        {
            s.SetStatus(DependentComponentStatus.NotInstalled);
            throw ControllableFfMpegService.NotReady("FFmpeg is not installed");
        };

        var run = await RunAsync(NewTask());

        Assert.AreEqual(DownloaderStatus.Failed, run.Downloader.Status);
        StringAssert.Contains(run.Downloader.Message!, "FFmpeg is not installed");
        Assert.AreEqual(0, run.Downloader.Waits.Count);
        Assert.AreEqual(0, _http.Api.Requests.Count);
    }

    [TestMethod]
    public void GlobalProgressToleratesEmptyArchivesAndFolders()
    {
        Assert.AreEqual(50m, BilibiliDownloader.ComputeGlobalProgress(50, 1, 0, 0, 0));
        Assert.AreEqual(50m, BilibiliDownloader.ComputeGlobalProgress(100, 2, 2, 1, 4));
        Assert.AreEqual(100m, BilibiliDownloader.ComputeGlobalProgress(250, 3, 2, 9, 4));
        Assert.AreEqual(0m, BilibiliDownloader.ComputeGlobalProgress(-5, 0, 1, 0, 1));
    }

    [TestMethod]
    public void FormatSubjectNamesTheVideoAndThePart()
    {
        var item = new FavoriteItem {Id = 42, BvId = "BV1abc", Title = "标题"};
        Assert.AreEqual("[BV1abc] 标题", BilibiliDownloader.FormatSubject(item, null));
        Assert.AreEqual("[BV1abc] 标题 P2 第二集",
            BilibiliDownloader.FormatSubject(item, new BilibiliArchivePage(2, 1, "第二集", 1)));
        Assert.AreEqual("[av42]", BilibiliDownloader.FormatSubject(new FavoriteItem {Id = 42}, null));
    }

    [TestMethod]
    public void PruningRemovesOnlyAbandonedWorkFolders()
    {
        var temp = Path.Combine(_root, "temp");
        var now = DateTime.UtcNow;
        var old = now - TimeSpan.FromDays(8);

        string Dir(params string[] parts)
        {
            var path = Path.Combine([temp, ..parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        string FileAt(DateTime written, params string[] parts)
        {
            var path = Path.Combine([temp, ..parts]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, written);
            return path;
        }

        FileAt(old, "100", "5001", "v-80-c7.m4s.part");
        FileAt(old, "100", "old-leftover.flv");
        FileAt(old, "200", "6001", "a-30280.m4s.part");
        var fresh = FileAt(now, "200", "6002", "v-80-c7.m4s.part");
        var notOurs = FileAt(old, "notes", "keep.txt");
        foreach (var d in new[] {Dir("100", "5001"), Dir("100"), Dir("200", "6001"), Dir("200", "6002"), Dir("200"), Dir("notes")})
        {
            Directory.SetLastWriteTimeUtc(d, old);
        }

        BilibiliDownloader.PruneWorkDirectories(temp, now, BilibiliDownloader.WorkDirectoryMaxAge);

        Assert.IsFalse(Directory.Exists(Path.Combine(temp, "100")), "a folder nobody wrote to for 8 days goes");
        Assert.IsFalse(Directory.Exists(Path.Combine(temp, "200", "6001")));
        Assert.IsTrue(File.Exists(fresh), "an old folder with a fresh partial stays");
        Assert.IsTrue(File.Exists(notOurs), "folders that are not favorites ids are not ours");
        BilibiliDownloader.PruneWorkDirectories(Path.Combine(_root, "missing"), now, TimeSpan.FromDays(7));
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("zh-Hans")]
    public void EverySkipReasonAndMessageIsLocalized(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            var localizer = _services.GetRequiredService<IDownloaderLocalizer>();
            foreach (var reason in Enum.GetValues<BilibiliSkipReason>())
            {
                var text = localizer.DescribeBilibiliSkip(reason, 12, "msg");
                Assert.AreNotEqual(reason.ToString(), text, $"{reason} has no {culture} text");
                // A missing zh-Hans entry falls back to the English one, which the check above cannot tell apart.
                // (Not the reverse for English: some English texts quote Bilibili's Chinese labels on purpose.)
                if (culture == "zh-Hans")
                {
                    Assert.IsTrue(text.Any(c => c >= 0x4E00 && c <= 0x9FFF), $"{reason} has no zh-Hans text: {text}");
                }
            }

            foreach (var text in new[]
                     {
                         localizer.BilibiliFavoritesNotFound("1", "n"), localizer.BilibiliRiskControl(-352),
                         localizer.BilibiliRiskControlWaiting(10, 1, 2), localizer.BilibiliNotLoggedIn(),
                         localizer.BilibiliDiskFull("x"), localizer.BilibiliSkipNotice("s", "r"),
                         localizer.BilibiliSkipFooter(), localizer.DownloadNoticesSummary(3),
                         localizer.DownloadNoticesTruncated(3),
                     })
            {
                Assert.IsFalse(text.StartsWith("Bilibili.") || text.StartsWith("DownloadNotices."), text);
                Assert.AreEqual(culture == "zh-Hans", text.Any(c => c >= 0x4E00 && c <= 0x9FFF), text);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
