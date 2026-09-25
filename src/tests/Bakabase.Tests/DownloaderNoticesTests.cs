using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Utils;

namespace Bakabase.Tests;

/// <summary>
/// Notices: things a run that still succeeds wants the user to know (e.g. items skipped on purpose). They become the
/// completed task's message, and are appended to the failure text of a failed one.
/// </summary>
[TestClass]
public sealed class DownloaderNoticesTests
{
    // Deliberately without [Downloader], like the scripted downloader of DownloaderTransientRetryTests.
    public enum NoticeTaskType
    {
        Only = 1
    }

    private sealed class NoticeDownloader(
        IServiceProvider serviceProvider,
        Func<NoticeDownloader, int, Task> script)
        : AbstractDownloader<NoticeTaskType>(serviceProvider)
    {
        public int Runs;
        public int Starts;
        public CancellationToken RunToken;
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public override NoticeTaskType EnumTaskType => NoticeTaskType.Only;
        public void Note(string notice) => AddNotice(notice);
        public string? Footer;
        protected override string? GetNoticesFooter() => Footer;
        protected override void OnStarting() => Starts++;
        protected override Task StartCore(DownloadTask task, CancellationToken ct)
        {
            RunToken = ct;
            return script(this, Interlocked.Increment(ref Runs));
        }
        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    private IServiceProvider _services = null!;

    [ClassInitialize]
    public static void Register(TestContext _)
    {
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(NoticeDownloader), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.ExHentai,
            TaskType = (int) NoticeTaskType.Only,
            EnumTaskType = NoticeTaskType.Only,
            Name = "Notices",
            DownloaderType = typeof(NoticeDownloader),
            HelperType = typeof(object),
            DefaultConvention = string.Empty,
        });
    }

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    private static DownloadTask NewTask(string? checkpoint = null) =>
        new() {Id = 1, Key = "k", DownloadPath = "/downloads", Checkpoint = checkpoint};

    private IDownloaderLocalizer Localizer => (IDownloaderLocalizer) _services.GetService(typeof(IDownloaderLocalizer))!;

    private static async Task Settle(NoticeDownloader downloader)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (downloader.Status is DownloaderStatus.Starting or DownloaderStatus.Downloading or DownloaderStatus.Stopping)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"still {downloader.Status}");
            }

            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task ACleanRunLeavesNoMessage()
    {
        var downloader = new NoticeDownloader(_services, (_, _) => Task.CompletedTask);
        await downloader.Start(NewTask());
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.IsNull(downloader.Message);
    }

    [TestMethod]
    public async Task NoticesAreDeduplicatedKeptAcrossReRunsAndCapped()
    {
        var downloader = new NoticeDownloader(_services, (self, run) =>
        {
            if (run >= 3)
            {
                return Task.CompletedTask;
            }

            self.Note("first");
            self.Note("first");
            if (run == 1)
            {
                throw new HttpRequestException(HttpRequestError.ConnectionError, "reset");
            }

            for (var i = 0; i < AbstractDownloader<NoticeTaskType>.MaxListedNotices + 4; i++)
            {
                self.Note($"n{i}");
            }

            return Task.CompletedTask;
        }) {Footer = "footer"};

        await downloader.Start(NewTask());
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.AreEqual(2, downloader.Runs);
        var lines = downloader.Message!.Split('\n');
        var localizer = (IDownloaderLocalizer) _services.GetService(typeof(IDownloaderLocalizer))!;
        Assert.AreEqual(localizer.DownloadNoticesSummary(AbstractDownloader<NoticeTaskType>.MaxListedNotices + 5), lines[0]);
        Assert.AreEqual("- first", lines[1]);
        Assert.AreEqual(AbstractDownloader<NoticeTaskType>.MaxListedNotices, lines.Count(l => l.StartsWith("- ")));
        Assert.AreEqual(localizer.DownloadNoticesTruncated(5), lines[^2]);
        Assert.AreEqual("footer", lines[^1]);

        // A new start begins with no notices.
        var starts = downloader.Starts;
        await downloader.Start(NewTask());
        await Settle(downloader);
        Assert.AreEqual(starts + 1, downloader.Starts);
        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.IsNull(downloader.Message, "the notes of the completed run are not carried into the next one");
    }

    [TestMethod]
    public async Task AStartResumingFromTheCheckpointKeepsTheNotesOfTheFailedRun()
    {
        var downloader = new NoticeDownloader(_services, (self, run) =>
        {
            self.Note(run == 1 ? "before the failure" : "after the restart");
            return run == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        });

        await downloader.Start(NewTask());
        await Settle(downloader);
        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);

        await downloader.Start(NewTask("1001-1005"));
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.AreEqual(
            $"{Localizer.DownloadNoticesSummary(2)}\n- before the failure\n- after the restart", downloader.Message);
    }

    [TestMethod]
    public async Task AStartFromTheBeginningDropsTheNotesOfTheFailedRun()
    {
        var downloader = new NoticeDownloader(_services, (self, run) =>
        {
            self.Note($"run {run}");
            return run == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        });

        await downloader.Start(NewTask("1001-1005"));
        await Settle(downloader);
        // The checkpoint was cleared: every item is seen (and noted) again.
        await downloader.Start(NewTask());
        await Settle(downloader);

        Assert.AreEqual($"{Localizer.DownloadNoticesSummary(1)}\n- run 2", downloader.Message);
    }

    [TestMethod]
    public async Task AStoppedTaskShowsItsNotesAndTheResumedRunKeepsThem()
    {
        var noted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRunOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new NoticeDownloader(_services, async (self, run) =>
        {
            if (run == 1)
            {
                try
                {
                    self.Note("before the stop");
                    noted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, self.RunToken);
                }
                finally
                {
                    firstRunOver.TrySetResult();
                }
            }

            self.Note("after the stop");
        });

        await downloader.Start(NewTask("1001-1003"));
        await noted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await downloader.Stop(DownloaderStopBy.ManuallyStop);
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Stopped, downloader.Status);
        Assert.AreEqual($"{Localizer.DownloadNoticesSummary(1)}\n- before the stop", downloader.Message);
        // The stopped run's body winds down after Stop returns; a start racing it is not what this test is about.
        await firstRunOver.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);

        await downloader.Start(NewTask("1001-1003"));
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.AreEqual($"{Localizer.DownloadNoticesSummary(2)}\n- before the stop\n- after the stop",
            downloader.Message);
    }

    [TestMethod]
    public async Task AFailureTheUserCanActOnLeadsTheMessageWithItsOwnText()
    {
        var reason = "B 站 Cookie 未登录或已过期，请在下载器设置中更新。";
        var downloader = new NoticeDownloader(_services, (_, _) =>
            throw new BilibiliDownloadInterruptedException(reason, new BilibiliNotLoggedInException("myinfo -101")));

        await downloader.Start(NewTask());
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);
        var lines = downloader.Message!.Split('\n');
        Assert.AreEqual(reason, lines[0]);
        StringAssert.StartsWith(lines[1], "An error occurred during downloading files.");
        StringAssert.Contains(downloader.Message, nameof(BilibiliNotLoggedInException), "the details follow");
    }

    [TestMethod]
    public async Task AnOrdinaryFailureKeepsTheGenericHeaderFirst()
    {
        var downloader = new NoticeDownloader(_services, (_, _) => throw new InvalidOperationException("boom"));

        await downloader.Start(NewTask());
        await Settle(downloader);

        StringAssert.StartsWith(downloader.Message!, "An error occurred during downloading files.");
    }

    [TestMethod]
    public async Task AFailedRunAppendsItsNoticesToTheError()
    {
        var downloader = new NoticeDownloader(_services, (self, _) =>
        {
            self.Note("skipped something");
            throw new InvalidOperationException("boom");
        });

        await downloader.Start(NewTask());
        await Settle(downloader);

        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);
        StringAssert.Contains(downloader.Message!, "boom");
        Assert.IsTrue(downloader.Message!.EndsWith("\n- skipped something"), downloader.Message);
    }
}
