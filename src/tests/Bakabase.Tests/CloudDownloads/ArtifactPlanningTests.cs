using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ArtifactPlanningTests
{
    [TestMethod]
    [DataRow("googleDrive", "https://drive.google.com/drive/folders/content?utm_source=first",
        "https://drive.google.com/drive/folders/content?utm_source=second&usp=sharing")]
    [DataRow("gofile", "https://gofile.io/d/content?utm_source=first",
        "https://gofile.io/d/content?utm_source=second")]
    [DataRow("baidu", "https://pan.baidu.com/s/1content?utm_source=first",
        "https://pan.baidu.com/share/init?surl=content&utm_source=second")]
    public void Source_identity_ignores_UI_grouping_and_recognized_provider_tracking(
        string provider, string firstUrl, string secondUrl)
    {
        var original = Source(provider, firstUrl);
        var rediscovered = original with
        {
            Id = "another-post-another-revision",
            GroupId = "a-new-UI-group",
            Url = secondUrl,
            ArchivePassword = "different-processing-password",
            ExtractionPlanJson = "{\"requirement\":\"notRequired\",\"steps\":[]}"
        };

        Assert.AreEqual(CloudSourceIdentity.Key(original, "account-one"),
            CloudSourceIdentity.Key(rediscovered, "account-one"),
            "The same remote content must not be reacquired because another post or group references it.");
    }

    [TestMethod]
    public void Source_identity_separates_accounts_versions_access_codes_and_resource_locators()
    {
        var original = Source("baidu", "https://pan.baidu.com/s/1content") with
            {AccessCode = "code-one", Version = "version-one"};
        var key = CloudSourceIdentity.Key(original, "account-one");
        var distinct = new[]
        {
            CloudSourceIdentity.Key(original, "account-two"),
            CloudSourceIdentity.Key(original),
            CloudSourceIdentity.Key(original with {Version = "version-two"}, "account-one"),
            CloudSourceIdentity.Key(original with {AccessCode = "code-two"}, "account-one"),
            CloudSourceIdentity.Key(original with {Url = "https://pan.baidu.com/s/1other"}, "account-one")
        };

        Assert.IsTrue(distinct.All(candidate => candidate != key),
            "Acquisition identity must retain account, content version and access scope.");
        Assert.AreEqual(distinct.Length, distinct.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void Unrecognized_and_direct_sources_keep_signed_query_and_fragment_information()
    {
        foreach (var provider in new[] {"unknown", "http"})
        {
            var source = Source(provider, "https://files.example.invalid/content?signature=first#version-one");
            Assert.AreNotEqual(CloudSourceIdentity.Key(source), CloudSourceIdentity.Key(source with
                {Url = "https://files.example.invalid/content?signature=second#version-one"}));
            Assert.AreNotEqual(CloudSourceIdentity.Key(source), CloudSourceIdentity.Key(source with
                {Url = "https://files.example.invalid/content?signature=first#version-two"}));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unknown_or_undeclared_transform_is_a_barrier_before_artifact_demand(bool registered)
    {
        var demand = new DemandActivity("files.demand");
        var registry = registered
            ? new Registry(new StubActivity("transform.unknown"), demand)
            : new Registry(demand);
        var input = new object();
        var context = Context(registry, input,
            new WorkflowPlannedActivity("transform.unknown", "{}"), new WorkflowPlannedActivity(demand.Kind, "{}"));

        var result = await WorkflowArtifactPlanner.SatisfyDownstreamAsync(context, input, default);

        Assert.AreSame(input, result);
        Assert.AreEqual(0, demand.Calls);
        CollectionAssert.AreEqual(new[] {"transform.unknown"}, registry.Requests.ToArray());
    }

    [TestMethod]
    [DataRow("download.enqueueSupported")]
    [DataRow("baidu.download")]
    public async Task Force_materializer_blocks_lookahead_instead_of_reusing_downstream_outputs(string kind)
    {
        IWorkflowActivity materializer = kind == "baidu.download"
            ? new BaiduDownloadActivity() : new EnqueueCloudSourcesActivity();
        const string forceConfig = "{\"reusePolicy\":\"force\"}";
        Assert.IsFalse(((IWorkflowArtifactTransparentActivity)materializer).PreservesArtifactDemand(forceConfig));
        var demand = new DemandActivity("files.demand");
        var registry = new Registry(materializer, demand);
        var input = new object();
        var context = Context(registry, input,
            new WorkflowPlannedActivity(materializer.Kind, forceConfig), new WorkflowPlannedActivity(demand.Kind, "{}"));

        var result = await WorkflowArtifactPlanner.SatisfyDownstreamAsync(context, input, default);

        Assert.AreSame(input, result);
        Assert.AreEqual(0, demand.Calls,
            "A saved processed artifact cannot bypass an explicit fresh acquisition.");
        CollectionAssert.AreEqual(new[] {materializer.Kind}, registry.Requests.ToArray());
    }

    [TestMethod]
    [DataRow("{\"accountId\":\"another-account\"}")]
    [DataRow("{\"outputDirectory\":\"/another-directory\"}")]
    public async Task Materializer_scope_changes_are_a_barrier_to_downstream_reuse(string config)
    {
        var materializer = new EnqueueCloudSourcesActivity();
        var demand = new DemandActivity("files.demand");
        var registry = new Registry(materializer, demand);
        var input = new object();

        var result = await WorkflowArtifactPlanner.SatisfyDownstreamAsync(Context(registry, input,
            new WorkflowPlannedActivity(materializer.Kind, config), new WorkflowPlannedActivity(demand.Kind, "{}")), input, default);

        Assert.AreSame(input, result);
        Assert.AreEqual(0, demand.Calls);
    }

    [TestMethod]
    public async Task Transparent_await_forwards_the_original_item_config_context_and_cancellation_to_demand()
    {
        var awaiting = new AwaitCloudDownloadsActivity();
        var demand = new DemandActivity("files.demand");
        var registry = new Registry(awaiting, demand);
        var input = new object();
        const string demandConfig = "{\"recursive\":{\"enabled\":true}}";
        var context = Context(registry, input, new WorkflowPlannedActivity(awaiting.Kind, "{}"),
            new WorkflowPlannedActivity(demand.Kind, demandConfig));
        using var cancellation = new CancellationTokenSource();

        var result = await WorkflowArtifactPlanner.SatisfyDownstreamAsync(context, input, cancellation.Token);

        Assert.AreSame(demand.Result, result);
        Assert.AreEqual(1, demand.Calls);
        Assert.AreNotSame(context, demand.Context);
        Assert.AreEqual(context.RunId, demand.Context!.RunId);
        Assert.AreSame(context.Services, demand.Context.Services);
        Assert.AreSame(context.Variables, demand.Context.Variables);
        Assert.AreEqual(demandConfig, demand.Context.ActivityConfigJson);
        Assert.AreEqual(0, demand.Context.FollowingActivities.Count,
            "A demand sees only its remaining consumers; it cannot recursively plan through itself.");
        Assert.AreSame(input, demand.Item);
        Assert.AreEqual(demandConfig, demand.ConfigJson);
        Assert.AreEqual(cancellation.Token, demand.CancellationToken);
        CollectionAssert.AreEqual(new[] {awaiting.Kind, demand.Kind}, registry.Requests.ToArray());
    }

    [TestMethod]
    public async Task Missing_registry_leaves_the_item_unchanged_without_executing_activities()
    {
        var input = new object();
        var result = await WorkflowArtifactPlanner.SatisfyDownstreamAsync(
            Context(null, input, new WorkflowPlannedActivity("files.demand", "{}")), input, default);
        Assert.AreSame(input, result);
    }

    private static CloudSource Source(string provider, string url) => new()
        {Id = "source-in-one-post", GroupId = "original-group", ProviderId = provider, Url = url};

    private static WorkflowExecutionContext Context(Registry? registry, object payload,
        WorkflowPlannedActivity first, params WorkflowPlannedActivity[] rest) => new()
    {
        RunId = 501, WorkflowDefinitionId = 51, TriggerKind = CloudWorkflow.Trigger,
        Payload = payload, ActivityConfigJson = "{}", Services = new Services(registry),
        Logger = NullLogger.Instance, FollowingActivities = new[] {first}.Concat(rest).ToArray()
    };

    private sealed class Services(Registry? registry) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IWorkflowActivityRegistry) ? registry : null;
    }

    private sealed class Registry(params IWorkflowActivity[] activities) : IWorkflowActivityRegistry
    {
        public IReadOnlyList<IWorkflowActivity> All { get; } = activities;
        public List<string> Requests { get; } = [];
        public IWorkflowActivity? Get(string kind)
        {
            Requests.Add(kind);
            return All.SingleOrDefault(activity => activity.Kind == kind);
        }
        public bool TryGet(string kind, out IWorkflowActivity activity)
        {
            activity = Get(kind)!;
            return activity != null;
        }
    }

    private class StubActivity(string kind) : IWorkflowActivity
    {
        public string Kind => kind;
        public string DisplayName => Kind;
        public string Group => "test";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
            CancellationToken ct) => throw new AssertFailedException("Planning must not execute workflow activities.");
    }

    private sealed class DemandActivity(string kind) : StubActivity(kind), IWorkflowArtifactDemandActivity
    {
        public object Result { get; } = new();
        public int Calls { get; private set; }
        public WorkflowExecutionContext? Context { get; private set; }
        public object? Item { get; private set; }
        public string? ConfigJson { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Task<object> TrySatisfyAsync(WorkflowExecutionContext context, object item, string configJson,
            CancellationToken ct)
        {
            Calls++;
            Context = context;
            Item = item;
            ConfigJson = configJson;
            CancellationToken = ct;
            return Task.FromResult(Result);
        }
    }
}
