using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Independent test databases reuse IDs; task execution gates are process-wide.
public sealed class AcquisitionPlanServiceTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private FakeBuilder _builder = null!;
    private AcquisitionPlanService _plans = null!;
    private readonly SourceProbe _availabilityProbe = new();

    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<Bakabase.Modules.Downloader.Cloud.ISourceAvailabilityProbe>();
            services.AddSingleton<Bakabase.Modules.Downloader.Cloud.ISourceAvailabilityProbe>(_availabilityProbe);
            services.AddScoped(_ => new Bakabase.Service.Components.DownloadIntents.DownloadMethodRegistry([], [], null!, null!));
        });
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _builder = new(_db);
        _plans = new(_db, [_builder], _services.GetRequiredService<IAcquisitionService>(),
            _services.GetRequiredService<IWorkflowDefinitionService>(), _services.GetRequiredService<Bakabase.Service.Components.DownloadIntents.DownloadIntentService>(), _services.GetRequiredService<IUserStoragePolicy>());
    }

    [TestMethod]
    public async Task ConfirmingDecodedSourceReplacesOnlySelectedLocatorAndKeepsSourceInstructionsAndHistory()
    {
        var root = Directory.CreateTempSubdirectory("acquisition-image-").FullName;
        try
        {
            var snapshot = new WorkflowSourceRef("postParser", "task", "47", "1", "0");
            var plan = _builder.BuildPlan(new("image", "Picture", "https://example.org/post.png"), "QR", root);
            var original = plan.Parts.Single();
            var image = original.Sources.Single() with {ArchivePassword = "posted-password", ProcessingSource = snapshot};
            plan = plan with {Parts = [original with {Sources = [image], ProcessingSource = snapshot},
                original with {Id = "second", SelectedSourceId = "other", Sources = [image with {Id = "other", Url = "https://elsewhere.invalid/else.zip"}]}]};
            var created = await _plans.CreatePreparedPlanAsync(plan, [snapshot]);
            var request = new AcquisitionPlanUpdateInput(created.Task.PlanRevision, SourceReplacement:
                new("main", "image", "https://example.org/game.7z", "1234"));
            var revised = await _plans.UpdateAsync(created.Id, request, default);
            var part = revised.Plan!.Parts[0];
            Assert.AreEqual(2, part.Sources.Count);
            var chosen = part.Sources.Single(s => s.Id == part.SelectedSourceId);
            Assert.AreEqual("https://example.org/game.7z", chosen.Url);
            Assert.AreEqual("http.native", chosen.MethodId);
            Assert.AreEqual("1234", chosen.AccessCode);
            Assert.AreEqual(image.ArchivePassword, chosen.ArchivePassword);
            Assert.AreEqual(snapshot, chosen.ProcessingSource);
            Assert.AreEqual(snapshot, part.ProcessingSource);
            Assert.AreEqual(image.Url, part.Sources.Single(s => s.Id == image.Id).Url);
            Assert.AreEqual("other", revised.Plan.Parts[1].SelectedSourceId);
            Assert.IsTrue(revised.PlanHistory.Any(h => h.Plan.Parts[0].SelectedSourceId == image.Id));
            Assert.IsNull(revised.Task.WorkflowRunId);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _plans.UpdateAsync(created.Id, request, default));

            var stage = revised.Plan.StageDirectories!.Parts["main"].Download!.Directory;
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "user-file.txt"), "untouched");
            var second = new AcquisitionPlanUpdateInput(revised.Task.PlanRevision,
                SourceReplacement: new("main", chosen.Id, "https://example.org/different.7z"));
            var rejected = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _plans.UpdateAsync(created.Id, second, default));
            Assert.AreEqual("SOURCE_REPLACEMENT_LOCAL_FILES", rejected.Message);
            Assert.AreEqual("untouched", File.ReadAllText(Path.Combine(stage, "user-file.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task PreparedTasksAllocateSeparateStageDirectoriesWithoutCreatingOrTrustingProducerFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "stage-allocation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Same title", root) with
            {
                StageDirectories = new() {TaskId = 999, Parts = new() { ["injected"] = new()
                    {Download = new(root, "exclusive", "producer")}}}
            };
            var first = await _plans.CreatePreparedPlanAsync(plan, []);
            var second = await _plans.CreatePreparedPlanAsync(plan, []);
            var left = first.Plan!.StageDirectories!;
            var right = second.Plan!.StageDirectories!;
            Assert.AreEqual(first.Id, left.TaskId);
            var paths = left.Parts.Values.Single();
            Assert.IsNotNull(paths.Download!.Allocation);
            Assert.IsNotNull(paths.Processing!.Allocation);
            Assert.AreNotEqual(paths.Download.Directory, paths.Processing.Directory);
            Assert.AreNotEqual(paths.Download.Directory, paths.Delivery!.Directory);
            Assert.AreNotEqual(paths.Download.Directory, right.Parts.Values.Single().Download!.Directory);
            Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length, "Saving plans must not create payload folders.");
            var persisted = await _plans.GetAsync(first.Id, default);
            Assert.AreEqual(paths.Download.Directory, persisted!.StageDirectories!.Parts.Values.Single().Download!.Directory);
            Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
        }
        finally {Directory.Delete(root, true);}
    }

    [TestMethod]
    public async Task HistoricalStageBindingsUseRecordedDirectoriesWithoutRewritingPlanOrFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "stage-bindings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "manual-file.txt");
            await File.WriteAllTextAsync(file, "user content");
            var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Legacy", root);
            var partId = plan.Parts.Single().Id;
            var row = new AcquisitionTaskDbModel {Title = plan.Title, BuilderId = "prepared", Phase = "attention",
                PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, System.Text.Json.JsonSerializerOptions.Web),
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
            _db.AcquisitionTasks.Add(row); await _db.SaveChangesAsync();
            var workset = AcquisitionPlanCompiler.Workset(row.Id, 1, plan, []);
            workset = workset with {Targets = workset.Targets.Select(t => t with {DownloadDirectory = root,
                ProcessedDirectory = Path.Combine(root, "existing-output"), PublishedDirectory = Path.Combine(root, "delivered")}).ToList()};
            var run = new Bakabase.Modules.Workflow.Abstractions.Models.Db.WorkflowRunDbModel
            {StartedAt = DateTime.Now, Status = Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants.WorkflowRunStatus.Failed,
                CurrentItemJson = Bakabase.Modules.Workflow.Components.WorkflowItemSnapshot.Capture(workset)};
            _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
            row.WorkflowRunId = run.Id; await _db.SaveChangesAsync();
            var before = row.PlanJson;
            var detail = await _plans.GetAsync(row.Id, default);
            var dirs = detail!.StageDirectories!.Parts[partId];
            Assert.AreEqual(root, dirs.Download!.Directory);
            Assert.AreEqual(Path.Combine(root, "existing-output"), dirs.Processing!.Directory);
            Assert.AreEqual(Path.Combine(root, "delivered"), dirs.Delivery!.Directory);
            Assert.AreEqual("borrowed", dirs.Download.Ownership);
            Assert.IsNull(dirs.Download.Allocation);
            Assert.IsNull(detail.Plan!.StageDirectories, "Read-time bindings do not rewrite frozen historical plans.");
            Assert.AreEqual(before, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == row.Id)).PlanJson);
            Assert.AreEqual("user content", await File.ReadAllTextAsync(file));
            Assert.AreEqual(1, Directory.GetFileSystemEntries(root).Length);
        }
        finally {Directory.Delete(root, true);}
    }

    [TestMethod]
    public async Task SearchPersistsOneIndependentTaskBeforeNetworkAndRequiresExplicitSelection()
    {
        var result = await _plans.CreateAsync(new("Requested title", "test.search"), default);
        Assert.IsTrue(_builder.SawDurablePreparingTask);
        Assert.IsNull(result.Task.ResourceId);
        Assert.IsNull(result.Task.WorkflowRunId);
        Assert.IsNull(result.Plan, "A search result must not silently select the first hit.");
        Assert.AreEqual(2, result.Candidates.Count);
        var selected = await _plans.SelectAsync(result.Id, new("two", result.Task.PlanRevision), default);
        Assert.AreEqual(result.Id, selected.Id);
        Assert.AreEqual("https://example.org/two.zip", selected.Plan!.Parts.Single().Sources.Single().Url);
        Assert.AreEqual("currentDirectory", selected.Plan.ExtractionLayout,
            "The first real plan captures new extraction defaults even after the search has already incremented its revision.");
        Assert.IsNull(selected.Task.WorkflowRunId, "Selection prepares a plan without starting execution.");
        Assert.IsTrue(selected.SourceRefs.Any(s => s.Module == "acquisition" && s.EntityId == result.Id.ToString()));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _plans.SelectAsync(result.Id, new("one", result.Task.PlanRevision), default));
    }

    [TestMethod]
    public async Task RecoveryOfUnstartedPreparedPlanIsReadonlyAndBindsSelectedInputs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "prepared-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", directory);
            var created = await _plans.CreatePreparedPlanAsync(plan, []);
            var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            var before = System.Text.Json.JsonSerializer.Serialize(task);
            var ready = await _plans.CheckUnstartedRecoveryAsync(task, default);
            Assert.IsNull(ready.Problem); Assert.IsNotNull(ready.Inputs);
            Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)));
            Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
            Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
            var changed = plan with {OutputDirectory = Path.Combine(directory, "changed")};
            task.PlanJson = System.Text.Json.JsonSerializer.Serialize(changed, System.Text.Json.JsonSerializerOptions.Web);
            Assert.AreNotEqual(ready.Inputs, (await _plans.CheckUnstartedRecoveryAsync(task, default)).Inputs);
        }
        finally {Directory.Delete(directory, true);}
    }

    [TestMethod]
    public async Task RecoveryNeverCreatesAFirstRunWhenItsHistoricalAssociationWasLost()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Retained", Path.GetTempPath());
        var created = await _plans.CreatePreparedPlanAsync(plan, []);
        _db.WorkflowRuns.Add(new Bakabase.Modules.Workflow.Abstractions.Models.Db.WorkflowRunDbModel
        {StartedAt = DateTime.Now, Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = created.Id.ToString(), Revision = "older"}]});
        await _db.SaveChangesAsync();
        var task = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        Assert.IsNull(task.WorkflowRunId);
        Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", (await _plans.CheckUnstartedRecoveryAsync(task, default)).Problem);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task NoResultsAndSearchFailureRemainRecoverableOnTheSameTask()
    {
        _builder.Results = [];
        var empty = await _plans.CreateAsync(new("No hits", "test.search"), default);
        Assert.IsTrue(empty.Issues.Any(i => i.Code == "BUILDER_NO_RESULTS"));
        _builder.Fail = true;
        var failed = await _plans.SearchAsync(empty.Id, "retry", default);
        Assert.AreEqual(empty.Id, failed.Id);
        Assert.IsTrue(failed.Issues.Any(i => i.Code == "BUILDER_SEARCH_FAILED"));
        Assert.IsTrue(failed.Issues.All(i => !i.Message.Contains("private token")));
        Assert.IsNull(failed.Task.WorkflowRunId);
        _builder.Fail = false; _builder.Results = [new("one", "One", "https://example.org/one.zip")];
        var recovered = await _plans.SearchAsync(empty.Id, "new query", default);
        Assert.AreEqual(1, recovered.Candidates.Count);
        Assert.AreEqual(3, recovered.PreparationAttempts.Count);
        CollectionAssert.AreEqual(new[] {"BUILDER_NO_RESULTS", "BUILDER_SEARCH_FAILED", null}, recovered.PreparationAttempts.Select(a => a.Code).ToArray());
        Assert.IsTrue(recovered.PreparationAttempts.All(a => a.CompletedAt != null));
        Assert.AreEqual(1, await _db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    public async Task PreparedInputIsGenericAndSourceLookupIncludesTasksThatHaveNoRun()
    {
        var source = new WorkflowSourceRef("customModule", "entry", "41", "2");
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared title", "");
        var result = await _plans.CreatePreparedPlanAsync(plan, [source]);
        Assert.AreEqual("prepared", result.Task.BuilderId);
        Assert.IsNull(result.Task.ResourceId); Assert.IsNull(result.Task.WorkflowRunId);
        var lookup = await _plans.BySourcesAsync([source, source with {Revision = "3"}], default);
        Assert.AreEqual(result.Id, lookup[0].Tasks.Single().Id);
        Assert.AreEqual(0, lookup[1].Tasks.Count);
        Assert.IsTrue(result.Issues.Any(i => i.Code == "OUTPUT_DIRECTORY_REQUIRED"));
        Assert.IsTrue((await _db.AcquisitionTasks.FindAsync(result.Id))!.PlanHistoryJson!.Contains("Prepared title"));
    }

    [TestMethod]
    public async Task InterruptedAssociationReattachesTheExistingPlanRunInsteadOfStartingAnother()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", "");
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var run = new Bakabase.Modules.Workflow.Abstractions.Models.Db.WorkflowRunDbModel
        {
            WorkflowDefinitionId = 0, Status = Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants.WorkflowRunStatus.Success,
            StartedAt = DateTime.Now, Sources = [new()
            {Module = "acquisition", EntityKind = "task", EntityId = prepared.Id.ToString(), Revision = prepared.Task.PlanRevision.ToString()}]
        };
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var recovered = await _plans.RunAsync(prepared.Id, prepared.Task.PlanRevision, default);
        Assert.AreEqual(run.Id, recovered.Task.WorkflowRunId);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual(Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Completed, recovered.Task.Status);
    }

    [TestMethod]
    public async Task RecheckingPreparationIssuesNeverStartsExecution()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", "");
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var first = await _plans.PreflightAsync(prepared.Id, default);
        Assert.IsTrue(first.Issues.Any(i => i.Code == "OUTPUT_DIRECTORY_REQUIRED"));
        var second = await _plans.PreflightAsync(prepared.Id, default);
        Assert.AreEqual(first.Task.PlanRevision, second.Task.PlanRevision);
        Assert.IsNull(second.Task.WorkflowRunId);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task PreparedDetailsUseTheRegisteredCloudDownloadTranslationKeys()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://pan.baidu.com/s/example"), "Prepared", "");
        var part = plan.Parts.Single();
        plan = plan with {Parts = [part with {Sources = [part.Sources.Single() with {Kind = "share", ProviderId = "baidu"}]}]};
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var expected = new Dictionary<string, string>
        {
            ["download.selectSources"] = "cloudDownload.activity.download.selectSources.name",
            ["baidu.transfer"] = "cloudDownload.activity.baidu.transfer.name",
            ["download.enqueueSupported"] = "cloudDownload.activity.download.enqueueSupported.name",
            ["download.awaitCompletion"] = "cloudDownload.activity.download.awaitCompletion.name"
        };
        foreach (var (kind, key) in expected)
            Assert.AreEqual(key, prepared.Steps.Single(s => s.Kind == kind).LabelKey);
    }

    [DataTestMethod]
    [DataRow("required", "PROCESSING_STEPS_MISSING")]
    [DataRow("unknown", "PROCESSING_CONFIRMATION_REQUIRED")]
    [DataRow("notRequired", null)]
    public async Task EmptyProcessingInstructionsExplainConfirmationBeforeExecution(string requirement, string? expectedCode)
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", "");
        plan = plan with {Parts = [plan.Parts.Single() with {Processing = new() {Requirement = requirement}}]};
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var issue = prepared.Issues.SingleOrDefault(i => i.Code is "PROCESSING_CONFIRMATION_REQUIRED" or "PROCESSING_STEPS_MISSING");
        Assert.AreEqual(expectedCode, issue?.Code);
        if (issue != null) Assert.IsFalse(issue.Blocking, "The download may start; processing later waits for confirmation.");
        Assert.IsNull(prepared.Task.WorkflowRunId);
    }

    [TestMethod]
    public async Task AutomaticFallbackBeforeFirstRunPreservesPlanHistoryAndCompilesMirrorSpecificSteps()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", Path.GetTempPath());
        var primary = plan.Parts.Single();
        plan = plan with {SourceProviderPriority = ["http"], Parts = [primary with {SelectionMode = "automatic", Sources =
            [primary.Sources.Single() with {Processing = primary.Processing}, new() {Id = "mirror", Url = "https://example.org/mirror.zip", Kind = "http",
                ArchivePassword = "mirror-password", Processing = new() {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Input = "download"}]}}]}]};
        _availabilityProbe.Unavailable.Add("https://example.org/one.zip");
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var result = await _plans.RunAsync(prepared.Id, prepared.Task.PlanRevision, default);
        Assert.IsNotNull(result.Task.WorkflowRunId);
        Assert.AreEqual(2, result.Task.PlanRevision);
        Assert.AreEqual("mirror", result.Plan!.Parts.Single().SelectedSourceId);
        Assert.AreEqual("one", result.PlanHistory[0].Plan.Parts.Single().SelectedSourceId);
        Assert.AreEqual("sourceSelection.preStartFallback", result.PlanHistory[1].Reason);
        Assert.AreEqual("required", result.Plan.Parts.Single().Processing!.Requirement);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual(2, result.Plan.Parts.Single().Sources.Count);
        Assert.IsTrue(result.Steps.Any(s => s.Operation == "extractArchive"));
    }

    [TestMethod]
    public async Task ManualSourceFailureDoesNotSwitchToPreservedMirror()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", Path.GetTempPath());
        plan = plan with {Parts = [plan.Parts.Single() with {Sources = [plan.Parts.Single().Sources.Single(),
            new() {Id = "mirror", Url = "https://example.org/mirror.zip", Kind = "http"}]}]};
        _availabilityProbe.Unavailable.Add("https://example.org/one.zip");
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var result = await _plans.RunAsync(prepared.Id, prepared.Task.PlanRevision, default);
        Assert.IsNull(result.Task.WorkflowRunId);
        Assert.AreEqual("one", result.Plan!.Parts.Single().SelectedSourceId);
        Assert.AreEqual(1, result.Task.PlanRevision);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task AProcessingReplanNeverChecksOrSwitchesSourcesBeforeExistingArtifactIdentityValidation()
    {
        var plan = _builder.BuildPlan(new("one", "One", "https://example.org/one.zip"), "Prepared", Path.GetTempPath());
        plan = plan with {Parts = [plan.Parts.Single() with {SelectionMode = "automatic", Sources = [plan.Parts.Single().Sources.Single(),
            new() {Id = "mirror", Url = "https://example.org/mirror.zip", Kind = "http"}]}]};
        _availabilityProbe.Unavailable.Add("https://example.org/one.zip");
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var row = (await _db.AcquisitionTasks.FindAsync(prepared.Id))!;
        var history = System.Text.Json.JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(row.PlanHistoryJson!, System.Text.Json.JsonSerializerOptions.Web)!;
        history[0] = history[0] with {PreviousRunId = 987654};
        row.PlanHistoryJson = System.Text.Json.JsonSerializer.Serialize(history, System.Text.Json.JsonSerializerOptions.Web);
        await _db.SaveChangesAsync();
        await _services.GetRequiredService<Bakabase.Service.Components.SourceAvailability.SourceAvailabilityService>()
            .CheckAsync(new() {Id = "one", Url = "https://example.org/one.zip", Kind = "http", ProviderId = "http", MethodId = "http.native"}, true, default);
        var callsBefore = _availabilityProbe.Calls;
        var result = await _plans.RunAsync(prepared.Id, prepared.Task.PlanRevision, default);
        Assert.IsNull(result.Task.WorkflowRunId);
        Assert.AreEqual("one", result.Plan!.Parts.Single().SelectedSourceId);
        Assert.AreEqual(1, result.Task.PlanRevision);
        Assert.AreEqual(callsBefore, _availabilityProbe.Calls);
        Assert.IsTrue(result.Issues.Any(i => i.Code == "PLAN_REPLAN_SOURCE_CHANGED"));
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [DataTestMethod]
    [DataRow("SOURCE_ACCESS_CODE_REJECTED", true)]
    [DataRow("SOURCE_ACCESS_CODE_REQUIRED", true)]
    [DataRow("SOURCE_VERIFICATION_REQUIRED", true)]
    [DataRow("SOURCE_AUTH_REQUIRED", false)]
    public async Task OnlyExplicitShareCredentialFailuresBlockNewExecutionWhileGenericAnonymousAuthDoesNot(string code, bool blocked)
    {
        const string url = "https://example.org/one.zip";
        _availabilityProbe.Conditions[url] = (Bakabase.Modules.Downloader.Cloud.SourceAvailabilityStatuses.AccessRequired, code);
        var plan = _builder.BuildPlan(new("one", "One", url), "Prepared", Path.GetTempPath());
        var prepared = await _plans.CreatePreparedPlanAsync(plan, []);
        var result = await _plans.RunAsync(prepared.Id, prepared.Task.PlanRevision, default);
        Assert.AreEqual(blocked, result.Task.WorkflowRunId == null);
        Assert.AreEqual(blocked, result.Issues.Any(i => i.Code == "SOURCE_ACCESS_VERIFICATION_REQUIRED"));
        Assert.IsFalse(result.Issues.Any(i => i.Code == "SOURCE_CONFIRMED_UNAVAILABLE"));
        Assert.AreEqual("one", result.Plan!.Parts.Single().SelectedSourceId);
    }

    private sealed class SourceProbe : Bakabase.Modules.Downloader.Cloud.ISourceAvailabilityProbe
    {
        public HashSet<string> Unavailable { get; } = [];
        public Dictionary<string, (string Status, string Code)> Conditions { get; } = [];
        public int Calls { get; private set; }
        public bool CanHandle(Bakabase.Modules.Downloader.Cloud.CloudSource source) => true;
        public Task<Bakabase.Modules.Downloader.Cloud.SourceAvailabilityResult> CheckAsync(Bakabase.Modules.Downloader.Cloud.CloudSource source, CancellationToken ct)
        {
            Calls++;
            var condition = Conditions.GetValueOrDefault(source.Url, (Unavailable.Contains(source.Url)
                ? Bakabase.Modules.Downloader.Cloud.SourceAvailabilityStatuses.ConfirmedUnavailable : Bakabase.Modules.Downloader.Cloud.SourceAvailabilityStatuses.Available, "TEST_OBSERVATION"));
            return Task.FromResult(new Bakabase.Modules.Downloader.Cloud.SourceAvailabilityResult {Status = condition.Item1,
                ReasonCode = condition.Item2, ProbeId = "test", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)});
        }
    }

    private sealed class FakeBuilder(BakabaseDbContext db) : IAcquisitionPlanBuilder
    {
        public bool Fail { get; set; }
        public bool SawDurablePreparingTask { get; private set; }
        public List<AcquisitionPlanCandidate> Results { get; set; } =
            [new("one", "One", "https://example.org/one.zip"), new("two", "Two", "https://example.org/two.zip")];
        public AcquisitionPlanBuilderDescriptor Descriptor => new("test.search", "test", "test", ["manual"], "title");
        public async Task<IReadOnlyList<AcquisitionPlanCandidate>> SearchAsync(string query, CancellationToken ct)
        {
            SawDurablePreparingTask = await db.AcquisitionTasks.AsNoTracking().AnyAsync(t => t.Phase == "preparing", ct);
            if (Fail) throw new InvalidOperationException("private token");
            return Results;
        }
        public AcquisitionPlan BuildPlan(AcquisitionPlanCandidate candidate, string title, string outputDirectory) => new()
        {
            Title = title, OutputDirectory = outputDirectory, Parts = [new()
            {
                Id = "main", SelectedSourceId = candidate.Id,
                Sources = [new() {Id = candidate.Id, Url = candidate.Url, Kind = "http"}], Processing = new() {Requirement = "notRequired"}
            }]
        };
    }
}
