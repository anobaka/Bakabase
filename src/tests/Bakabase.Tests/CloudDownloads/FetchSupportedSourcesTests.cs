using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class FetchSupportedSourcesTests
{
    [TestMethod]
    public async Task Missing_directory_waits_before_any_downloader_or_artifact_lookup()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var outcome = await new FetchSupportedSourcesActivity().ProcessItemAsync(Context(services), Item(), default);
        Assert.AreEqual("cloud.chooseDirectory", outcome.Suspension?.Reason);
        var saved = (AcquisitionWorkItem)outcome.Suspension!.Item;
        Assert.IsTrue(saved.Variables.ContainsKey("download.workset"));
        Assert.AreEqual("0", saved.Variables["download.phase"]);
    }

    [TestMethod]
    public async Task Verified_downstream_output_satisfies_acquisition_without_any_download_service()
    {
        var demand = new VerifiedDemand();
        using var services = Services(demand);
        var item = Item() with {Variables = new Dictionary<string, string>
        {
            ["download.outputDirectory"] = Path.GetTempPath(),
            ["download.candidate"] = JsonSerializer.Serialize(new DownloadCandidate
            {
                Id = "selected", Url = "https://files.example.invalid/archive.zip", AccessCode = "share-code",
                ArchivePassword = "archive-secret", ExtractionPlanJson = "{\"requirement\":\"notRequired\",\"steps\":[]}"
            }, WorkflowJson.Options)
        }};
        var outcome = await new FetchSupportedSourcesActivity().ProcessItemAsync(Context(services), item, default);
        Assert.IsNull(outcome.Suspension);
        var result = (AcquisitionWorkItem)outcome.Replacement!;
        Assert.IsTrue(result.AlreadyProcessed);
        Assert.AreEqual(1, demand.Calls);
        Assert.AreEqual("archive-secret", demand.Input!.SelectedLink!.ArchivePassword);
        Assert.AreEqual("share-code", demand.Input.SelectedLink.AccessCode);
        Assert.IsTrue(demand.Input.Variables.ContainsKey("download.intentKey"));
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "resource-42"), demand.Input.TargetDirectory);
        Assert.AreEqual("3", result.Variables["download.phase"]);
        Assert.IsFalse(result.Variables["execution.summary"].Contains("archive-secret", StringComparison.Ordinal));
        Assert.IsFalse(result.Variables["execution.summary"].Contains("share-code", StringComparison.Ordinal));
        var workset = JsonSerializer.Deserialize<DownloadWorkset>(result.Variables["download.workset"], WorkflowJson.Options)!;
        Assert.AreEqual("reused", workset.Targets.Single().ExecutionStatus);
        Assert.AreEqual("processed-artifact", workset.Targets.Single().ProcessedArtifactId);
    }

    [TestMethod]
    public async Task Directory_resume_uses_the_same_saved_source_and_can_reuse_typed_output()
    {
        using var empty = new ServiceCollection().BuildServiceProvider();
        var activity = new FetchSupportedSourcesActivity();
        var waiting = await activity.ProcessItemAsync(Context(empty), Item(), default);
        var snapshot = (AcquisitionWorkItem)waiting.Suspension!.Item;
        var demand = new VerifiedDemand();
        using var services = Services(demand);
        // The signal is consumed only by the cloud adapter; demand is deliberately not consulted
        // before applying resume choices. No downloader service exists in this fixture.
        var resumed = await activity.ResumeAsync(Context(services), snapshot,
            JsonSerializer.Serialize(new CloudSelectionSignal {OutputDirectory = Path.GetTempPath()}, WorkflowJson.Options), default);
        Assert.AreEqual(0, demand.Calls);
        Assert.IsNotNull(resumed.Suspension, "The selected directory is retained while missing download services require attention.");
        var workset = JsonSerializer.Deserialize<DownloadWorkset>(((AcquisitionWorkItem)resumed.Suspension.Item)
            .Variables[FetchSupportedSourcesActivity.WorksetVariable], WorkflowJson.Options)!;
        Assert.AreEqual(Path.GetTempPath(), workset.OutputDirectory);
        Assert.AreEqual(Item().LeadValue, workset.Targets.Single().SelectedSource!.Url);
    }

    [TestMethod]
    public async Task Force_acquisition_never_asks_downstream_to_bypass_download()
    {
        var demand = new VerifiedDemand();
        using var services = Services(demand);
        var item = Item() with {Variables = new Dictionary<string, string> {["download.outputDirectory"] = Path.GetTempPath()}};
        var outcome = await new FetchSupportedSourcesActivity()
            .ProcessItemAsync(Context(services, "{\"reusePolicy\":\"force\"}"), item, default);
        Assert.AreEqual(0, demand.Calls);
        Assert.IsNotNull(outcome.Suspension, "Missing execution services remain actionable instead of bypassing download with a downstream artifact.");
        Assert.IsFalse(((AcquisitionWorkItem)outcome.Suspension.Item).AlreadyProcessed);
    }

    [TestMethod]
    public async Task Prepared_resolved_source_keeps_provenance_and_plans_with_its_final_identity()
    {
        var demand = new VerifiedDemand();
        using var services = Services(demand);
        var final = "https://files.example.invalid/archive.zip";
        var source = DownloadMethodRegistry.Normalize(new() {Id = "selected", Url = final}) with
        {
            OriginalUrl = "https://cm1.hk/s/original",
            ResolutionChain = ["https://cm1.hk/s/original", final]
        };
        var item = Item() with {Variables = new Dictionary<string, string>
        {
            ["download.outputDirectory"] = Path.GetTempPath(),
            ["download.candidate"] = JsonSerializer.Serialize(new DownloadCandidate {Id = "selected", Url = final}, WorkflowJson.Options),
            ["download.resolvedSource"] = JsonSerializer.Serialize(source, WorkflowJson.Options)
        }};
        var outcome = await new FetchSupportedSourcesActivity().ProcessItemAsync(Context(services), item, default);
        var result = (AcquisitionWorkItem)outcome.Replacement!;
        Assert.AreEqual(final, demand.Input!.SelectedLink!.Url);
        var workset = JsonSerializer.Deserialize<DownloadWorkset>(result.Variables["download.workset"], WorkflowJson.Options)!;
        var selected = workset.Targets.Single().SelectedSource!;
        Assert.AreEqual(source.OriginalUrl, selected.OriginalUrl);
        CollectionAssert.AreEqual(source.ResolutionChain, selected.ResolutionChain);
        Assert.AreEqual(CloudSourceIdentity.Key(source), CloudSourceIdentity.Key(selected));
    }

    private static AcquisitionWorkItem Item() => new()
    {
        ResourceId = 42, LeadKind = AcquisitionLeadKind.DirectUrl,
        LeadValue = "https://files.example.invalid/archive.zip", Title = "A resource"
    };

    private static ServiceProvider Services(VerifiedDemand demand) => new ServiceCollection()
        .AddSingleton<IUserStoragePolicy>(new UserStoragePolicy(false))
        .AddSingleton<IBOptions<AcquisitionOptions>>(new TestBOptions<AcquisitionOptions>(new()))
        .AddSingleton<IWorkflowActivityRegistry>(new Registry(demand)).BuildServiceProvider();

    private static WorkflowExecutionContext Context(IServiceProvider services, string config = "{}") => new()
    {
        RunId = 400, WorkflowDefinitionId = 40, TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
        Payload = new AcquisitionRequestedPayload(), ActivityConfigJson = config, Services = services,
        Logger = NullLogger.Instance, FollowingActivities = [new("test.verified", "{}")]
    };

    private sealed class Registry(IWorkflowActivity activity) : IWorkflowActivityRegistry
    {
        public IReadOnlyList<IWorkflowActivity> All => [activity];
        public IWorkflowActivity? Get(string kind) => kind == activity.Kind ? activity : null;
        public bool TryGet(string kind, out IWorkflowActivity result) {result = Get(kind)!; return result != null;}
    }

    private sealed class VerifiedDemand : IWorkflowActivity, IWorkflowArtifactDemandActivity
    {
        public string Kind => "test.verified";
        public string DisplayName => Kind;
        public string Group => "test";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public int Calls {get; private set;}
        public AcquisitionWorkItem? Input {get; private set;}
        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct) =>
            throw new AssertFailedException("Lookahead must not execute the consumer.");
        public Task<object> TrySatisfyAsync(WorkflowExecutionContext ctx, object item, string config, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            Input = (AcquisitionWorkItem)item;
            var variables = Input.Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
            variables["artifact.outputId"] = "processed-artifact";
            variables["artifact.operationId"] = "processing-operation";
            return Task.FromResult<object>(Input with {Variables = variables, AlreadyProcessed = true,
                ExtractedDirectory = Path.GetTempPath(), Files = [Path.Combine(Path.GetTempPath(), "verified.txt")]});
        }
    }
}
