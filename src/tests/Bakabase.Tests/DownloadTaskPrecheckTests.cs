using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// The pre-check is what lets a re-run of a large, mostly-finished queue skip straight past the
/// tasks with nothing left to do, instead of paying a downloader, a background task, a database
/// write and a rate-limited network request each to reach the same conclusion.
///
/// It is only allowed to skip a task when it is <em>provably</em> done, so most of what is worth
/// pinning down here is when it must NOT skip.
/// </summary>
[TestClass]
public class DownloadTaskPrecheckTests
{
    private string _downloadDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _downloadDir = Path.Combine(Path.GetTempPath(), "bakabase-precheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_downloadDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_downloadDir, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private sealed class StubExHentaiOptions(ExHentaiOptions options) : IBOptions<ExHentaiOptions>
    {
        public ExHentaiOptions Value { get; } = options;
    }

    /// <summary>
    /// Knows nothing, which is the state right after a restart — exactly the case the verdicts
    /// persisted on the task exist to cover.
    /// </summary>
    private sealed class EmptyVerdictCache : ITransientTorrentVerdictCache
    {
        public bool IsKnownNoTorrent(int taskId) => false;
    }

    private ExHentaiDownloadTaskPrecheck BuildPrecheck(bool prioritizeTorrents = false,
        int? torrentCheckValidityHours = null) =>
        new(
            new StubExHentaiOptions(new ExHentaiOptions
            {
                PrioritizeTasksWithTorrent = prioritizeTorrents,
                TorrentCheckValidityHours = torrentCheckValidityHours
            }),
            new EmptyVerdictCache(),
            NullLogger<ExHentaiDownloadTaskPrecheck>.Instance);

    private DownloadTask NewTask(int id, string? name, ExHentaiTaskOptions options,
        int type = (int) ExHentaiDownloadTaskType.SingleWork)
    {
        var task = new DownloadTask
        {
            Id = id,
            Key = $"https://exhentai.org/g/{id}/",
            Name = name,
            ThirdPartyId = ThirdPartyId.ExHentai,
            Type = type,
            DownloadPath = _downloadDir
        };

        task.SetTypedOptions(options);

        return task;
    }

    private void WriteTorrent(string name) =>
        File.WriteAllText(Path.Combine(_downloadDir, $"{name}.torrent"), "d4:teste");

    private static async Task<DownloadTaskPrecheckVerdict?> Evaluate(IDownloadTaskPrecheck precheck,
        params DownloadTask[] tasks)
    {
        var verdicts = await precheck.EvaluateAsync(tasks, CancellationToken.None);

        return verdicts.TryGetValue(tasks[0].Id, out var v) ? v : null;
    }

    [TestMethod]
    public async Task TorrentAlreadyOnDisk_IsSatisfiedWithoutStarting()
    {
        WriteTorrent("Some Gallery");

        var verdict = await Evaluate(BuildPrecheck(),
            NewTask(1, "Some Gallery", new ExHentaiTaskOptions { PreferTorrent = true }));

        Assert.AreEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, verdict?.Outcome);
    }

    [TestMethod]
    public async Task TorrentFileNameIsSanitised_TheSameWayTheDownloaderWritesIt()
    {
        // The downloader replaces characters a file name cannot hold with an underscore, so the
        // pre-check has to match exactly or it silently never skips a gallery with a slash in its
        // title. "/" is the one separator that is invalid on every platform this ships to.
        WriteTorrent("A Gallery_part 1");

        var verdict = await Evaluate(BuildPrecheck(),
            NewTask(1, "A Gallery/part 1", new ExHentaiTaskOptions { PreferTorrent = true }));

        Assert.AreEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, verdict?.Outcome);
    }

    [TestMethod]
    public async Task NoTorrentOnDisk_RunsNormally()
    {
        var verdict = await Evaluate(BuildPrecheck(),
            NewTask(1, "Some Gallery", new ExHentaiTaskOptions { PreferTorrent = true }));

        Assert.IsNull(verdict, "A task with work left must not be given a verdict at all.");
    }

    [TestMethod]
    public async Task TaskWithoutAName_IsNeverSkipped()
    {
        // The gallery name is the torrent's file name, so a task that has never run cannot be
        // matched against the folder — and guessing would skip a download that never happened.
        WriteTorrent("Some Gallery");

        var verdict = await Evaluate(BuildPrecheck(),
            NewTask(1, null, new ExHentaiTaskOptions { PreferTorrent = true }));

        Assert.IsNull(verdict);
    }

    [TestMethod]
    public async Task TaskThatOptedOutOfTorrents_IsNeverSkipped()
    {
        // It downloads images; a .torrent sitting in the folder says nothing about that.
        WriteTorrent("Some Gallery");

        var verdict = await Evaluate(BuildPrecheck(),
            NewTask(1, "Some Gallery", new ExHentaiTaskOptions { PreferTorrent = false }));

        Assert.AreNotEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, verdict?.Outcome);
    }

    [TestMethod]
    public async Task ListTask_IsNeverSkipped()
    {
        // A list task covers many galleries, so one matching file proves nothing about the rest.
        WriteTorrent("Some Gallery");

        var verdict = await Evaluate(BuildPrecheck(),
            NewTask(1, "Some Gallery", new ExHentaiTaskOptions { PreferTorrent = true },
                (int) ExHentaiDownloadTaskType.List));

        Assert.IsNull(verdict);
    }

    [TestMethod]
    public async Task TorrentPriorityOff_LeavesOrderingAlone()
    {
        var verdict = await Evaluate(BuildPrecheck(prioritizeTorrents: false),
            NewTask(1, "Image Only", new ExHentaiTaskOptions { PreferTorrent = false }));

        Assert.IsNull(verdict, "Without torrent-priority every task is equal and keeps FIFO order.");
    }

    [TestMethod]
    public async Task TorrentPriorityOn_DefersTasksThatWillDownloadImages()
    {
        var verdict = await Evaluate(BuildPrecheck(prioritizeTorrents: true),
            NewTask(1, "Image Only", new ExHentaiTaskOptions { PreferTorrent = false }));

        Assert.AreEqual(DownloadTaskPrecheckOutcome.Defer, verdict?.Outcome);
    }

    [TestMethod]
    public async Task TorrentPriorityOn_DefersTasksWithAFreshNoTorrentVerdict()
    {
        var verdict = await Evaluate(BuildPrecheck(prioritizeTorrents: true, torrentCheckValidityHours: 24),
            NewTask(1, "Probed", new ExHentaiTaskOptions
            {
                PreferTorrent = true,
                NoTorrentCheckedAt = DateTime.Now.AddHours(-1)
            }));

        Assert.AreEqual(DownloadTaskPrecheckOutcome.Defer, verdict?.Outcome);
    }

    [TestMethod]
    public async Task TorrentPriorityOn_ReProbesOnceTheVerdictHasExpired()
    {
        var verdict = await Evaluate(BuildPrecheck(prioritizeTorrents: true, torrentCheckValidityHours: 24),
            NewTask(1, "Probed Long Ago", new ExHentaiTaskOptions
            {
                PreferTorrent = true,
                NoTorrentCheckedAt = DateTime.Now.AddHours(-48)
            }));

        Assert.IsNull(verdict, "An expired verdict must send the task back to the front to be probed.");
    }

    [TestMethod]
    public async Task OneListingIsSharedAcrossTasksInTheSameFolder()
    {
        // The whole point of the batch pre-check: answering for a thousand tasks must not cost a
        // thousand trips to the filesystem.
        WriteTorrent("A");
        WriteTorrent("B");

        var tasks = Enumerable.Range(1, 3)
            .Select(i => NewTask(i, i == 3 ? "C" : (i == 1 ? "A" : "B"),
                new ExHentaiTaskOptions { PreferTorrent = true }))
            .ToArray();

        var verdicts = await BuildPrecheck().EvaluateAsync(tasks, CancellationToken.None);

        Assert.AreEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, verdicts[1].Outcome);
        Assert.AreEqual(DownloadTaskPrecheckOutcome.AlreadySatisfied, verdicts[2].Outcome);
        Assert.IsFalse(verdicts.ContainsKey(3), "C has no torrent on disk and must still run.");
    }

    [TestMethod]
    public async Task UnreadableFolder_SkipsNothing()
    {
        var task = NewTask(1, "Some Gallery", new ExHentaiTaskOptions { PreferTorrent = true });

        task.DownloadPath = Path.Combine(_downloadDir, "does-not-exist");

        var verdicts = await BuildPrecheck().EvaluateAsync([task], CancellationToken.None);

        Assert.IsFalse(verdicts.ContainsKey(1),
            "Downloading a torrent that already exists is cheap; wrongly skipping one is not.");
    }
}

/// <summary>
/// The runner is what makes the pre-check affordable — a scheduling pass happens after every task
/// transition — and also what makes it dangerous, because a cached answer that has gone stale skips
/// a download that should have run.
/// </summary>
[TestClass]
public class DownloadTaskPrecheckRunnerTests
{
    private sealed class CountingPrecheck : IDownloadTaskPrecheck
    {
        public int Calls;
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;

        public Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct)
        {
            Calls++;

            IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict> verdicts = candidates.ToDictionary(
                c => c.Id,
                _ => new DownloadTaskPrecheckVerdict(DownloadTaskPrecheckOutcome.Defer));

            return Task.FromResult(verdicts);
        }
    }

    private sealed class ThrowingPrecheck : IDownloadTaskPrecheck
    {
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;

        public Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private static DownloadTask NewTask(int id) => new()
    {
        Id = id,
        Key = id.ToString(),
        ThirdPartyId = ThirdPartyId.ExHentai,
        DownloadPath = "/downloads"
    };

    private static DownloadTaskPrecheckRunner Build(IDownloadTaskPrecheck precheck) =>
        new([precheck], NullLogger<DownloadTaskPrecheckRunner>.Instance);

    [TestMethod]
    public async Task RepeatedPassesOverTheSameTasks_EvaluateOnce()
    {
        var precheck = new CountingPrecheck();
        var runner = Build(precheck);

        await runner.EvaluateAsync([NewTask(1), NewTask(2)]);
        await runner.EvaluateAsync([NewTask(1), NewTask(2)]);
        await runner.EvaluateAsync([NewTask(1)]);

        Assert.AreEqual(1, precheck.Calls);
    }

    [TestMethod]
    public async Task ATaskTheSnapshotNeverSaw_ForcesReEvaluation()
    {
        var precheck = new CountingPrecheck();
        var runner = Build(precheck);

        await runner.EvaluateAsync([NewTask(1)]);
        await runner.EvaluateAsync([NewTask(1), NewTask(2)]);

        Assert.AreEqual(2, precheck.Calls);
    }

    [TestMethod]
    public async Task Invalidate_ForcesReEvaluation()
    {
        var precheck = new CountingPrecheck();
        var runner = Build(precheck);

        await runner.EvaluateAsync([NewTask(1)]);
        runner.Invalidate();
        await runner.EvaluateAsync([NewTask(1)]);

        Assert.AreEqual(2, precheck.Calls);
    }

    [TestMethod]
    public async Task AFailingPrecheck_SchedulesNormallyRatherThanBlockingTheQueue()
    {
        var runner = Build(new ThrowingPrecheck());

        var verdicts = await runner.EvaluateAsync([NewTask(1)]);

        Assert.AreEqual(0, verdicts.Count);
    }

    [TestMethod]
    public async Task SourcesWithoutAPrecheck_AreLeftAlone()
    {
        var runner = Build(new CountingPrecheck());

        var verdicts = await runner.EvaluateAsync([
            new DownloadTask { Id = 9, Key = "9", ThirdPartyId = ThirdPartyId.Pixiv, DownloadPath = "/d" }
        ]);

        Assert.AreEqual(0, verdicts.Count);
    }
}
