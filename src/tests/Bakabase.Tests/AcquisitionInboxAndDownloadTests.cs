using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// The two ways files actually arrive: downloaded straight off a link, or fetched by a person into
/// the inbox. The second is the interesting one — it is where the pipeline deliberately stops and
/// waits for a human, and where a wrong guess moves somebody's file.
/// </summary>
[TestClass]
public sealed class AcquisitionInboxAndDownloadTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;
    private string _inbox = null!;
    private string _working = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseInbox_{Guid.NewGuid():N}");
        _inbox = Path.Combine(_root, "inbox");
        _working = Path.Combine(_root, "working");
        Directory.CreateDirectory(_inbox);
        Directory.CreateDirectory(_working);

        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.InboxDirectory = _inbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private AcquisitionStepContext Context(string? configJson = null) => new(
        _sp, NullLogger.Instance, (_, _) => Task.CompletedTask, _working, configJson);

    private static AcquisitionWorkItem Item(params AcquisitionLink[] links) => new()
    {
        ResourceId = 1,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.com/thread/1",
        Links = links,
        SelectedLinkIndex = links.Length > 0 ? 0 : null,
    };

    private AcquisitionWorkItem RequestedItem(AcquisitionLeadKind kind, string value) =>
        (AcquisitionWorkItem) new AcquisitionRequestedTrigger().ExtractItems(new AcquisitionRequestedPayload
        {
            ResourceId = 1,
            LeadKind = kind,
            LeadValue = value,
            WorkingDirectory = _working
        }).Single();

    private WorkflowExecutionContext WorkflowContext() => new()
    {
        RunId = 1,
        WorkflowDefinitionId = 1,
        TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
        // Task-row mirroring is covered by AcquisitionPipelineTests; this exercises the real
        // adapter and steps against a restored item, without an unrelated task row.
        Payload = new object(),
        ActivityConfigJson = """{"openLink":false}""",
        Services = _sp,
        Logger = NullLogger.Instance
    };

    private string DropInInbox(string name, string content = "payload")
    {
        var path = Path.Combine(_inbox, name);

        File.WriteAllText(path, content);

        return path;
    }

    // ------- the inbox step -------

    [TestMethod]
    public async Task WaitingForTheInbox_SuspendsWithEverythingNeededToFetchItByHand()
    {
        var step = new WaitForInboxStep();
        var item = Item(new AcquisitionLink("https://pan.baidu.com/s/abc", "8k2p",
            DriveKind: AcquisitionDriveKind.Baidu));

        var outcome = await step.ExecuteAsync(Context("""{"openLink":false}"""), item, CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;

        Assert.AreEqual(AcquisitionWaitReason.WaitingForFile, suspended.Reason);

        var prompt = JsonSerializer.Deserialize<WaitForInboxStep.Prompt>(suspended.PromptJson!, Json)!;

        Assert.AreEqual("https://pan.baidu.com/s/abc", prompt.Url);
        Assert.AreEqual("8k2p", prompt.AccessCode, "the code is what the user will be asked for");
        Assert.AreEqual(_inbox, prompt.InboxDirectory);
    }

    [TestMethod]
    public async Task ARequestedMagnetIsShownWhenTheDefaultRecipeWaitsForFiles()
    {
        const string url = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Example";
        var item = RequestedItem(AcquisitionLeadKind.Magnet, url);

        Assert.AreEqual(AcquisitionDriveKind.Magnet, item.SelectedLink!.DriveKind);

        var outcome = await new WaitForInboxStep().ExecuteAsync(Context("""{"openLink":false}"""),
            item, CancellationToken.None);
        var suspended = (AcquisitionStepOutcome.Suspend) outcome;
        var prompt = JsonSerializer.Deserialize<WaitForInboxStep.Prompt>(suspended.PromptJson!, Json)!;

        Assert.AreEqual(AcquisitionWaitReason.WaitingForFile, suspended.Reason);
        Assert.AreEqual(url, prompt.Url);
        Assert.AreEqual(_inbox, prompt.InboxDirectory);
    }

    [DataTestMethod]
    [DataRow(AcquisitionLeadKind.SharedPage, "https://example.com/share")]
    [DataRow(AcquisitionLeadKind.SharedDocument, "A title and https://example.com/share")]
    [DataRow(AcquisitionLeadKind.PlatformHolding, "Steam:123")]
    [DataRow(AcquisitionLeadKind.Manual, "/some/local/directory")]
    public void OtherLeadKindsDoNotBecomeDownloadLinks(AcquisitionLeadKind kind, string value)
    {
        var item = RequestedItem(kind, value);

        Assert.AreEqual(0, item.Links.Count);
        Assert.IsNull(item.SelectedLink);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AResumedMagnetRecoversAnEmptySnapshotButPreservesExistingLinkChoices(bool hasLinks)
    {
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        const string chosenUrl = "https://pan.baidu.com/s/chosen-by-user";
        var snapshot = new AcquisitionWorkItem
        {
            ResourceId = 1,
            LeadKind = AcquisitionLeadKind.Magnet,
            LeadValue = magnet,
            WorkingDirectory = _working,
            Links = hasLinks
                ? [new AcquisitionLink(magnet), new AcquisitionLink(chosenUrl, "1234", "archive-password",
                    AcquisitionDriveKind.Baidu)]
                : [],
            SelectedLinkIndex = hasLinks ? 1 : null
        };
        var restored = JsonSerializer.Deserialize<AcquisitionWorkItem>(JsonSerializer.Serialize(snapshot, Json), Json)!;

        var outcome = await new AcquisitionStepActivity(new WaitForInboxStep())
            .ProcessItemAsync(WorkflowContext(), restored, CancellationToken.None);
        var prompt = JsonSerializer.Deserialize<WaitForInboxStep.Prompt>(outcome.Suspension!.PromptJson!, Json)!;
        var waitingItem = (AcquisitionWorkItem) outcome.Suspension.Item;

        Assert.AreEqual(hasLinks ? chosenUrl : magnet, prompt.Url);
        Assert.AreEqual(hasLinks ? "1234" : null, prompt.AccessCode);
        Assert.AreEqual(hasLinks ? "archive-password" : null, waitingItem.SelectedLink!.ArchivePassword);
        Assert.AreEqual(hasLinks ? 1 : 0, waitingItem.SelectedLinkIndex);
        Assert.AreEqual(hasLinks ? 2 : 0, restored.Links.Count, "restoring does not mutate the old snapshot");
    }

    [TestMethod]
    public async Task WithNoInboxConfigured_TheStepSaysSoRatherThanWaitingForever()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.InboxDirectory = null;

        var outcome = await new WaitForInboxStep().ExecuteAsync(Context(), Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
    }

    [TestMethod]
    public async Task ClaimingMovesTheFileOutOfTheInboxAndOntoTheItem()
    {
        var step = new WaitForInboxStep();
        var dropped = DropInInbox("A Great Work.zip");

        var outcome = await step.ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.WaitingForFile,
                JsonSerializer.Serialize(new WaitForInboxStep.ClaimSignal([dropped]), Json)),
            CancellationToken.None);

        var item = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual(Path.Combine(_working, "A Great Work.zip"), item.Files.Single());
        Assert.IsTrue(File.Exists(item.Files[0]));
        Assert.IsFalse(File.Exists(dropped), "and it is gone from the inbox, so nothing claims it twice");
    }

    /// <summary>
    /// The claim arrives over an API. Without this, a request naming any path on the machine would
    /// have the step move that file.
    /// </summary>
    [TestMethod]
    public async Task AClaimForAFileOutsideTheInboxIsRefused()
    {
        var elsewhere = Path.Combine(_root, "not-the-inbox.zip");

        File.WriteAllText(elsewhere, "x");

        var outcome = await new WaitForInboxStep().ResumeAsync(Context(), Item(),
            new AcquisitionResumeSignal(AcquisitionWaitReason.WaitingForFile,
                JsonSerializer.Serialize(new WaitForInboxStep.ClaimSignal([elsewhere]), Json)),
            CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(outcome);
        Assert.IsTrue(File.Exists(elsewhere), "and it stays where it was");
    }

    /// <summary>Steps are re-run at the cursor after a restart, so the move has to be repeatable.</summary>
    [TestMethod]
    public async Task ClaimingTheSameFileTwiceIsHarmless()
    {
        var step = new WaitForInboxStep();
        var dropped = DropInInbox("A Great Work.zip");
        var signal = new AcquisitionResumeSignal(AcquisitionWaitReason.WaitingForFile,
            JsonSerializer.Serialize(new WaitForInboxStep.ClaimSignal([dropped]), Json));

        var first = ((AcquisitionStepOutcome.Continue) await step.ResumeAsync(Context(), Item(), signal,
            CancellationToken.None)).Item;

        // Same claim again, with the file put back the way a retry would find it.
        DropInInbox("A Great Work.zip");

        var second = ((AcquisitionStepOutcome.Continue) await step.ResumeAsync(Context(), first, signal,
            CancellationToken.None)).Item;

        Assert.AreEqual(1, second.Files.Count, "the same file does not get listed twice");
        Assert.AreEqual(1, Directory.GetFiles(_working).Length);
    }

    // ------- the watcher -------

    private AcquisitionInboxWatcher Watcher() =>
        new(_sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<IBOptions<AcquisitionOptions>>(),
            NullLogger<AcquisitionInboxWatcher>.Instance);

    [TestMethod]
    public async Task AFileStillGrowingIsNotOfferedToAnything()
    {
        var watcher = Watcher();
        var path = DropInInbox("growing.zip", "a");

        await watcher.TickAsync(CancellationToken.None);
        File.AppendAllText(path, "bbbb");
        await watcher.TickAsync(CancellationToken.None);
        File.AppendAllText(path, "cccc");
        await watcher.TickAsync(CancellationToken.None);

        // Nothing is waiting in this test, so what is asserted is that the file is still there and
        // untouched — the watcher never took it out from under a download in progress.
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void APartiallyDownloadedFileIsNotStable()
    {
        Assert.IsFalse(AcquisitionInboxService.IsStable(DropInInbox("work.zip.crdownload")));
        Assert.IsFalse(AcquisitionInboxService.IsStable(DropInInbox("work.zip.part")));
        Assert.IsTrue(AcquisitionInboxService.IsStable(DropInInbox("work.zip")));
    }

    /// <summary>
    /// Taking part 1 away while part 2 is still downloading leaves an archive that can never be
    /// unpacked, and the run fails on a set it cannot complete.
    /// </summary>
    [TestMethod]
    public void AnIncompleteVolumeSetIsNotReady()
    {
        DropInInbox("work.part1.rar");
        var partial = DropInInbox("work.part2.rar.crdownload");

        Assert.IsFalse(AcquisitionInboxService.IsCompleteVolumeSet(Path.Combine(_inbox, "work.part1.rar")));

        File.Move(partial, Path.Combine(_inbox, "work.part2.rar"));

        Assert.IsTrue(AcquisitionInboxService.IsCompleteVolumeSet(Path.Combine(_inbox, "work.part1.rar")));
        Assert.IsTrue(AcquisitionInboxService.IsCompleteVolumeSet(Path.Combine(_inbox, "single.zip")),
            "a file that is not part of a set is always ready");
    }

    // ------- downloading -------

    /// <summary>
    /// A tiny server so the downloader is exercised against real HTTP semantics rather than a mock
    /// that agrees with it.
    /// </summary>
    private sealed class Server : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _body;
        private readonly bool _supportsRange;
        private readonly bool _declareLength;

        public string Url { get; }
        public int Requests;

        public Server(byte[] body, bool supportsRange, bool declareLength = true)
        {
            _body = body;
            _supportsRange = supportsRange;
            _declareLength = declareLength;

            var port = 18000 + Random.Shared.Next(1000);

            Url = $"http://127.0.0.1:{port}/file.bin";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                Interlocked.Increment(ref Requests);

                var rsp = ctx.Response;

                rsp.Headers.Add("Content-Disposition", "attachment; filename=\"A Great Work.zip\"");

                if (_supportsRange) rsp.Headers.Add("Accept-Ranges", "bytes");

                var range = ctx.Request.Headers["Range"];

                if (ctx.Request.HttpMethod == "HEAD")
                {
                    if (_declareLength) rsp.ContentLength64 = _body.Length;
                    rsp.Close();

                    continue;
                }

                var (start, end) = (0, _body.Length - 1);

                if (_supportsRange && range?.StartsWith("bytes=") == true)
                {
                    var parts = range["bytes=".Length..].Split('-');

                    start = int.Parse(parts[0]);
                    end = parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1]) : _body.Length - 1;
                    rsp.StatusCode = 206;
                    rsp.Headers.Add("Content-Range", $"bytes {start}-{end}/{_body.Length}");
                }

                var slice = _body[start..(end + 1)];

                if (_declareLength) rsp.ContentLength64 = slice.Length;
                await rsp.OutputStream.WriteAsync(slice);
                rsp.Close();
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* best effort */ }
        }
    }

    private HttpDownloadClient Downloader() => new(_sp.GetRequiredService<IHttpDownloader>());

    private sealed class HttpDownloadClient(IHttpDownloader downloader)
    {
        public Task<string> DownloadToDirectory(string url, string directory, CancellationToken ct) =>
            downloader.DownloadAsync(new HttpDownloadRequest(url, directory), null, ct);
        public Task Download(string url, string path, CancellationToken ct) =>
            downloader.DownloadAsync(new HttpDownloadRequest(url, Path.GetDirectoryName(path)!)
                {FileName = Path.GetFileName(path)}, null, ct);
    }

    [TestMethod]
    public async Task ARequestedDirectLinkReachesTheHttpDownloader()
    {
        var body = Encoding.UTF8.GetBytes("downloaded from the requested lead");
        using var server = new Server(body, supportsRange: true);
        var requested = RequestedItem(AcquisitionLeadKind.DirectUrl, server.Url);

        var outcome = await new FetchHttpStep().ExecuteAsync(Context(), requested, CancellationToken.None);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        var downloaded = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual(server.Url, downloaded.SelectedLink!.Url);
        Assert.AreEqual(AcquisitionDriveKind.DirectUrl, downloaded.SelectedLink.DriveKind);
        CollectionAssert.AreEqual(body, await File.ReadAllBytesAsync(downloaded.Files.Single()));
    }

    [TestMethod]
    public async Task ARetriedDirectLinkFromAnOldEmptySnapshotCanDownload()
    {
        var body = Encoding.UTF8.GetBytes("downloaded after retrying the old snapshot");
        using var server = new Server(body, supportsRange: true);
        var oldSnapshot = new AcquisitionWorkItem
        {
            ResourceId = 1,
            LeadKind = AcquisitionLeadKind.DirectUrl,
            LeadValue = server.Url,
            WorkingDirectory = _working
        };
        var restored = JsonSerializer.Deserialize<AcquisitionWorkItem>(JsonSerializer.Serialize(oldSnapshot, Json), Json)!;

        var outcome = await new AcquisitionStepActivity(new FetchHttpStep())
            .ProcessItemAsync(WorkflowContext(), restored, CancellationToken.None);
        var downloaded = (AcquisitionWorkItem) outcome.Replacement!;

        CollectionAssert.AreEqual(body, await File.ReadAllBytesAsync(downloaded.Files.Single()));
        Assert.AreEqual(server.Url, downloaded.SelectedLink!.Url);
        Assert.AreEqual(0, restored.Links.Count);
    }

    [DataTestMethod]
    [DataRow("https://files.example.r2.dev/work.zip", AcquisitionDriveKind.Cloudflare)]
    [DataRow("https://pan.baidu.com/s/example", AcquisitionDriveKind.Baidu)]
    public void DirectLeadsKeepTheExistingHostClassification(string url, AcquisitionDriveKind expected)
    {
        Assert.AreEqual(expected, RequestedItem(AcquisitionLeadKind.DirectUrl, url).SelectedLink!.DriveKind);
    }

    [TestMethod]
    public async Task DownloadsWholeFile_WithRangeAndWithout()
    {
        var body = Encoding.UTF8.GetBytes(new string('x', 40_000));

        foreach (var supportsRange in new[] {true, false})
        {
            using var server = new Server(body, supportsRange);

            var path = await Downloader().DownloadToDirectory(server.Url,
                Path.Combine(_working, supportsRange ? "with" : "without"), CancellationToken.None);

            Assert.AreEqual("A Great Work.zip", Path.GetFileName(path),
                "the name the server gave it, not the one in the URL");
            CollectionAssert.AreEqual(body, await File.ReadAllBytesAsync(path));
        }
    }

    /// <summary>
    /// Plenty of servers do not say how large a file is. That used to be a null-reference away from
    /// failing the download outright.
    /// </summary>
    [TestMethod]
    public async Task DownloadsAFileTheServerWillNotSizeInAdvance()
    {
        var body = Encoding.UTF8.GetBytes("small but unmeasured");

        using var server = new Server(body, supportsRange: false, declareLength: false);

        var path = await Downloader().DownloadToDirectory(server.Url, _working, CancellationToken.None);

        CollectionAssert.AreEqual(body, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task AnInterruptedDownloadCanBeRetriedWithoutTrustingItsOldPrefix()
    {
        var body = Encoding.UTF8.GetBytes(new string('y', 30_000));

        using var server = new Server(body, supportsRange: true);

        var directory = Path.Combine(_working, "resume");

        Directory.CreateDirectory(directory);
        // What a killed download leaves behind.
        await File.WriteAllBytesAsync(Path.Combine(directory, "A Great Work.zip"), body[..10_000]);

        var path = await Downloader().DownloadToDirectory(server.Url, directory, CancellationToken.None);

        CollectionAssert.AreEqual(body, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task FetchingSkipsALinkNoMachineCanFollow()
    {
        var outcome = await new FetchHttpStep().ExecuteAsync(Context(),
            Item(new AcquisitionLink("https://pan.baidu.com/s/abc", DriveKind: AcquisitionDriveKind.Baidu)),
            CancellationToken.None);

        // Skip rather than Fail: a recipe may put this step in front of the inbox one so the easy
        // case is automatic and the rest still works.
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(outcome);
    }

    [TestMethod]
    public async Task FetchingADirectLinkPutsItInTheWorkingDirectory()
    {
        var body = Encoding.UTF8.GetBytes("the actual file");

        using var server = new Server(body, supportsRange: true);

        var outcome = await new FetchHttpStep().ExecuteAsync(Context(),
            Item(new AcquisitionLink(server.Url, DriveKind: AcquisitionDriveKind.DirectUrl)),
            CancellationToken.None);

        var item = ((AcquisitionStepOutcome.Continue) outcome).Item;

        Assert.AreEqual(1, item.Files.Count);
        CollectionAssert.AreEqual(body, await File.ReadAllBytesAsync(item.Files[0]));
    }
}
