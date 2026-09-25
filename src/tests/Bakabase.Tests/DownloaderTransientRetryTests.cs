using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests;

/// <summary>
/// A single dropped connection anywhere in a download used to fail the whole task. The downloader
/// now runs the task again after a transient network failure, a few times, before giving up.
/// </summary>
[TestClass]
public sealed class DownloaderTransientRetryTests
{
    // Deliberately without [Downloader]: definitions are discovered from every loaded assembly, and
    // a test task type must not join the real registry.
    public enum ScriptedTaskType
    {
        Only = 1
    }

    private sealed class ScriptedDownloader(
        IServiceProvider serviceProvider,
        Func<ScriptedDownloader, DownloadTask, CancellationToken, int, Task> script)
        : AbstractDownloader<ScriptedTaskType>(serviceProvider)
    {
        public int Runs;
        public readonly List<TimeSpan> Waits = [];
        public readonly List<string?> StepsWhileWaiting = [];
        public Func<TimeSpan, CancellationToken, Task> Wait = (_, _) => Task.CompletedTask;

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public override ScriptedTaskType EnumTaskType => ScriptedTaskType.Only;

        public Task ReportCheckpoint(string checkpoint) => OnCheckpointChangedInternal(checkpoint);

        /// <summary>Stands in for the retry path writing its step after a stop already cleared it.</summary>
        public void WriteLateStep(string step) => Current = step;

        protected override Task StartCore(DownloadTask task, CancellationToken ct) =>
            script(this, task, ct, Interlocked.Increment(ref Runs));

        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct)
        {
            Waits.Add(delay);
            StepsWhileWaiting.Add(Current);
            return Wait(delay, ct);
        }
    }

    private sealed class RetryLocalizer : IDownloaderLocalizer
    {
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public string GetDownloaderName<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType) => "";
        public string? GetDownloaderDescription<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType) => null;
        public string GetNamingFieldName<TEnum>(TEnum namingFieldValue) => "";
        public string? GetNamingFieldDescription<TEnum>(TEnum namingFieldValue) => null;
        public string? GetNamingFieldExample<TEnum>(TEnum namingFieldValue) => null;
        public string InvalidFavorites() => "";
        public string FfMpegIsNotReady() => "";
        public string InvalidCookie() => "";
        public string DownloadPathNotSet() => "";

        public string TransientNetworkErrorRetrying(int delaySeconds, int retry, int maxRetries) =>
            $"retrying in {delaySeconds}s ({retry}/{maxRetries})";

        public string DownloadNoticesSummary(int count) => $"{count} notices";
        public string DownloadNoticesTruncated(int remaining) => $"{remaining} more";
        public string BilibiliFavoritesNotFound(string favoritesId, string? name) => "";
        public string BilibiliRiskControl(int? code) => "";
        public string BilibiliRiskControlWaiting(int minutes, int retry, int maxRetries) => "";
        public string BilibiliNotLoggedIn() => "";
        public string BilibiliDiskFull(string path) => "";

        public string DescribeBilibiliSkip(BilibiliSkipReason reason, int? code, string? message) =>
            reason.ToString();

        public string BilibiliSkipNotice(string subject, string reason) => "";
        public string BilibiliSkipFooter() => "";
    }

    private IServiceProvider _services = null!;

    [ClassInitialize]
    public static void RegisterScriptedDownloader(TestContext _)
    {
        DownloaderInternals.DownloaderTypeDefinitionMap.TryAdd(typeof(ScriptedDownloader), new DownloaderDefinition
        {
            ThirdPartyId = ThirdPartyId.ExHentai,
            TaskType = (int) ScriptedTaskType.Only,
            EnumTaskType = ScriptedTaskType.Only,
            Name = "Scripted",
            DownloaderType = typeof(ScriptedDownloader),
            HelperType = typeof(object),
            DefaultConvention = string.Empty,
        });
    }

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IDownloaderLocalizer>(new RetryLocalizer()));
    }

    /// <summary>The failure from the original report: an image server cut the TLS handshake short.</summary>
    private static HttpRequestException SslHandshakeCutShort() => new(HttpRequestError.SecureConnectionError,
        "The SSL connection could not be established, see inner exception.",
        new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));

    private static DownloadTask NewTask(string? checkpoint = null) =>
        new() { Id = 7, Key = "https://exhentai.org/g/1/a/", DownloadPath = "/downloads", Checkpoint = checkpoint };

    private ScriptedDownloader Build(Func<ScriptedDownloader, DownloadTask, CancellationToken, int, Task> script) =>
        new(_services, script);

    private static async Task WaitUntilSettled(ScriptedDownloader downloader)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (downloader.Status is DownloaderStatus.Starting or DownloaderStatus.Downloading
               or DownloaderStatus.Stopping)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The downloader is still {downloader.Status}.");
            }

            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task ATransientFailureRunsTheTaskAgainFromTheLatestCheckpoint()
    {
        var seenCheckpoints = new List<string?>();
        var runTokens = new List<CancellationToken>();
        var previousRunCancelledAtStart = new List<bool>();
        var failureTimesWhileRetrying = new List<int>();
        var downloader = Build(async (self, task, ct, run) =>
        {
            seenCheckpoints.Add(task.Checkpoint);
            if (run > 1)
            {
                // Sampled now, not after the task settles: by then Start() has cancelled everything.
                previousRunCancelledAtStart.Add(runTokens[^1].IsCancellationRequested);
                failureTimesWhileRetrying.Add(self.FailureTimes);
                Assert.IsFalse(ct.IsCancellationRequested, "Each run must get a live token of its own.");
            }

            runTokens.Add(ct);
            switch (run)
            {
                case 1:
                    await self.ReportCheckpoint("1-5");
                    throw SslHandshakeCutShort();
                case 2:
                    await self.ReportCheckpoint("1-9");
                    // ExHentai gives up on a gallery by rethrowing a finished task's AggregateException.
                    throw new AggregateException(SslHandshakeCutShort());
            }
        });

        Assert.IsTrue(await downloader.Start(NewTask("initial")));
        await WaitUntilSettled(downloader);

        Assert.AreEqual(DownloaderStatus.Complete, downloader.Status);
        Assert.AreEqual(3, downloader.Runs);
        CollectionAssert.AreEqual(new[] { 0, 0 }, failureTimesWhileRetrying,
            "Healed failures must not feed the task-level retry schedule.");
        CollectionAssert.AreEqual(new[] { "initial", "1-5", "1-9" }, seenCheckpoints,
            "Each run must resume where the previous one got to, not where the task started.");
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) }, downloader.Waits);
        CollectionAssert.AreEqual(new[] { "retrying in 10s (1/3)", "retrying in 30s (2/3)" },
            downloader.StepsWhileWaiting);
        CollectionAssert.AreEqual(new[] { true, true }, previousRunCancelledAtStart,
            "Whatever a failed run left in flight must be cancelled before the next run starts.");
        Assert.IsNull(downloader.Current);
    }

    [TestMethod]
    public async Task AnOutageThatOutlastsEveryRetryFailsOnceAndSaysSo()
    {
        var downloader = Build((_, _, _, _) => throw SslHandshakeCutShort());

        await downloader.Start(NewTask());
        await WaitUntilSettled(downloader);

        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);
        Assert.AreEqual(4, downloader.Runs, "One run plus three retries.");
        CollectionAssert.AreEqual(
            new[] { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90) },
            downloader.Waits);
        Assert.AreEqual(1, downloader.FailureTimes);
        StringAssert.Contains(downloader.Message!, "automatic retries after network errors: 3");
        StringAssert.Contains(downloader.Message!, "The SSL connection could not be established");
        Assert.IsNull(downloader.Current);
    }

    [TestMethod]
    public async Task FailuresThatRepeatingCannotFixAreNotRetried()
    {
        var downloader = Build((_, _, _, _) => throw new InvalidOperationException("Failed to parse the gallery."));

        await downloader.Start(NewTask());
        await WaitUntilSettled(downloader);

        Assert.AreEqual(DownloaderStatus.Failed, downloader.Status);
        Assert.AreEqual(1, downloader.Runs);
        Assert.AreEqual(0, downloader.Waits.Count);
        Assert.AreEqual(1, downloader.FailureTimes);
        Assert.IsFalse(downloader.Message!.Contains("automatic retries"));
    }

    [TestMethod]
    public async Task ADeferredTaskYieldsInsteadOfRetrying()
    {
        var downloader = Build((_, _, _, _) => throw new DownloadDeferredException());

        await downloader.Start(NewTask());
        await WaitUntilSettled(downloader);

        Assert.AreEqual(DownloaderStatus.Stopped, downloader.Status);
        Assert.AreEqual(DownloaderStopBy.Defer, downloader.StoppedBy);
        Assert.AreEqual(1, downloader.Runs);
    }

    [TestMethod]
    public async Task StoppingWhileWaitingStopsAtOnceWithoutAnotherRun()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = Build((_, _, _, _) => throw SslHandshakeCutShort());
        downloader.Wait = async (_, ct) =>
        {
            waiting.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                interrupted.TrySetResult();
                throw;
            }
        };

        await downloader.Start(NewTask());
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await downloader.Stop(DownloaderStopBy.ManuallyStop);
        // Stop() sets the final state itself; what this proves is that the wait actually ended.
        await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilSettled(downloader);
        await Task.Delay(100);

        Assert.AreEqual(DownloaderStatus.Stopped, downloader.Status);
        Assert.AreEqual(DownloaderStopBy.ManuallyStop, downloader.StoppedBy);
        Assert.AreEqual(1, downloader.Runs);
        Assert.AreEqual(0, downloader.FailureTimes, "A stop is not a failure.");
        Assert.IsNull(downloader.Current);
    }

    [TestMethod]
    public async Task AStopThatBeatsTheRetryStepDoesNotLeaveItOnTheStoppedRow()
    {
        var downloader = Build((_, _, _, _) => throw SslHandshakeCutShort());
        downloader.Wait = async (_, ct) =>
        {
            // The stop completes (clearing Current) before the retry step lands.
            await downloader.Stop(DownloaderStopBy.ManuallyStop);
            downloader.WriteLateStep("retrying in 10s (1/3)");
            ct.ThrowIfCancellationRequested();
        };

        await downloader.Start(NewTask());
        await WaitUntilSettled(downloader);
        await Task.Delay(100);

        Assert.AreEqual(DownloaderStatus.Stopped, downloader.Status);
        Assert.AreEqual(1, downloader.Runs);
        Assert.IsNull(downloader.Current, "A stopped task must not keep promising a retry.");
    }
}
