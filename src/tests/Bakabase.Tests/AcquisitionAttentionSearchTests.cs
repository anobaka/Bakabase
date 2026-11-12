using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.SourceAvailability.Probes;
using Bakabase.Service.Components.Workflow;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class AcquisitionAttentionSearchTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionTaskSearchService _search = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider();
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _search = new(_db, _services.GetRequiredService<IAcquisitionService>());
    }
    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run)> Seed(string title, string[] codes,
        WorkflowRunStatus status = WorkflowRunStatus.Waiting, string waitReason = "cloud.cloudAttention")
    {
        var run = new WorkflowRunDbModel {StartedAt = DateTime.Now, Status = status, CurrentStepIndex = 1, WaitReason = waitReason,
            // Deliberately unreadable private payloads: global facets must not deserialize these.
            CurrentItemJson = "private checkpoint not JSON", PayloadJson = "private payload not JSON", PlanSnapshotJson = "private frozen plan not JSON"};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = title, BuilderId = "test", Status = AcquisitionStatus.Waiting,
            WorkflowRunId = run.Id, Phase = "attention", PlanHistoryJson = "unchanged history", PlanJson = "private plan not JSON",
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now};
        _db.AcquisitionTasks.Add(task);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "current", StepIndex = 1, Attempt = 1,
            Status = "waiting", StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, WaitReason = waitReason,
            OutputsJson = JsonSerializer.Serialize(codes.Select((code, i) => new WorkflowOutputBlock {Kind = "warning", Key = "target" + i + ":problem", Code = code}), Json)});
        await _db.SaveChangesAsync(); return (task, run);
    }

    [TestMethod]
    public async Task LocalStorageFacetIncludesHistoricalWriteFailuresWithoutMixingCloudCapacityOrShareExpiry()
    {
        var ids = new List<int>();
        foreach (var code in new[] {"LOCAL_STORAGE_FULL", "LOCAL_STORAGE_WRITE_FAILED", "LOCAL_STORAGE_ACCESS_DENIED",
                     "LOCAL_STORAGE_UNAVAILABLE", "BAIDU_OUTPUT_DIRECTORY_FAILED", "StorageUnavailable"})
            ids.Add((await Seed(code, [code])).Task.Id);
        await Seed("Cloud capacity", ["PIKPAK_STORAGE_QUOTA_EXCEEDED"]);
        await Seed("Expired share", ["BAIDU_SHARE_EXPIRED"]);
        var page = await _search.SearchAsync(new(AttentionTag: "localStorage", PageSize: 2));
        Assert.AreEqual(6, page.TotalCount);
        Assert.AreEqual(2, page.Items.Count);
        Assert.AreEqual(6, page.AttentionCounts["localStorage"]);
        Assert.AreEqual(1, page.AttentionCounts["cloudStorageFull"]);
        Assert.AreEqual(1, page.AttentionCounts["sourceUnavailable"]);
        CollectionAssert.AreEquivalent(ids, page.FilteredTaskIds.ToList());
        Assert.IsTrue(page.Items.All(t => t.AttentionTags.SequenceEqual(new[] {"localStorage"})));
        Assert.IsTrue((await _db.AcquisitionTasks.ToListAsync()).All(t => t.PlanHistoryJson == "unchanged history"));
    }

    [TestMethod]
    public async Task CurrentProcessingStoragePromptIsSearchableWithoutRevivingEarlierSourceFailure()
    {
        var (task, run) = await Seed("Processing reserve", [], waitReason: "cloud.fileProcessing");
        run.WaitPromptJson = "{\"reason\":\"StorageUnavailable\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW: SECRET_VALUE\"}";
        run.ErrorMessage = "BAIDU_SHARE_EXPIRED: earlier failure";
        (await _db.WorkflowNodeExecutions.SingleAsync()).Kind = AcquisitionPlanProcessingActivity.ActivityKind;
        await _db.SaveChangesAsync();
        var before = run.WaitPromptJson;
        var page = await _search.SearchAsync(new(AttentionTag: "localStorage"));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"}, page.Items.Single().AttentionTags.ToArray());
        Assert.AreEqual(0, page.AttentionCounts["sourceUnavailable"]);
        Assert.AreEqual(before, page.Items.Single().WaitPromptJson, "The existing task prompt remains available to its resume form.");
        Assert.IsFalse(JsonSerializer.Serialize(new {page.AttentionCounts, page.Items.Single().AttentionTags}).Contains("SECRET_VALUE"));
        await _db.Entry(run).ReloadAsync(); Assert.AreEqual(before, run.WaitPromptJson);
    }

    [DataTestMethod]
    [DataRow("acquisition.plan.process", "LOCAL_STORAGE_FULL", false)]
    [DataRow("fs.assessDelivery", "LOCAL_STORAGE_RESERVE_LOW", true)]
    [DataRow("fs.publishArtifacts", "LOCAL_STORAGE_FULL", true)]
    [DataRow("download.awaitCompletion", "LOCAL_STORAGE_RESERVE_LOW", false)]
    public async Task CurrentFailedStorageCauseIsIncludedInSpaceFacts(string kind, string code, bool fromMetadata)
    {
        var (task, run) = await Seed("Current failure", ["PasswordUnknown"], WorkflowRunStatus.Failed);
        run.ErrorMessage = fromMetadata ? "I/O failed at SECRET_PATH" : code;
        var node = await _db.WorkflowNodeExecutions.SingleAsync(); node.Kind = kind; node.Status = "failed";
        node.OutputsJson = JsonSerializer.Serialize(new[] {
            new WorkflowOutputBlock {Kind = "warning", Key = "part:processing", Code = "PasswordUnknown"},
            new WorkflowOutputBlock {Kind = "warning", Key = "activity-failed", Code = fromMetadata ? code : "WORKFLOW_ACTIVITY_FAILED", Severity = "error"}
        }, Json);
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(AttentionTag: "localStorage", Facts: ["spaceInsufficient"]));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {"localStorage"}, page.Items.Single().AttentionTags.ToArray());
        Assert.AreEqual(1, page.FactCounts["spaceInsufficient"]);
        Assert.AreEqual(0, page.FactCounts["downloadedPending"], "A storage failure alone cannot claim inputs were downloaded.");
        run.PendingSignalJson = "{}"; await _db.SaveChangesAsync();
        Assert.AreEqual(0, (await _search.SearchAsync(new(Facts: ["spaceInsufficient"]))).TotalCount,
            "An accepted continuation is not a stopped capacity failure.");
    }

    [DataTestMethod]
    [DataRow("A different current failure.")]
    [DataRow("A remote response mentioned LOCAL_STORAGE_FULL without an OS cause.")]
    [DataRow("PLAN_CURRENT_DIRECTORY_CHANGED")]
    public async Task CurrentFailureDoesNotKeepInheritedStorageWarningsInFacets(string error)
    {
        var (_, run) = await Seed("New error", ["LOCAL_STORAGE_FULL"], WorkflowRunStatus.Failed, "cloud.fileProcessing");
        run.ErrorMessage = error;
        var node = await _db.WorkflowNodeExecutions.SingleAsync(); node.Kind = "acquisition.plan.process"; node.Status = "failed";
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new());
        Assert.AreEqual(0, page.AttentionCounts["localStorage"]);
        Assert.AreEqual(0, page.FactCounts["spaceInsufficient"]);
    }

    [TestMethod]
    public async Task CurrentUnmatchedInputWaitDoesNotAppearAsAnEarlierSourceOrSpaceProblem()
    {
        var (task, run) = await Seed("Unmatched selector", [], waitReason: "cloud.fileProcessing");
        run.WaitPromptJson = "{\"reason\":\"ProcessingInputNotMatched\",\"message\":\"private selector\"}";
        run.ErrorMessage = "LOCAL_STORAGE_RESERVE_LOW: prior attempt";
        (await _db.WorkflowNodeExecutions.SingleAsync()).Kind = "fs.assessDelivery";
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(AttentionTag: "fileProcessing"));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {"fileProcessing"}, page.Items.Single().AttentionTags.ToArray());
        Assert.AreEqual(0, page.AttentionCounts["localStorage"]);
    }

    [TestMethod]
    public async Task PublicationWaitProvidesTheSameCapacityFacetAsFileProcessing()
    {
        var (task, run) = await Seed("Publication reserve", [], waitReason: "artifacts.publication");
        run.WaitPromptJson = "{\"reason\":\"StorageUnavailable\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}";
        (await _db.WorkflowNodeExecutions.SingleAsync()).Kind = "fs.publishArtifacts";
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(AttentionTag: "localStorage", Facts: ["spaceInsufficient"]));
        Assert.AreEqual(task.Id, page.Items.Single().Id);
        CollectionAssert.AreEqual(new[] {"localStorage", "fileProcessing"}, page.Items.Single().AttentionTags.ToArray());
    }

    [DataTestMethod]
    [DataRow("baidu.transfer", "waiting")]
    [DataRow("acquisition.plan.process", "succeeded")]
    public async Task StaleProcessingPromptDoesNotCreateStorageSearchFacet(string kind, string status)
    {
        var (_, run) = await Seed("Earlier reserve", [], waitReason: "cloud.fileProcessing");
        run.WaitPromptJson = "{\"reason\":\"StorageUnavailable\",\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}";
        var node = await _db.WorkflowNodeExecutions.SingleAsync(); node.Kind = kind; node.Status = status;
        await _db.SaveChangesAsync();
        Assert.AreEqual(0, (await _search.SearchAsync(new(AttentionTag: "localStorage"))).TotalCount);
    }

    [TestMethod]
    public async Task GlobalFacetsAndFilteredBulkIdsAreNotLimitedToTheVisiblePage()
    {
        for (var i = 0; i < 30; i++) await Seed("Target " + i, [i % 2 == 0 ? "BAIDU_SHARE_EXPIRED" : "BAIDU_SHARE_UNAVAILABLE"]);
        await Seed("Cancelled", ["BAIDU_SHARE_EXPIRED"], WorkflowRunStatus.Cancelled);
        var page = await _search.SearchAsync(new(AttentionTag: "sourceUnavailable", Page: 2, PageSize: 4));
        Assert.AreEqual(30, page.TotalTasks); Assert.AreEqual(30, page.StageCounts["all"]); Assert.IsFalse(page.StageCounts.ContainsKey("cancelled"));
        Assert.AreEqual(15, page.TotalCount); Assert.AreEqual(4, page.Items.Count); Assert.AreEqual(2, page.Page);
        Assert.AreEqual(15, page.FilteredTaskIds.Count); Assert.AreEqual(15, page.CheckableTaskIds.Count);
        Assert.AreEqual(15, page.AttentionCounts["sourceUnavailable"]); Assert.AreEqual(15, page.AttentionCounts["sourceNeedsVerification"]);
        Assert.IsTrue(page.Items.All(t => t.AttentionTags.SequenceEqual(new[] {"sourceUnavailable"})));
        Assert.IsTrue(page.FilteredTaskIds.SequenceEqual(page.FilteredTaskIds.OrderByDescending(id => id)));
        var narrowed = await _search.SearchAsync(new(Keyword: "Target 2", Stage: "attention", PageSize: 100));
        Assert.AreEqual(30, narrowed.TotalTasks, "Global status counts do not collapse when searching.");
        Assert.AreEqual(11, narrowed.TotalCount); Assert.AreEqual(6, narrowed.AttentionCounts["sourceUnavailable"]);
        Assert.IsTrue((await _db.AcquisitionTasks.ToListAsync()).All(t => t.PlanHistoryJson == "unchanged history"));
    }

    [TestMethod]
    public async Task HashNumberSearchIncludesLiteralTitlesWithoutRequiringCandidateSelection()
    {
        var (numbered, _) = await Seed("Different title", []);
        var literal = "#" + numbered.Id;
        var (named, _) = await Seed("Collection " + literal + " volume", []);
        await Seed("Unrelated", []);
        var page = await _search.SearchAsync(new(Keyword: literal));
        CollectionAssert.AreEquivalent(new[] {numbered.Id, named.Id}, page.Items.Select(t => t.Id).ToArray());
        var phrase = await _search.SearchAsync(new(Keyword: "Collection " + literal));
        Assert.AreEqual(named.Id, phrase.Items.Single().Id);
        Assert.AreEqual(0, (await _search.SearchAsync(new(Keyword: "#2147483648"))).TotalCount);
    }

    [TestMethod]
    public async Task LatestCurrentAttemptAndLiveRunStatusSuppressResolvedHistoricalTags()
    {
        var (task, run) = await Seed("Repaired", ["BAIDU_SHARE_EXPIRED"]);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "current", StepIndex = 1, Attempt = 2,
            Status = "succeeded", StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow});
        await _db.SaveChangesAsync();
        var repaired = await _search.SearchAsync(new());
        Assert.AreEqual(0, repaired.AttentionCounts["sourceUnavailable"]);
        run.Status = WorkflowRunStatus.Success; await _db.SaveChangesAsync();
        var completed = await _search.SearchAsync(new());
        Assert.AreEqual(1, completed.StageCounts["completed"]); Assert.AreEqual(0, completed.StageCounts["attention"]);
        Assert.AreEqual(0, completed.Items.Single().AttentionTags.Count);
        Assert.AreEqual(AcquisitionStatus.Waiting, task.Status, "Reads must not reconcile the persisted task index.");
    }

    [TestMethod]
    public async Task VisibleShareWithoutOriginalDownloadIsNotExpiredOrClearedByMetadataChecks()
    {
        const string code = "PIKPAK_ORIGINAL_FILE_UNAVAILABLE";
        await Seed("Visible share", [code]);
        var page = await _search.SearchAsync(new());
        Assert.AreEqual(1, page.AttentionCounts["sourceNeedsVerification"]);
        Assert.AreEqual(0, page.AttentionCounts["sourceUnavailable"]);
        Assert.IsFalse(ExecutionProblemClassifier.IsRefreshableSourceCode(code));
    }

    [TestMethod]
    public async Task MultipleReasonsProduceIndependentTagsButUnknownLinksAreNeverExpired()
    {
        await Seed("Mixed", ["SOURCE_SHARE_EXPIRED", "DEPENDENCY_REQUIRED"]);
        await Seed("Network", ["PIXELDRAIN_NETWORK_ERROR"]);
        await Seed("Unknown", ["BAIDU_SHARE_UNAVAILABLE", "GOFILE_SHARE_NOT_FOUND", "PIXELDRAIN_SHARE_UNAVAILABLE"]);
        var page = await _search.SearchAsync(new());
        Assert.AreEqual(1, page.AttentionCounts["sourceUnavailable"]);
        Assert.AreEqual(1, page.AttentionCounts["dependencyMissing"]);
        Assert.AreEqual(1, page.AttentionCounts["networkIssue"]);
        Assert.AreEqual(1, page.AttentionCounts["sourceNeedsVerification"]);
        Assert.AreEqual(2, page.Items.Single(t => t.Title == "Mixed").AttentionTags.Count);
        var scoped = await _search.SearchAsync(new(AttentionTag: "dependencyMissing"));
        Assert.AreEqual(1, scoped.Items.Count); Assert.AreEqual(1, scoped.AttentionCounts["sourceNeedsVerification"], "Selecting one tag must not hide the other available facets.");
    }

    [TestMethod]
    public async Task ReasonFiltersUseAndAndMergeTheLegacyParameterWithoutChangingFacetCounts()
    {
        var (both, _) = await Seed("Both", ["SOURCE_SHARE_EXPIRED", "DEPENDENCY_REQUIRED"]);
        await Seed("Only source", ["SOURCE_SHARE_EXPIRED"]);
        await Seed("Only dependency", ["DEPENDENCY_REQUIRED"]);
        var page = await _search.SearchAsync(new(AttentionTag: "sourceUnavailable",
            AttentionTags: ["dependencyMissing", "sourceUnavailable"], PageSize: 1));
        CollectionAssert.AreEqual(new[] {both.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(2, page.AttentionCounts["sourceUnavailable"]);
        Assert.AreEqual(2, page.AttentionCounts["dependencyMissing"]);
        Assert.AreEqual(0, page.FactCounts["downloadedPending"]);
        Assert.AreEqual(0, page.TaskFacts[both.Id].Count);
    }

    [TestMethod]
    public async Task CapacityFactAloneIncludesCurrentDownloadFailureButCannotClaimFilesReady()
    {
        var (full, _) = await Seed("Full", ["LOCAL_STORAGE_FULL"]);
        await Seed("Disconnected", ["LOCAL_STORAGE_UNAVAILABLE"]);
        var (_, stale) = await Seed("Stale", ["LOCAL_STORAGE_RESERVE_LOW"]);
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = stale.Id, NodeId = "current", StepIndex = 1,
            Attempt = 2, Status = "succeeded", StartedAt = DateTime.UtcNow});
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(Facts: ["spaceInsufficient"]));
        CollectionAssert.AreEqual(new[] {full.Id}, page.FilteredTaskIds.ToArray());
        Assert.AreEqual(1, page.FactCounts["spaceInsufficient"]);
        CollectionAssert.AreEqual(new[] {"spaceInsufficient"}, page.TaskFacts[full.Id].ToArray());
        var joint = await _search.SearchAsync(new(Facts: ["spaceInsufficient", "downloadedPending"]));
        Assert.AreEqual(0, joint.TotalCount);
        Assert.AreEqual(1, joint.FactCounts["spaceInsufficient"], "Counts remain within keyword/stage before selected facts.");
    }

    [TestMethod]
    public async Task JointFactFilterUsesRecordedInputAndReturnsOnlyMatchingBulkIds()
    {
        var (task, run) = await Seed("Ready inputs", ["LOCAL_STORAGE_RESERVE_LOW"], waitReason: "cloud.fileProcessing");
        await Seed("Only capacity", ["LOCAL_STORAGE_FULL"]);
        var operationId = Guid.NewGuid().ToString("N");
        var entries = new[] {new ArtifactManifestEntry("small.srt", 12000, new string('a', 64))};
        var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
        _db.ArtifactOperations.Add(new() {Id = operationId, Kind = "download", State = ArtifactOperationState.Completed, OutputArtifactId = operationId});
        _db.Artifacts.Add(new() {Id = operationId, OperationId = operationId, Kind = "download", RootPath = "/do-not-probe",
            Fingerprint = fingerprint, ManifestJson = JsonSerializer.Serialize(new ArtifactManifest(entries, fingerprint), Json)});
        _db.ArtifactOperationBindings.Add(new() {Id = operationId, OwnerKind = "workflow", OwnerId = run.Id.ToString(), Slot = "p:artifact.download", OperationId = operationId});
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        task.PlanRevision = 1; task.PlanJson = "{\"parts\":[{\"id\":\"p\"}]}";
        run.CurrentStepIndex = 0;
        run.PlanSnapshotJson = JsonSerializer.Serialize(new {triggerKind = CloudWorkflow.Trigger, nodes = new[]
            {new {nodeId = "current", kind = AcquisitionPlanProcessingActivity.ActivityKind, configJson = "{\"targetId\":\"p\"}"}}}, Json);
        run.CurrentItemJson = JsonSerializer.Serialize(new {type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name,
            item = new DownloadWorkset {Targets = [new() {Id = "p", DownloadArtifactId = operationId, DownloadOperationId = operationId, ProcessingWaitReason = "DiskSpaceLimit"}]}}, Json);
        run.WaitPromptJson = "{\"targetId\":\"p\",\"reason\":\"DiskSpaceLimit\"}";
        var node = await _db.WorkflowNodeExecutions.SingleAsync(n => n.WorkflowRunId == run.Id);
        node.StepIndex = 0; node.Kind = AcquisitionPlanProcessingActivity.ActivityKind;
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new(AttentionTags: ["fileProcessing", "localStorage"], Facts: ["downloadedPending", "spaceInsufficient"]));
        CollectionAssert.AreEqual(new[] {task.Id}, page.FilteredTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {task.Id}, page.CheckableTaskIds.ToArray());
        Assert.AreEqual(1, page.FactCounts["downloadedPending"]); Assert.AreEqual(2, page.FactCounts["spaceInsufficient"]);
        CollectionAssert.AreEqual(new[] {"downloadedPending", "spaceInsufficient"}, page.TaskFacts[task.Id].ToArray());
    }

    [TestMethod]
    public async Task PreparationIssuesAndStableWorkflowWaitReasonsUseTheSameTaxonomy()
    {
        var (processing, run) = await Seed("Files", [], waitReason: "cloud.fileProcessing");
        _db.AcquisitionTasks.Add(new() {Title = "Choose", BuilderId = "test", Phase = "attention", Status = AcquisitionStatus.Waiting,
            BuilderStateJson = "{\"issues\":[{\"code\":\"OUTPUT_DIRECTORY_REQUIRED\",\"message\":\"not used for classification\",\"blocking\":true}]}"});
        _db.AcquisitionTasks.Add(new() {Title = "Draft", BuilderId = "test", Phase = "preparing", Status = AcquisitionStatus.Waiting});
        await _db.SaveChangesAsync();
        var page = await _search.SearchAsync(new());
        Assert.AreEqual(1, page.AttentionCounts["fileProcessing"]); Assert.AreEqual(1, page.AttentionCounts["planNeedsInput"]);
        Assert.AreEqual(1, page.StageCounts["preparing"]);
        Assert.AreEqual(0, page.Items.Single(t => t.Title == "Draft").AttentionTags.Count);
    }

    [TestMethod]
    public async Task FreshSourceEvidenceClassifiesUnrecognizedLegacySourceFailureButNotProcessingOrAccountBlocks()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "acquisition-source-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = new AcquisitionPlanSource {Id = "source", Url = "https://gofile.io/d/abcdef", ProviderId = "gofile"};
            var plan = new AcquisitionPlan {Parts = [new() {Id = "part", SelectedSourceId = source.Id, Sources = [source]}]};
            var sourceTaskId = 0;
            foreach (var (title, kind, blocker) in new[]
                     {("Legacy source", "baidu.transfer", "WORKFLOW_ACTIVITY_FAILED"),
                         ("Legacy processing", "fs.publishArtifacts", "WORKFLOW_ACTIVITY_FAILED"),
                         ("Account", "baidu.transfer", "BAIDU_ACCOUNT_INVALID")})
            {
                var (task, run) = await Seed(title, [], WorkflowRunStatus.Failed);
                task.PlanJson = JsonSerializer.Serialize(plan, Json);
                run.ErrorMessage = "An old activity failed without a recognized source code.";
                var node = await _db.WorkflowNodeExecutions.SingleAsync(n => n.WorkflowRunId == run.Id);
                node.Kind = kind; node.Status = "failed";
                node.OutputsJson = JsonSerializer.Serialize(new[] {new WorkflowOutputBlock
                    {Kind = "warning", Key = "activity-failed", Code = blocker}}, Json);
                if (title == "Legacy source") sourceTaskId = task.Id;
            }
            await _db.SaveChangesAsync();
            var probe = new DefinitiveSourceProbe();
            var availability = new SourceAvailabilityService(new SourceAvailabilityStore(cacheRoot), [probe]);
            await availability.CheckAsync(DownloadMethodRegistry.Normalize(new DownloadCandidate
                {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId}), true, default);
            var search = new AcquisitionTaskSearchService(_db, _services.GetRequiredService<IAcquisitionService>(), new(availability));
            var result = await search.SearchAsync(new(AttentionTag: "sourceUnavailable"));
            CollectionAssert.AreEqual(new[] {sourceTaskId}, result.FilteredTaskIds.ToArray());
            Assert.AreEqual(1, result.AttentionCounts["sourceUnavailable"]);
            Assert.AreEqual(1, result.AttentionCounts["accessRequired"]);
            Assert.AreEqual(1, result.AttentionCounts["other"]);
            Assert.AreEqual(1, probe.Calls, "The list must only read the existing check.");
            Assert.IsTrue((await _db.AcquisitionTasks.ToListAsync()).All(t => t.PlanHistoryJson == "unchanged history"));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true); }
    }

    private sealed class DefinitiveSourceProbe : ISourceAvailabilityProbe
    {
        public int Calls;
        public bool CanHandle(CloudSource source) => true;
        public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new SourceAvailabilityResult {Status = SourceAvailabilityStatuses.ConfirmedUnavailable,
                ReasonCode = "SOURCE_SHARE_NOT_FOUND", ProbeId = "test", CheckedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)});
        }
    }

    [TestMethod]
    public async Task ExactIdSearchAndCancelledDeepLinkDoNotExposeCancelledInAll()
    {
        var (task, _) = await Seed("Historical", [], WorkflowRunStatus.Cancelled);
        Assert.AreEqual(0, (await _search.SearchAsync(new(Keyword: "#" + task.Id))).TotalCount);
        var history = await _search.SearchAsync(new(Keyword: "#" + task.Id, Stage: "cancelled"));
        Assert.AreEqual(1, history.TotalCount); Assert.AreEqual(0, history.TotalTasks);
        Assert.AreEqual(0, history.Items.Single().AttentionTags.Count);
    }

    [DataTestMethod]
    [DataRow("BAIDU_SHARE_EXPIRED", "SOURCE_SHARE_EXPIRED", "confirmedUnavailable", "sourceUnavailable")]
    [DataRow("BAIDU_SHARE_ACCESS_CODE_REJECTED", "SOURCE_ACCESS_CODE_REJECTED", "accessRequired", "accessRequired")]
    [DataRow("PIXELDRAIN_NETWORK_ERROR", "SOURCE_NETWORK_ERROR", "temporarilyUnavailable", "networkIssue")]
    [DataRow("BAIDU_SHARE_NETWORK_REQUIRES_PROXY", "SOURCE_NETWORK_REQUIRES_PROXY", "unknown", "networkIssue")]
    public void ProviderAndNormalizedReasonsShareOneClassification(string raw, string normalized, string status, string tag)
    {
        Assert.AreEqual(normalized, ExecutionProblemClassifier.NormalizedReason(raw));
        Assert.AreEqual(status, ExecutionProblemClassifier.AvailabilityStatus(raw));
        Assert.AreEqual(status, ExecutionProblemClassifier.AvailabilityStatus(normalized));
        CollectionAssert.AreEqual(new[] {tag}, ExecutionProblemClassifier.Classify([raw]).ToArray());
        CollectionAssert.AreEqual(new[] {tag}, ExecutionProblemClassifier.Classify([normalized]).ToArray());
        if (raw.StartsWith("BAIDU_"))
        { var probe = BaiduShareAvailabilityProbe.Classify(raw); Assert.AreEqual(normalized, probe.ReasonCode); Assert.AreEqual(status, probe.Status); }
    }

    [TestMethod]
    public async Task RejectInvalidFacetsAndUnboundedPageSize()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _search.SearchAsync(new(Stage: "invalid")));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _search.SearchAsync(new(AttentionTag: "provider-specific-label")));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _search.SearchAsync(new(PageSize: 101)));
    }
}
