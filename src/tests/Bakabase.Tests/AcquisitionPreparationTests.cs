using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AcquisitionPreparationTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private readonly PreparationProbe _probe = new();
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private AcquisitionPlanService Plans => _services.GetRequiredService<AcquisitionPlanService>();

    [TestInitialize]
    public async Task Setup()
    {
        _root = Directory.CreateTempSubdirectory("acquisition-preparation-").FullName;
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<ISourceAvailabilityProbe>();
            services.AddSingleton<ISourceAvailabilityProbe>(_probe);
            services.RemoveAll<DownloadMethodRegistry>();
            services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
        });
    }
    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static AcquisitionDraftSource Source(string id) => new()
    {
        Id = id, Url = $"https://example.test/{id}.zip", Kind = "http", AccessCode = "access-" + id,
        ArchivePassword = "archive-" + id, Processing = new() {Requirement = "notRequired"}
    };
    private Task<AcquisitionPlanTaskDetail> Draft(params AcquisitionDraftSource[] sources) =>
        Plans.CreateDraftAsync(new("Prepared resource", sources, _root), default);
    private async Task<string> SavedTask(int id) => JsonSerializer.Serialize(await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == id));

    [TestMethod]
    public async Task UnknownRelationshipsRequireExplicitReviewAndPreviewDoesNotChangeTaskOrStartWork()
    {
        var draft = await Draft(Source("a"), Source("b")); var before = await SavedTask(draft.Id);
        var input = new AcquisitionPreparationInput(draft.Task.PlanRevision, CheckAvailability: true);
        var preview = await Plans.PreviewPreparationAsync(draft.Id, input, default);
        Assert.IsTrue(preview.RequiresRelationshipChoice); Assert.IsFalse(preview.CanConfirm);
        Assert.AreEqual(2, preview.Groups.Single().Sources.Count); Assert.AreEqual(2, _probe.Calls);
        Assert.AreEqual(before, await SavedTask(draft.Id));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id, input, default));
        Assert.AreEqual(before, await SavedTask(draft.Id));
        var confirmed = await Plans.ConfirmPreparationAsync(draft.Id, input with {Relationship = "mirrors", SelectedSourceIds = ["b"]}, default);
        var part = confirmed.Plan!.Parts.Single();
        Assert.AreEqual("b", part.SelectedSourceId); Assert.AreEqual("manual", part.SelectionMode);
        Assert.AreEqual(2, part.Sources.Count); Assert.AreEqual(2, confirmed.DraftSources.Count);
        CollectionAssert.AreEqual(new[] {"archive-a", "archive-b"}, part.Sources.Select(s => s.ArchivePassword).ToArray());
        Assert.AreEqual(2, _probe.Calls, "Confirmation uses cached observations without performing another source check.");
        Assert.AreEqual("prepared", confirmed.Task.BuilderId); Assert.IsNull(confirmed.Task.WorkflowRunId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync()); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length, "Preparation allocates names but creates no payload folders.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id, input with {Relationship = "mirrors"}, default));
    }

    [TestMethod]
    public async Task IndependentSelectionKeepsUnselectedSourcesAndNewCardsCanBeAddedWithoutSavingADraftFirst()
    {
        var draft = await Draft(Source("a"), Source("b"));
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision,
            Sources: [Source("c")], Relationship: "independent", SelectedSourceIds: ["a", "c"]), default);
        Assert.AreEqual(2, prepared.Plan!.Parts.Count);
        Assert.IsTrue(prepared.Plan.Parts.All(p => p.Sources.Count == 1));
        CollectionAssert.AreEquivalent(new[] {"a", "b", "c"}, prepared.DraftSources.Select(s => s.Id).ToArray());
        var preview = await Plans.PreviewPreparationAsync(draft.Id, new(prepared.Task.PlanRevision, CheckAvailability: false), default);
        Assert.IsFalse(preview.RequiresRelationshipChoice);
        Assert.AreEqual(2, preview.Groups.Count(g => g.Required));
        var extra = preview.Groups.Single(g => !g.Required);
        Assert.AreEqual("b", extra.Sources.Single().Candidate.Id); Assert.AreEqual(0, extra.SelectedSourceIds.Count);
        Assert.IsTrue(preview.CanConfirm);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id,
            new(prepared.Task.PlanRevision, SelectedSourceIds: ["a"]), default));
    }

    [TestMethod]
    public async Task ConfirmedInvalidSourcesStayVisibleButNetworkAndUnknownObservationsStaySelectable()
    {
        _probe.Statuses[Source("a").Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        _probe.Statuses[Source("b").Url] = SourceAvailabilityStatuses.TemporarilyUnavailable;
        _probe.Statuses[Source("c").Url] = SourceAvailabilityStatuses.Unknown;
        var draft = await Draft(Source("a"), Source("b"), Source("c"));
        var preview = await Plans.PreviewPreparationAsync(draft.Id, new(draft.Task.PlanRevision, Relationship: "mirrors"), default);
        var candidates = preview.Groups.Single().Sources;
        Assert.AreEqual(3, candidates.Count); Assert.IsFalse(candidates[0].CanSelect);
        Assert.IsTrue(candidates.Skip(1).All(c => c.CanSelect));
        Assert.IsFalse(preview.Groups.Single().RecommendedSourceIds.Contains("a"));
        Assert.IsTrue(preview.CanConfirm);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id,
            new(draft.Task.PlanRevision, Relationship: "mirrors", SelectedSourceIds: ["a"]), default));
        var confirmed = await Plans.ConfirmPreparationAsync(draft.Id,
            new(draft.Task.PlanRevision, Relationship: "mirrors", SelectedSourceIds: ["b"]), default);
        Assert.AreEqual("b", confirmed.Plan!.Parts.Single().SelectedSourceId);
        Assert.IsFalse(confirmed.Issues.Any(i => i.Code == "SOURCE_CONFIRMED_UNAVAILABLE"));
    }

    [TestMethod]
    public async Task PreparedGroupsKeepManualChoicesOtherPartsAndMirrorSpecificProcessingEvidence()
    {
        var original = new WorkflowSourceRef("test", "resource", "44", "1", "a");
        var a = new AcquisitionPlanSource {Id = "a", Url = Source("a").Url, Kind = "http",
            Processing = new() {Requirement = "notRequired"}, ProcessingSource = original, AppliedProcessingRevisionId = "a-review"};
        var b = a with {Id = "b", Url = Source("b").Url, ArchivePassword = "b-password", ProcessingSource = original with {ResultKey = "b"},
            AppliedProcessingRevisionId = "b-review", Processing = new() {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Input = "download"}]}};
        var c = a with {Id = "c", Url = Source("c").Url};
        var plan = new AcquisitionPlan {Title = "Grouped", OutputDirectory = _root, Parts = [
            new() {Id = "main", SelectedSourceId = "a", Sources = [a, b], Processing = a.Processing, ProcessingSource = a.ProcessingSource, AppliedProcessingRevisionId = a.AppliedProcessingRevisionId},
            new() {Id = "second", SelectedSourceId = "c", Sources = [c], Processing = c.Processing, ProcessingSource = c.ProcessingSource}]};
        var prepared = await Plans.CreatePreparedPlanAsync(plan, [original]);
        var preview = await Plans.PreviewPreparationAsync(prepared.Id, new(prepared.Task.PlanRevision, CheckAvailability: false), default);
        Assert.IsFalse(preview.RequiresRelationshipChoice); Assert.AreEqual(2, preview.Groups.Count);
        Assert.AreEqual("a", preview.Groups[0].SelectedSourceIds.Single());
        var revised = await Plans.ConfirmPreparationAsync(prepared.Id,
            new(prepared.Task.PlanRevision, SelectedSourceIds: ["b", "c"]), default);
        var part = revised.Plan!.Parts[0];
        Assert.AreEqual("b", part.SelectedSourceId); Assert.AreEqual("b", part.ProcessingSource!.ResultKey);
        Assert.AreEqual("b-review", part.AppliedProcessingRevisionId); Assert.AreEqual("extractArchive", part.Processing!.Steps.Single().Op);
        Assert.AreEqual(JsonSerializer.Serialize(prepared.Plan!.Parts[1]), JsonSerializer.Serialize(revised.Plan.Parts[1]));
        Assert.AreEqual("a", revised.PlanHistory[0].Plan.Parts[0].SelectedSourceId);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task SavedProducerGroupsAndPerSourceReferencesAreRecoveredWithoutReparsingOrGuessing()
    {
        var a = Source("0"); var b = Source("1");
        var post = new PostParserTaskDbModel {Title = "Original", Link = "https://example.test/post", Revision = 2, ParsingState = "complete", Results = JsonSerializer.Serialize(new
        {
            DownloadInfo = new {isComplete = true, groups = new[] {new {id = "main", title = "Main", kind = "main"}},
                resources = new[] {new {link = a.Url, code = a.AccessCode, password = a.ArchivePassword, extraction = a.Processing, groupId = "main"},
                    new {link = b.Url, code = b.AccessCode, password = b.ArchivePassword, extraction = b.Processing, groupId = "main"}}}
        })};
        Db.PostParserTasks.Add(post); await Db.SaveChangesAsync();
        var origin = new WorkflowSourceRef("postParser", "task", post.Id.ToString(), "2");
        var row = await Plans.CreateDraftRowAsync(new("Saved", [a, b], _root), null, [],
            [origin, origin with {ResultKey = "0"}, origin with {ResultKey = "1"}], null, default);
        var before = await SavedTask(row.Id); var originalPost = JsonSerializer.Serialize(await Db.PostParserTasks.AsNoTracking().SingleAsync());
        var preview = await Plans.PreviewPreparationAsync(row.Id, new(row.PlanRevision, CheckAvailability: false), default);
        Assert.IsFalse(preview.RequiresRelationshipChoice); Assert.AreEqual("single", preview.Groups.Single().SelectionMode);
        Assert.AreEqual(before, await SavedTask(row.Id));
        var prepared = await Plans.ConfirmPreparationAsync(row.Id, new(row.PlanRevision, SelectedSourceIds: ["1"]), default);
        Assert.AreEqual("1", prepared.Plan!.Parts.Single().ProcessingSource!.ResultKey);
        CollectionAssert.AreEqual(new[] {"0", "1"}, prepared.Plan.Parts.Single().Sources.Select(s => s.ProcessingSource!.ResultKey).ToArray());
        Assert.AreEqual(originalPost, JsonSerializer.Serialize(await Db.PostParserTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task ChangedSourceCredentialsKeepProcessingProvenanceButChangedLocatorDoesNotClaimOriginalEvidence()
    {
        var source = new AcquisitionPlanSource {Id = "a", Url = Source("a").Url, Kind = "http", AccessCode = "original",
            Processing = Source("a").Processing, ProcessingSource = new("test", "resource", "4", "1", "a"), AppliedProcessingRevisionId = "review"};
        var prepared = await Plans.CreatePreparedPlanAsync(new() {Title = "Original", OutputDirectory = _root, Parts = [new()
        {Id = "main", Sources = [source], SelectedSourceId = "a", Processing = source.Processing, ProcessingSource = source.ProcessingSource,
            AppliedProcessingRevisionId = source.AppliedProcessingRevisionId}]}, []);
        var edited = await Plans.ConfirmPreparationAsync(prepared.Id, new(prepared.Task.PlanRevision, Sources: [Source("a") with {AccessCode = "corrected", GroupId = "main"}]), default);
        Assert.AreEqual(source.ProcessingSource, edited.Plan!.Parts.Single().ProcessingSource);
        Assert.AreEqual("corrected", edited.Plan.Parts.Single().Sources.Single().AccessCode);
        var moved = await Plans.ConfirmPreparationAsync(prepared.Id, new(edited.Task.PlanRevision, Sources: [Source("a") with {Url = "https://example.test/new.zip", GroupId = "main"}]), default);
        Assert.IsNull(moved.Plan!.Parts.Single().ProcessingSource);
        Assert.IsNull(moved.Plan.Parts.Single().AppliedProcessingRevisionId);
        Assert.AreEqual(source.Url, moved.PlanHistory[0].Plan.Parts.Single().Sources.Single().Url);
    }

    [TestMethod]
    public async Task PastExecutionsPreventPreparationEvenIfTheirCurrentAssociationIsMissing()
    {
        var draft = await Draft(Source("a"));
        Db.WorkflowRuns.Add(new WorkflowRunDbModel {Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = draft.Id.ToString(), Revision = "old"}]});
        await Db.SaveChangesAsync(); var before = await SavedTask(draft.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.PreviewPreparationAsync(draft.Id, new(draft.Task.PlanRevision), default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision), default));
        Assert.AreEqual(before, await SavedTask(draft.Id)); Assert.AreEqual(0, _probe.Calls);
    }

    [TestMethod]
    public async Task AddingIndependentPartsAllocatesNonoverlappingBindingsWithoutCreatingFiles()
    {
        var draft = await Draft(Source("a"));
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision, SelectedSourceIds: ["a"]), default);
        var oldPart = prepared.Plan!.Parts.Single();
        var oldDirectories = prepared.Plan.StageDirectories!.Parts[oldPart.Id];
        var revised = await Plans.ConfirmPreparationAsync(draft.Id, new(prepared.Task.PlanRevision,
            Sources: [Source("b")], SelectedSourceIds: ["a", "b"]), default);
        Assert.AreEqual(2, revised.Plan!.Parts.Count);
        Assert.AreNotEqual(oldDirectories.Download!.Directory, revised.Plan.StageDirectories!.Parts[oldPart.Id].Download!.Directory,
            "The old single-part reservation covered the task folder; independent parts need separate subfolders.");
        Assert.AreEqual(2, revised.Plan.StageDirectories.Parts.Count);
        Assert.AreEqual(2, revised.Plan.DeliveryLayout!.PartDirectories.Count);
        var expanded = await Plans.ConfirmPreparationAsync(draft.Id, new(revised.Task.PlanRevision,
            Sources: [Source("c")], SelectedSourceIds: ["a", "b", "c"]), default);
        foreach (var (id, directories) in revised.Plan.StageDirectories.Parts)
            Assert.AreEqual(directories, expanded.Plan!.StageDirectories!.Parts[id], "Existing multipart reservations remain fixed.");
        Assert.AreEqual(3, expanded.Plan!.Parts.Count);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length);
    }

    [TestMethod]
    public async Task UnreadableAlternativeInstructionsRemainVisibleAndCannotBecomeAnExecutionSelection()
    {
        const string raw = "malformed producer instructions";
        var first = new AcquisitionPlanSource {Id = "a", Url = Source("a").Url, Kind = "http", Processing = Source("a").Processing};
        var bad = first with {Id = "b", Url = Source("b").Url, Processing = null, OriginalProcessingPlanJson = raw,
            ProcessingIssueCode = "SOURCE_PROCESSING_INVALID"};
        var prepared = await Plans.CreatePreparedPlanAsync(new() {Title = "Retained instructions", OutputDirectory = _root,
            Parts = [new() {Id = "main", Sources = [first, bad], SelectedSourceId = "a"}]}, []);
        var preview = await Plans.PreviewPreparationAsync(prepared.Id, new(prepared.Task.PlanRevision, CheckAvailability: false), default);
        var alternative = preview.Groups.Single().Sources.Single(c => c.Candidate.Id == "b");
        Assert.IsFalse(alternative.CanSelect); Assert.AreEqual(raw, alternative.Candidate.ExtractionPlanJson);
        Assert.IsFalse(alternative.CanSelectManually == true);
        var invalidOverride = await Plans.PreviewPreparationAsync(prepared.Id, new(prepared.Task.PlanRevision,
            SelectedSourceIds: ["b"], CheckAvailability: false, AllowUnavailableSourceIds: ["b"]), default);
        Assert.IsFalse(invalidOverride.CanConfirm);
        Assert.IsTrue(preview.CanConfirm);
        var revised = await Plans.ConfirmPreparationAsync(prepared.Id, new(prepared.Task.PlanRevision,
            Sources: [Source("a") with {GroupId = "main", AccessCode = "edited"}, Source("b") with {GroupId = "main", Processing = null}]), default);
        Assert.AreEqual(raw, revised.Plan!.Parts.Single().Sources.Single(s => s.Id == "b").OriginalProcessingPlanJson);
        Assert.AreEqual("notRequired", revised.Plan.Parts.Single().Sources.Single(s => s.Id == "a").Processing!.Requirement);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(prepared.Id,
            new(revised.Task.PlanRevision, SelectedSourceIds: ["b"]), default));
    }

    [TestMethod]
    public async Task LocalFilesProtectAnUnstartedPreparedSourceFromBeingReplaced()
    {
        var draft = await Draft(Source("a"));
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision), default);
        var directory = prepared.Plan!.StageDirectories!.Parts.Values.Single().Download!.Directory;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "retained.bin"), "retained bytes");
        var before = await SavedTask(draft.Id);
        var preview = await Plans.PreviewPreparationAsync(draft.Id, new(prepared.Task.PlanRevision, CheckAvailability: false), default);
        Assert.IsFalse(preview.CanConfirm); Assert.IsTrue(preview.Requirements.Any(r => r.Code == "SOURCE_REPLACEMENT_LOCAL_FILES"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id,
            new(prepared.Task.PlanRevision, Sources: [Source("a") with {Url = Source("b").Url}]), default));
        Assert.AreEqual(before, await SavedTask(draft.Id));
        Assert.AreEqual("retained bytes", await File.ReadAllTextAsync(Path.Combine(directory, "retained.bin")));
    }

    [TestMethod]
    public async Task ExistingRequiredGroupsSupportMoreThanOneHundredTotalSourcesDuringPreviewAndCardEdits()
    {
        var parts = Enumerable.Range(0, 2).Select(group => new AcquisitionPlanPart
        {
            Id = "part-" + group, SelectedSourceId = $"{group}-0", Processing = new() {Requirement = "notRequired"},
            Sources = Enumerable.Range(0, 60).Select(index => new AcquisitionPlanSource
            {Id = $"{group}-{index}", Url = $"https://example.test/{group}-{index}.zip", Kind = "http",
                Processing = new() {Requirement = "notRequired"}}).ToList()
        }).ToList();
        var prepared = await Plans.CreatePreparedPlanAsync(new() {Title = "Many mirrors", OutputDirectory = _root, Parts = parts}, []);
        var preview = await Plans.PreviewPreparationAsync(prepared.Id, new(prepared.Task.PlanRevision, CheckAvailability: false), default);
        Assert.AreEqual(120, preview.Groups.Sum(g => g.Sources.Count)); Assert.IsTrue(preview.CanConfirm);
        var edits = preview.Groups.SelectMany(g => g.Sources.Select(c => new AcquisitionDraftSource
        {
            Id = c.Candidate.Id, GroupId = g.Id, Url = c.Candidate.Url, Kind = c.Candidate.Kind,
            Processing = new() {Requirement = "notRequired"}, ArchivePassword = c.Candidate.Id == "0-0" ? "corrected" : null
        })).ToList();
        var confirmed = await Plans.ConfirmPreparationAsync(prepared.Id,
            new(prepared.Task.PlanRevision, Sources: edits, SelectedSourceIds: ["0-0", "1-0"]), default);
        Assert.AreEqual(120, confirmed.Plan!.Parts.Sum(p => p.Sources.Count));
        Assert.AreEqual("corrected", confirmed.Plan.Parts[0].Sources[0].ArchivePassword);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task PreparationCanExpandBeforeTheDownloadRootIsConfigured()
    {
        var draft = await Plans.CreateDraftAsync(new("Unconfigured", [Source("a")], ""), default);
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision), default);
        Assert.IsNull(prepared.Plan!.StageDirectories);
        var expanded = await Plans.ConfirmPreparationAsync(draft.Id,
            new(prepared.Task.PlanRevision, Sources: [Source("b")], SelectedSourceIds: ["a", "b"]), default);
        Assert.AreEqual(2, expanded.Plan!.Parts.Count); Assert.IsNull(expanded.Plan.StageDirectories);
        Assert.IsTrue(expanded.Issues.Any(i => i.Code == "OUTPUT_DIRECTORY_REQUIRED"));
        Assert.IsNull(expanded.Task.WorkflowRunId);
    }

    [TestMethod]
    public async Task ExplicitUnavailableChoicePreservesEvidenceAndReachesStartWithoutAutomaticFallback()
    {
        _probe.Statuses[Source("a").Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        var draft = await Draft(Source("a"), Source("b"));
        var initial = await Plans.PreviewPreparationAsync(draft.Id, new(draft.Task.PlanRevision, Relationship: "mirrors"), default);
        var invalid = initial.Groups.Single().Sources.Single(c => c.Candidate.Id == "a");
        Assert.IsFalse(invalid.CanSelect); Assert.IsTrue(invalid.CanSelectManually == true);
        CollectionAssert.AreEqual(new[] {"b"}, initial.Groups.Single().RecommendedSourceIds.ToArray());
        var manualInput = new AcquisitionPreparationInput(draft.Task.PlanRevision, Relationship: "mirrors", SelectedSourceIds: ["a"], AllowUnavailableSourceIds: ["a"]);
        var preview = await Plans.PreviewPreparationAsync(draft.Id, manualInput, default);
        Assert.IsTrue(preview.CanConfirm); Assert.IsFalse(preview.Groups.Single().Sources[0].CanSelect);
        Assert.IsTrue(preview.Groups.Single().Sources[0].Requirements.Any(r => r.Code == "SOURCE_CONFIRMED_UNAVAILABLE"));
        CollectionAssert.AreEqual(new[] {"b"}, preview.Groups.Single().RecommendedSourceIds.ToArray());
        CollectionAssert.AreEqual(new[] {"a"}, preview.AllowUnavailableSourceIds.ToArray());
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, manualInput, default);
        var part = prepared.Plan!.Parts.Single(); var selected = part.Sources.Single(s => s.Id == "a");
        Assert.AreEqual("manual", part.SelectionMode);
        Assert.IsTrue(AcquisitionSourceAvailabilityOverrides.Matches(selected, draft.Id));
        Assert.IsNull(part.Sources.Single(s => s.Id == "b").ManualAvailabilityOverride);
        var reopened = await Plans.PreviewPreparationAsync(draft.Id, new(prepared.Task.PlanRevision, CheckAvailability: false), default);
        Assert.IsTrue(reopened.CanConfirm); Assert.IsFalse(reopened.Groups.Single().Sources[0].CanSelect);
        CollectionAssert.AreEqual(new[] {"a"}, reopened.AllowUnavailableSourceIds.ToArray());
        CollectionAssert.AreEqual(new[] {"b"}, reopened.Groups.Single().RecommendedSourceIds.ToArray());
        var preflight = await Plans.PreflightAsync(draft.Id, default);
        Assert.IsFalse(preflight.Issues.Any(i => i.Code == "SOURCE_CONFIRMED_UNAVAILABLE"));
        var started = await Plans.RunAsync(draft.Id, prepared.Task.PlanRevision, default);
        Assert.IsNotNull(started.Task.WorkflowRunId, string.Join(",", started.Issues.Select(i => i.Code)));
        Assert.AreEqual("a", started.Plan!.Parts.Single().SelectedSourceId);
        var observation = await _services.GetRequiredService<SourceAvailabilityService>().GetAsync(invalid.ResolvedSource!, default);
        Assert.AreEqual(SourceAvailabilityStatuses.ConfirmedUnavailable, observation!.Status);
        Assert.AreEqual(JsonSerializer.Serialize(invalid.Availability), JsonSerializer.Serialize(observation), "A manual choice never rewrites the health evidence.");
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
    }

    [DataTestMethod]
    [DataRow("url")]
    [DataRow("access")]
    [DataRow("archive")]
    [DataRow("provider")]
    [DataRow("method")]
    [DataRow("account")]
    public async Task EditingTheExactSourceInvalidatesItsStoredUnavailableOverride(string field)
    {
        _probe.Statuses[Source("a").Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        _probe.Statuses[Source("b").Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        var draft = await Draft(Source("a"));
        await Plans.PreviewPreparationAsync(draft.Id, new(draft.Task.PlanRevision), default);
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision, SelectedSourceIds: ["a"], AllowUnavailableSourceIds: ["a"]), default);
        var source = Source("a") with {GroupId = prepared.Plan!.Parts.Single().Id};
        source = field switch
        {
            "url" => source with {Url = Source("b").Url}, "access" => source with {AccessCode = "corrected"},
            "archive" => source with {ArchivePassword = "corrected"}, "provider" => source with {ProviderId = "http"},
            "method" => source with {MethodId = "http.native"}, _ => source with {AccountId = "changed-account"}
        };
        var input = new AcquisitionPreparationInput(prepared.Task.PlanRevision, Sources: [source]);
        var preview = await Plans.PreviewPreparationAsync(draft.Id, input, default);
        Assert.IsFalse(preview.CanConfirm); Assert.AreEqual(0, preview.AllowUnavailableSourceIds.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id, input, default));
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task ExplicitlyClearingOrUnselectingAManualOverrideDoesNotMakeItADefaultRecommendation()
    {
        _probe.Statuses[Source("a").Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        var draft = await Draft(Source("a"), Source("b"));
        await Plans.PreviewPreparationAsync(draft.Id, new(draft.Task.PlanRevision, Relationship: "mirrors"), default);
        var prepared = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision, Relationship: "mirrors", SelectedSourceIds: ["a"], AllowUnavailableSourceIds: ["a"]), default);
        var cleared = await Plans.PreviewPreparationAsync(draft.Id, new(prepared.Task.PlanRevision, AllowUnavailableSourceIds: []), default);
        Assert.IsFalse(cleared.CanConfirm); Assert.AreEqual(0, cleared.AllowUnavailableSourceIds.Count);
        var switched = await Plans.ConfirmPreparationAsync(draft.Id, new(prepared.Task.PlanRevision,
            SelectedSourceIds: ["b"], AllowUnavailableSourceIds: []), default);
        Assert.IsTrue(switched.Plan!.Parts.Single().Sources.All(s => s.ManualAvailabilityOverride == null));
        var invalidAgain = await Plans.PreviewPreparationAsync(draft.Id,
            new(switched.Task.PlanRevision, SelectedSourceIds: ["a"], CheckAvailability: false), default);
        Assert.IsFalse(invalidAgain.CanConfirm);
    }

    [TestMethod]
    public async Task UnavailableOverridesCannotBeInjectedThroughLegacyPreparationOrImportedPlans()
    {
        _probe.Statuses[Source("a").Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        var draft = await Draft(Source("a"));
        await Plans.PreviewPreparationAsync(draft.Id, new(draft.Task.PlanRevision), default);
        var confirmed = await Plans.ConfirmPreparationAsync(draft.Id, new(draft.Task.PlanRevision,
            SelectedSourceIds: ["a"], AllowUnavailableSourceIds: ["a"]), default);
        var legacy = await Plans.PrepareDraftAsync(draft.Id, new(confirmed.Task.PlanRevision, confirmed.Plan!), default);
        Assert.IsNull(legacy.Plan!.Parts.Single().Sources.Single().ManualAvailabilityOverride);
        Assert.IsTrue(legacy.Issues.Any(i => i.Code == "SOURCE_CONFIRMED_UNAVAILABLE"));
        var imported = await Plans.CreatePreparedPlanAsync(confirmed.Plan!, []);
        Assert.IsNull(imported.Plan!.Parts.Single().Sources.Single().ManualAvailabilityOverride);
        const string payload = "{\"allowedUnavailableSourceFingerprints\":{\"a\":\"injected\"}}";
        Assert.AreEqual(0, JsonSerializer.Deserialize<DownloadIntentRequest>(payload, JsonSerializerOptions.Web)!.AllowedUnavailableSourceFingerprints.Count);
        Assert.AreEqual(0, Newtonsoft.Json.JsonConvert.DeserializeObject<DownloadIntentRequest>(payload)!.AllowedUnavailableSourceFingerprints.Count);
    }

    [DataTestMethod]
    [DataRow("unsupported")]
    [DataRow("authentication")]
    [DataRow("disabled")]
    public async Task ExplicitOverridesNeverBypassOtherSourceBlockers(string blocker)
    {
        var source = blocker == "unsupported" ? Source("a") with {Url = "https://docs.google.com/document/d/example/edit", Kind = "auto"} : Source("a");
        _probe.Statuses[source.Url] = blocker == "authentication" ? SourceAvailabilityStatuses.AccessRequired : SourceAvailabilityStatuses.ConfirmedUnavailable;
        if (blocker == "authentication") _probe.ReasonCodes[source.Url] = "SOURCE_ACCESS_CODE_REJECTED";
        var draft = await Draft(source);
        if (blocker == "disabled")
        {
            var normalized = DownloadMethodRegistry.Normalize(new() {Id = source.Id, Url = source.Url, Kind = source.Kind, AccessCode = source.AccessCode});
            await _services.GetRequiredService<SourceAvailabilityStore>().DecideAsync(
                SourceAvailabilityService.DecisionKey($"acquisition:{draft.Id}", normalized), null,
                new("decision", true, DateTimeOffset.UtcNow, "fixture", null), default);
        }
        var input = new AcquisitionPreparationInput(draft.Task.PlanRevision, SelectedSourceIds: ["a"], AllowUnavailableSourceIds: ["a"]);
        var preview = await Plans.PreviewPreparationAsync(draft.Id, input, default);
        Assert.IsFalse(preview.CanConfirm); Assert.IsFalse(preview.Groups.Single().Sources.Single().CanSelectManually == true);
        Assert.AreEqual(0, preview.AllowUnavailableSourceIds.Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ConfirmPreparationAsync(draft.Id, input, default));
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task RuntimeOverrideIsBoundToTheResolvedAccountGenerationAndCannotBeSuppliedAsAnotherFingerprint()
    {
        var raw = new AcquisitionPlanSource {Id = "a", Url = Source("a").Url, Kind = "http"};
        var resolved = new CloudSource {Id = "a", Url = raw.Url, Kind = "http", ProviderId = "http", MethodId = "http.native", AccountId = "default-account", AccountVersion = "generation-one"};
        raw = raw with {ManualAvailabilityOverride = AcquisitionSourceAvailabilityOverrides.Create(42, raw, resolved)};
        Assert.IsTrue(AcquisitionSourceAvailabilityOverrides.Matches(raw, 42, resolved));
        Assert.IsFalse(AcquisitionSourceAvailabilityOverrides.Matches(raw, 43, resolved));
        Assert.IsFalse(AcquisitionSourceAvailabilityOverrides.Matches(raw, 42, resolved with {AccountId = "new-default"}));
        Assert.IsFalse(AcquisitionSourceAvailabilityOverrides.Matches(raw, 42, resolved with {AccountVersion = "generation-two"}));
        Assert.IsFalse(AcquisitionSourceAvailabilityOverrides.Matches(raw, 42, resolved with {MethodId = "different-engine"}));
        _probe.Statuses[raw.Url] = SourceAvailabilityStatuses.ConfirmedUnavailable;
        var intents = _services.GetRequiredService<DownloadIntentService>();
        var request = new DownloadIntentRequest {Candidates = [new() {Id = "a", Url = raw.Url, Kind = "http"}], SelectedCandidateIds = ["a"],
            CheckSourceAvailability = true, AllowedUnavailableSourceFingerprints = new Dictionary<string, string> {{"a", raw.ManualAvailabilityOverride.ResolvedSourceFingerprint}}};
        var rejected = await intents.PreflightAsync(request, _root, default);
        Assert.IsFalse(rejected.CanSubmit, "The public HTTP source has no matching resolved account generation.");
        var actual = rejected.Candidates.Single().ResolvedSource!;
        var accepted = await intents.PreflightAsync(request with {AllowedUnavailableSourceFingerprints = new Dictionary<string, string>
            {{"a", AcquisitionSourceAvailabilityOverrides.ResolvedFingerprint(actual)}}}, _root, default);
        Assert.IsTrue(accepted.CanSubmit);
        Assert.AreEqual(SourceAvailabilityStatuses.ConfirmedUnavailable, accepted.Candidates.Single().Availability!.Status);
    }

    private sealed class PreparationProbe : ISourceAvailabilityProbe
    {
        public Dictionary<string, string> Statuses { get; } = [];
        public Dictionary<string, string> ReasonCodes { get; } = [];
        public int Calls { get; private set; }
        public bool CanHandle(CloudSource source) => true;
        public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new SourceAvailabilityResult {Status = Statuses.GetValueOrDefault(source.Url, SourceAvailabilityStatuses.Available),
                ReasonCode = ReasonCodes.GetValueOrDefault(source.Url, "TEST_OBSERVATION"), ProbeId = "preparation-test", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)});
        }
    }
}
