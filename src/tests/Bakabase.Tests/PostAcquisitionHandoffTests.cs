using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Handoffs;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class PostAcquisitionHandoffTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private ThirdPartyOptions Options => _services.GetRequiredService<IBOptions<ThirdPartyOptions>>().Value;
    private PostAcquisitionCompletionObserver Observer => _services.GetRequiredService<PostAcquisitionCompletionObserver>();
    private PostAcquisitionHandoffService Worker => _services.GetRequiredService<PostAcquisitionHandoffService>();

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<Bakabase.Modules.Downloader.Cloud.ISourceAvailabilityProbe>();
            services.AddScoped<PostAcquisitionCompletionObserver>();
            services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
        });
        Options.PostParserAcquisition.Enabled = true;
        _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value.DefaultDownloadPath = Path.GetTempPath();
    }

    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<(PostParserTaskDbModel Post, WorkflowRunDbModel Run, PostParserInput Input)> Saved(
        bool grouped = true, bool two = true, bool complete = true)
    {
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Success,
            OutputCount = 1, StartedAt = DateTime.Now, CompletedAt = DateTime.Now};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var info = new PostDownloadInfo
        {
            IsComplete = complete, Title = "Saved content",
            Groups = grouped ? [new() {Id = "main", Kind = "main", Title = "Main"}] : [],
            Resources = [new() {Link = "https://example.test/first.zip", GroupId = grouped ? "main" : null,
                Code = "access", Password = "archive", Extraction = new() {Requirement = "notRequired"}}]
        };
        if (two) info.Resources.Add(new() {Link = "https://example.test/mirror.zip", GroupId = grouped ? "main" : null});
        var post = new PostParserTaskDbModel {Link = "https://example.test/post", Revision = 3,
            WorkflowRunId = run.Id, Title = "Saved content", ParsingState = complete ? "complete" : "partial",
            Results = JsonSerializer.Serialize(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", info}}, JsonSerializerOptions.Web)};
        Db.PostParserTasks.Add(post); await Db.SaveChangesAsync();
        return (post, run, new() {TaskId = post.Id, Revision = post.Revision, Link = post.Link});
    }

    private async Task<AcquisitionHandoffDbModel> Stage((PostParserTaskDbModel Post, WorkflowRunDbModel Run, PostParserInput Input) saved)
    {
        await Observer.StageAsync(saved.Run, saved.Input, default); await Db.SaveChangesAsync();
        return await Db.AcquisitionHandoffs.SingleAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RevokedReadinessStopsQueuedAndPreparedHandoffsWithoutChangingSavedResults(bool prepared)
    {
        var saved = await Saved();
        var content = new PostContent {MainHtml = "visible download", Locks = [new("https://example.test/proof", 99, false) {BlockId = "proof"}]};
        var state = new Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostParserContentReadinessState
        {
            History = [new() {Version = 1, ContentFingerprint = Bakabase.Modules.PostParser.Services.PostContentReadinessPolicy.Fingerprint(content), BlockIds = ["proof"]}]
        };
        content = state.Apply(content);
        var info = PostParserSavedResults.ReadDownloadInfo(saved.Post.Results!) with {ContentReadiness = content.Readiness};
        saved.Post.Results = JsonSerializer.Serialize(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", info}}, JsonSerializerOptions.Web);
        saved.Post.ContentSnapshotJson = JsonSerializer.Serialize(content, JsonSerializerOptions.Web);
        saved.Post.ContentReadinessJson = JsonSerializer.Serialize(state, JsonSerializerOptions.Web);
        await Db.SaveChangesAsync();
        var originalResult = saved.Post.Results;
        var handoff = await Stage(saved);
        if (prepared)
        {
            var plan = await _services.GetRequiredService<PostAcquisitionPlanAdapter>().CreateAsync(new()
                {TaskId = saved.Post.Id, Revision = saved.Post.Revision, ResourceIndices = [0]}, Path.GetTempPath(), default);
            handoff.AcquisitionTaskId = plan.AcquisitionTaskIds.Single(); handoff.Status = "prepared";
            await Db.SaveChangesAsync();
        }
        var workflow = _services.GetRequiredService<PostParserWorkflowService<BakabaseDbContext>>();
        var view = await workflow.GetContentReadinessAsync(saved.Post.Id);
        await workflow.RevokeContentReadinessAsync(saved.Post.Id, new()
        {
            Revision = view.Revision, WorkflowRunId = view.WorkflowRunId, ExpectedContentFingerprint = view.ContentFingerprint!,
            ExpectedDecisionVersion = view.DecisionVersion, DecisionId = view.ActiveDecisionId!
        });
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("HANDOFF_SOURCE_CHANGED", handoff.Code);
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
        Assert.AreEqual(prepared ? 1 : 0, await Db.AcquisitionTasks.CountAsync());
        Assert.IsFalse(await Db.AcquisitionTasks.AnyAsync(t => t.WorkflowRunId != null));
        Assert.AreEqual(originalResult, (await Db.PostParserTasks.AsNoTracking().SingleAsync()).Results);
    }

    [TestMethod]
    public async Task OnlyNewSuccessfulCompleteSourcesStageAndTheOutboxSharesTheCommit()
    {
        var saved = await Saved();
        Options.PostParserAcquisition.Enabled = false;
        await Observer.StageAsync(saved.Run, saved.Input, default);
        Assert.AreEqual(0, Db.ChangeTracker.Entries<AcquisitionHandoffDbModel>().Count());
        Options.PostParserAcquisition.Enabled = true;
        saved.Run.Status = WorkflowRunStatus.Waiting;
        await Observer.StageAsync(saved.Run, saved.Input, default);
        saved.Run.Status = WorkflowRunStatus.Success; saved.Run.FailedItemCount = 1;
        await Observer.StageAsync(saved.Run, saved.Input, default);
        saved.Run.FailedItemCount = 0;
        await Observer.StageAsync(saved.Run, saved.Input with {Revision = 2}, default);
        Assert.AreEqual(0, Db.ChangeTracker.Entries<AcquisitionHandoffDbModel>().Count());
        await Observer.StageAsync(saved.Run, saved.Input, default);
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.AsNoTracking().CountAsync(), "Observer must not commit independently.");
        await Db.SaveChangesAsync();
        await Observer.StageAsync(saved.Run, saved.Input, default); await Db.SaveChangesAsync();
        Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync());
        Assert.AreEqual(Path.GetTempPath(), (await Db.AcquisitionHandoffs.SingleAsync()).OutputDirectory);
    }

    [TestMethod]
    public async Task HistoricalReadsAndPartialWaitingNeverCreateHandoffs()
    {
        await Saved(complete: false);
        Assert.IsFalse(await Worker.ProcessNextAsync(default));
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.CountAsync());
        var saved = await Db.PostParserTasks.SingleAsync();
        var run = await Db.WorkflowRuns.SingleAsync();
        await Observer.StageAsync(run, new PostParserInput {TaskId = saved.Id, Revision = saved.Revision}, default);
        await Db.SaveChangesAsync();
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.CountAsync());
    }

    [TestMethod]
    public async Task ClearMirrorsCreateOneTaskAndSubmitExactlyOneDynamicRun()
    {
        var saved = await Saved(); var original = saved.Post.Results;
        var handoff = await Stage(saved);
        Assert.IsTrue(await Worker.ProcessNextAsync(default));
        Assert.IsFalse(await Worker.ProcessNextAsync(default));
        var target = await Db.AcquisitionTasks.SingleAsync();
        Assert.IsNotNull(target.WorkflowRunId);
        Assert.AreEqual("created", handoff.Status);
        using var plan = JsonDocument.Parse(target.PlanJson!);
        Assert.AreEqual(1, plan.RootElement.GetProperty("parts").GetArrayLength());
        Assert.AreEqual("0", plan.RootElement.GetProperty("parts")[0].GetProperty("selectedSourceId").GetString());
        Assert.AreEqual("automatic", plan.RootElement.GetProperty("parts")[0].GetProperty("selectionMode").GetString());
        Assert.AreEqual(2, plan.RootElement.GetProperty("parts")[0].GetProperty("sources").GetArrayLength());
        Assert.AreEqual("baidu", plan.RootElement.GetProperty("sourceProviderPriority")[0].GetString());
        Assert.AreEqual(original, (await Db.PostParserTasks.AsNoTracking().SingleAsync()).Results);
        Assert.AreEqual(2, await Db.WorkflowRuns.CountAsync(), "The second run belongs to acquisition, not a reparsed post.");
    }

    [TestMethod]
    public async Task UnknownMultipleSourcesRemainOneCredentialPreservingDraft()
    {
        await Stage(await Saved(grouped: false));
        await Worker.ProcessNextAsync(default);
        var target = await Db.AcquisitionTasks.SingleAsync();
        Assert.IsNull(target.WorkflowRunId); Assert.IsNull(target.PlanJson);
        var detail = await _services.GetRequiredService<AcquisitionPlanService>().GetAsync(target.Id, default);
        Assert.AreEqual(2, detail!.DraftSources.Count);
        Assert.AreEqual("access", detail.DraftSources[0].AccessCode);
        Assert.AreEqual("archive", detail.DraftSources[0].ArchivePassword);
        Assert.IsTrue(detail.Issues.Any(i => i.Code == "SOURCE_SELECTION_REQUIRED"));
        Assert.AreEqual("attention", (await Db.AcquisitionHandoffs.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task MissingDirectoryCreatesDiagnosableTaskWithoutStartingDownloads()
    {
        _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value.DefaultDownloadPath = null;
        var handoff = await Stage(await Saved());
        await Worker.ProcessNextAsync(default);
        Assert.IsNotNull(handoff.AcquisitionTaskId); Assert.AreEqual("attention", handoff.Status);
        Assert.AreEqual("OUTPUT_DIRECTORY_REQUIRED", handoff.Code);
        Assert.IsNull((await Db.AcquisitionTasks.SingleAsync()).WorkflowRunId);
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task DisabledQueuedHandoffCanBeExplicitlyRetriedWithoutDuplicateIntent()
    {
        var handoff = await Stage(await Saved()); Options.PostParserAcquisition.Enabled = false;
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("HANDOFF_DISABLED", handoff.Code); Assert.IsNull(handoff.AcquisitionTaskId);
        Options.PostParserAcquisition.Enabled = true;
        await Worker.RetryAsync(handoff.Id, default); await Worker.ProcessNextAsync(default);
        Assert.AreEqual("created", handoff.Status);
        Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync()); Assert.AreEqual(1, await Db.AcquisitionTasks.CountAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Worker.RetryAsync(handoff.Id, default));
    }

    [TestMethod]
    public async Task ChangedSourceDoesNotStartAnOldHandoff()
    {
        var saved = await Saved(); var handoff = await Stage(saved);
        saved.Post.Revision++; await Db.SaveChangesAsync();
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("HANDOFF_SOURCE_CHANGED", handoff.Code);
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    public async Task ExistingManuallyPreparedTaskIsLinkedWithoutAutoStartingOrDuplicatingIt()
    {
        var saved = await Saved(); var handoff = await Stage(saved);
        var manual = await _services.GetRequiredService<PostAcquisitionPlanAdapter>().CreateAsync(new()
            {TaskId = saved.Post.Id, Revision = saved.Post.Revision, ResourceIndices = [0]}, Path.GetTempPath(), default);
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual(manual.AcquisitionTaskIds.Single(), handoff.AcquisitionTaskId);
        Assert.AreEqual("HANDOFF_EXISTING_TASK", handoff.Code);
        Assert.AreEqual(1, await Db.AcquisitionTasks.CountAsync());
        Assert.IsNull((await Db.AcquisitionTasks.SingleAsync()).WorkflowRunId);
    }

    [TestMethod]
    public void SelectionNeverTreatsMirrorsOrSupplementalToolsAsRequiredParts()
    {
        var info = new PostDownloadInfo {Groups = [new() {Id = "a", Kind = "main"},
            new() {Id = "b", Kind = "main"}, new() {Id = "tool", Kind = "tool"}]};
        var candidates = new List<DownloadCandidate> {new() {Id = "a1", Url = "https://example.test/a1", GroupId = "a"},
            new() {Id = "a2", Url = "https://example.test/a2", GroupId = "a"}, new() {Id = "b1", Url = "https://example.test/b1", GroupId = "b"},
            new() {Id = "tool", Url = "https://example.test/tool", GroupId = "tool"}};
        var groups = PostAcquisitionSelection.Groups(info, candidates)!;
        Assert.AreEqual(2, groups.Count); Assert.AreEqual(2, groups[0].Count);
        candidates.Add(new() {Id = "uncertain"});
        Assert.IsNull(PostAcquisitionSelection.Groups(info, candidates));
        Assert.AreEqual("single", PostAcquisitionSelection.Groups(new(), [new() {Id = "single"}])![0][0]);
    }

    [TestMethod]
    public void LegacyDuplicateLocatorsDoNotCreateAmbiguityButConflictingCredentialsAndGroupsDo()
    {
        var first = new DownloadCandidate {Id = "0", Url = "https://example.test/file", AccessCode = "same"};
        var groups = PostAcquisitionSelection.Groups(new() {Groups = null!}, [first, first with {Id = "1"}]);
        Assert.AreEqual("0", groups![0][0]);
        Assert.IsNull(PostAcquisitionSelection.Groups(new(), [first, first with {Id = "1", AccessCode = "different"}]));
        Assert.IsNull(PostAcquisitionSelection.Groups(new() {Groups = [new() {Id = "main", Kind = "main"},
            new() {Id = "main", Kind = "preview"}]}, [first with {GroupId = "main"}]));
    }

    [TestMethod]
    public async Task PreparedCrashWindowResumesTheSameTaskAndReattachesAnAlreadyStartedRun()
    {
        var saved = await Saved(); var handoff = await Stage(saved);
        var manual = await _services.GetRequiredService<PostAcquisitionPlanAdapter>().CreateAsync(new()
            {TaskId = saved.Post.Id, Revision = saved.Post.Revision, ResourceIndices = [0]}, Path.GetTempPath(), default);
        handoff.AcquisitionTaskId = manual.AcquisitionTaskIds.Single(); handoff.Status = "prepared";
        await Db.SaveChangesAsync();
        await Worker.ProcessNextAsync(default);
        var runId = (await Db.AcquisitionTasks.SingleAsync()).WorkflowRunId;
        Assert.IsNotNull(runId); Assert.AreEqual("created", handoff.Status);
        handoff.Status = "prepared"; await Db.SaveChangesAsync();
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("created", handoff.Status);
        Assert.AreEqual(runId, (await Db.AcquisitionTasks.SingleAsync()).WorkflowRunId);
        Assert.AreEqual(1, await Db.AcquisitionTasks.CountAsync()); Assert.AreEqual(2, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task SourceChangeAfterPreparingPreservesTheTaskButStopsAutomaticSubmission()
    {
        var saved = await Saved(); var handoff = await Stage(saved);
        var manual = await _services.GetRequiredService<PostAcquisitionPlanAdapter>().CreateAsync(new()
            {TaskId = saved.Post.Id, Revision = saved.Post.Revision, ResourceIndices = [0]}, Path.GetTempPath(), default);
        handoff.AcquisitionTaskId = manual.AcquisitionTaskIds.Single(); handoff.Status = "prepared";
        saved.Post.Revision++; await Db.SaveChangesAsync();
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("HANDOFF_SOURCE_CHANGED", handoff.Code);
        Assert.IsNull((await Db.AcquisitionTasks.SingleAsync()).WorkflowRunId);
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task AutomaticEmptyOverrideInheritsAtCompletionAndLaterDefaultChangesDoNotMoveItsPlan()
    {
        Options.PostParserAcquisition.OutputDirectory = "";
        await Stage(await Saved());
        _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value.DefaultDownloadPath =
            Path.Combine(Path.GetTempPath(), "later-default");
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual(Path.GetTempPath(), (await Db.AcquisitionTasks.SingleAsync()).TargetDirectory);
    }

    [TestMethod]
    public async Task ProducerCompletionCapturesAcquisitionRootEvenWhenStorageIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "missing-acquisition-storage-" + Guid.NewGuid().ToString("N"));
        _services.GetRequiredService<IBOptions<Bakabase.Modules.Acquisition.Models.Domain.AcquisitionOptions>>().Value.DownloadRootDirectory = root;
        Options.PostParserAcquisition.OutputDirectory = null;
        var handoff = await Stage(await Saved());
        Assert.AreEqual(root, handoff.OutputDirectory);
        Assert.AreEqual("queued", handoff.Status);
        Assert.IsFalse(Directory.Exists(root));
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }
}
