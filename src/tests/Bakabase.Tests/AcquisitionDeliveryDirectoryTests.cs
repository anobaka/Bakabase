using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionDeliveryDirectoryTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "delivery-layout-test");
    // These tests prepare local paths only. Use the same empty registry as the existing
    // plan/output-directory tests so resolving the service never loads real cloud tools.
    private static Task<IServiceProvider> BuildServices() => TestServiceBuilder.BuildServiceProvider(services =>
        services.AddScoped(_ => new Bakabase.Service.Components.DownloadIntents.DownloadMethodRegistry([], [], null!, null!)));
    private static AcquisitionPlan Plan(string title = "A title") => new()
    {
        Title = title, OutputDirectory = Root,
        Parts = [new() {Id = "main", SelectedSourceId = "one", Sources = [new() {Id = "one", Url = "https://example.test/file.zip"}],
            Processing = new() {Requirement = "notRequired"}}]
    };
    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }

    [TestMethod]
    public void NewCaptureUsesProvenWrappersEvenWithPathOverridesAndFrozenPlansKeepTheirLayout()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage());
        var fresh = resolver.Capture();
        Assert.IsTrue(fresh.ContentLayout!.RemoveGeneratedDirectories);
        Assert.IsFalse(fresh.ContentLayout.CollapseSingleRootDirectory);
        var legacy = JsonSerializer.Deserialize<AcquisitionDeliverySettings>("{}", WorkflowJson.Options)!;
        Assert.AreEqual(fresh.ContentLayout, resolver.Capture(legacy).ContentLayout);
        var original = Plan();
        Assert.IsNull(resolver.Resolve(1, original.Title, Root, original.Parts, legacy).Settings.ContentLayout);
        Assert.IsFalse(JsonSerializer.Serialize(legacy, WorkflowJson.Options).Contains("contentLayout"));
        Assert.ThrowsException<ArgumentException>(() => resolver.Capture(new()
            {ContentLayout = new ArtifactDeliveryPolicy {Version = 999}}));
    }

    [TestMethod]
    public void DeliveryPolicyUsesFrozenWorkflowCallerBindingWithoutChangingProcessingOrDownloadNodes()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage());
        var original = Plan() with {StoragePolicy = new()};
        var originalLayout = resolver.Resolve(1, original.Title, Root, original.Parts, new());
        var old = original with {DeliveryLayout = originalLayout};
        var updated = old with {DeliveryLayout = originalLayout with {Settings = originalLayout.Settings with
            {ContentLayout = new() {CollapseSingleRootDirectory = true}}}};
        var first = AcquisitionPlanCompiler.Compile(old); var second = AcquisitionPlanCompiler.Compile(updated);
        CollectionAssert.AreEqual(first.Nodes.Where(n => n.Kind != "fs.publishArtifacts").Select(n => n.ConfigJson).ToArray(),
            second.Nodes.Where(n => n.Kind != "fs.publishArtifacts").Select(n => n.ConfigJson).ToArray());
        var publish = second.Nodes.Single(n => n.Kind == "fs.publishArtifacts");
        Assert.IsTrue(publish.ParameterBindings.Any(b => b.ParameterKey == "delivery.contentLayout" && b.ConfigPath == "/contentLayout"));
        var config = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(publish.ConfigJson, WorkflowJson.Options)!;
        Assert.IsTrue(config.ContentLayout!.CollapseSingleRootDirectory);
        Assert.IsNull(JsonSerializer.Deserialize<FileArtifactPublicationConfig>(
            first.Nodes.Single(n => n.Kind == "fs.publishArtifacts").ConfigJson, WorkflowJson.Options)!.ContentLayout);
        var restored = Bakabase.Modules.Workflow.Components.WorkflowRunPlans.Read(
            Bakabase.Modules.Workflow.Components.WorkflowRunPlans.Serialize(second))!;
        Assert.AreEqual(publish.ConfigJson, restored.Nodes.Single(n => n.Kind == "fs.publishArtifacts").ConfigJson);
    }

    [TestMethod]
    public void NamesAreReadableCrossPlatformAndIdentitiesSurviveSanitization()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage());
        var first = resolver.Resolve(1, "CON: game/<demo>?  ", Root, Plan().Parts, new());
        var second = resolver.Resolve(2, "CON: game/<demo>?  ", Root, Plan().Parts, new());
        Assert.AreEqual(Path.Combine(Root, "Acquired"), first.RootDirectory);
        StringAssert.Contains(Path.GetFileName(first.TaskDirectory), "CON_ game__demo__");
        Assert.IsTrue(first.TaskDirectory.EndsWith("[A1]"));
        Assert.AreNotEqual(first.TaskDirectory, second.TaskDirectory);
        Assert.AreEqual(first.TaskDirectory, first.PartDirectories["main"]);
        Assert.IsFalse(Directory.Exists(first.TaskDirectory), "Resolving settings must not create any files.");
        var longName = resolver.Resolve(3, new string('游', 512), Root, Plan().Parts, new());
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(Path.GetFileName(longName.TaskDirectory)) < 120);
        StringAssert.EndsWith(longName.TaskDirectory, "[A3]");
    }

    [DataTestMethod]
    [DataRow("../{TaskId}")]
    [DataRow("/{TaskId}")]
    [DataRow("folder\\{TaskId}")]
    [DataRow("{Title}")]
    [DataRow("{Title}{TaskId}")]
    [DataRow("{Title} [A{TaskId}1]")]
    [DataRow("{TaskId}")]
    [DataRow("{Title} [A{TaskId}] duplicate [A{TaskId}]")]
    [DataRow("{TaskId}/{Title}")]
    [DataRow("{TaskId}-{Unknown}")]
    [DataRow("{Title} [A{TaskId}].")]
    [DataRow("CON/{TaskId}")]
    public void UnsafeOrUnstableTemplatesAreRejected(string template) => Assert.ThrowsException<ArgumentException>(() =>
        AcquisitionDeliveryDirectoryResolver.ValidateSettings(new() {DirectoryTemplate = template}));

    [TestMethod]
    public void FixedSuffixAllowsCustomPrefixesAndPreventsCrossTemplateIdentityCollisions()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage()); var plan = Plan();
        var first = resolver.Resolve(21, "Game", Root, plan.Parts, new());
        var other = resolver.Resolve(2, "Game", Root, plan.Parts, new() {DirectoryTemplate = "games/{Title}1 [A{TaskId}]"});
        StringAssert.EndsWith(first.TaskDirectory, "[A21]");
        StringAssert.EndsWith(other.TaskDirectory, "[A2]");
        Assert.AreNotEqual(first.TaskDirectory, other.TaskDirectory);
        AcquisitionDeliveryDirectoryResolver.ValidateSettings(new() {DirectoryTemplate = "archive/[A{TaskId}]", PartDirectoryTemplate = "data/[{PartId}]"});
        Assert.ThrowsException<ArgumentException>(() => AcquisitionDeliveryDirectoryResolver.ValidateSettings(new() {PartDirectoryTemplate = "{PartTitle} [{PartId}1]"}));
    }

    [TestMethod]
    public void MultipartPathsKeepStablePartIdsAndRejectCaseInsensitiveCollisions()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage()); var plan = Plan();
        var parts = new[] {plan.Parts[0] with {Title = "Game", Id = "one"}, plan.Parts[0] with {Title = "Game", Id = "two"}};
        var layout = resolver.Resolve(1, "Title", Root, parts, new());
        Assert.AreEqual(Path.Combine(layout.TaskDirectory, "Game [one]"), layout.PartDirectories["one"]);
        Assert.AreEqual(Path.Combine(layout.TaskDirectory, "Game [two]"), layout.PartDirectories["two"]);
        var renamed = resolver.Resolve(1, "Changed", Root + "-changed", parts.Select(p => p with {Title = "Changed"}).ToList(), new(), layout);
        Assert.AreEqual(layout.TaskDirectory, renamed.TaskDirectory);
        CollectionAssert.AreEquivalent(layout.PartDirectories.Values.ToList(), renamed.PartDirectories.Values.ToList());
        Assert.ThrowsException<ArgumentException>(() => resolver.Resolve(1, "Title", Root,
            [parts[0], parts[0] with {Id = "ONE"}], new()));
        var single = resolver.Resolve(1, "Title", Root, [parts[0]], new());
        Assert.ThrowsException<ArgumentException>(() => resolver.Resolve(1, "Title", Root, parts, new(), single));
    }

    [TestMethod]
    public void FrozenPublicationMapChangesOnlyDeliveryAndLegacyFallbackStaysIdentical()
    {
        var original = Plan(); var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage());
        var plan = original with {DeliveryLayout = resolver.Resolve(1, original.Title, Root, original.Parts, new())};
        var oldWorkset = AcquisitionPlanCompiler.Workset(1, 1, original, []);
        var workset = AcquisitionPlanCompiler.Workset(1, 1, plan, []);
        Assert.AreEqual(JsonSerializer.Serialize(oldWorkset), JsonSerializer.Serialize(workset), "Delivery does not change any download intent.");
        var oldConfig = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(AcquisitionPlanCompiler.Compile(original).Nodes.Last().ConfigJson, WorkflowJson.Options)!;
        var config = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(AcquisitionPlanCompiler.Compile(plan).Nodes.Last().ConfigJson, WorkflowJson.Options)!;
        var target = workset.Targets.Single();
        Assert.IsNull(oldConfig.TargetDirectories);
        StringAssert.EndsWith(PublishFileArtifactsActivity.CloudTargetDirectory(workset, target, oldConfig), Path.DirectorySeparatorChar + "result");
        Assert.AreEqual(plan.DeliveryLayout!.TaskDirectory, PublishFileArtifactsActivity.CloudTargetDirectory(workset, target, config));
        Assert.AreEqual(Path.Combine(Root, "manual"), PublishFileArtifactsActivity.CloudTargetDirectory(workset, target, config with {TargetDirectory = Path.Combine(Root, "manual")}));
        Assert.ThrowsException<InvalidOperationException>(() => PublishFileArtifactsActivity.CloudTargetDirectory(workset, target with {Id = "unknown"}, config));
    }

    [TestMethod]
    public void UnconfiguredNewLayoutResolvesLaterButNeverFallsBackToLegacyPublishing()
    {
        var resolver = new AcquisitionDeliveryDirectoryResolver(new Storage()); var plan = Plan();
        var pending = resolver.Resolve(1, plan.Title, "", plan.Parts, new());
        Assert.AreEqual("", pending.TaskDirectory);
        var config = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(AcquisitionPlanCompiler.Compile(plan with
            {OutputDirectory = "", DeliveryLayout = pending}).Nodes.Last().ConfigJson, WorkflowJson.Options)!;
        Assert.ThrowsException<InvalidOperationException>(() => PublishFileArtifactsActivity.CloudTargetDirectory(
            AcquisitionPlanCompiler.Workset(1, 1, plan, []), AcquisitionPlanCompiler.Workset(1, 1, plan, []).Targets[0], config));
        var configured = resolver.Resolve(1, plan.Title, Root, plan.Parts, new(), pending);
        Assert.AreEqual(Path.Combine(Root, "Acquired", "A title [A1]"), configured.TaskDirectory);
    }

    [TestMethod]
    public async Task NewPlansCaptureDefaultsAndExplicitEditsNeverChangeDownloadIdentity()
    {
        var services = await BuildServices();
        await using var scopeLifetime = (IAsyncDisposable) services;
        var plans = services.GetRequiredService<AcquisitionPlanService>();
        var defaults = services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        defaults.DeliveryRootDirectory = Path.Combine(Root, "first");
        var created = await plans.CreatePreparedPlanAsync(Plan(), []);
        var destination = created.Plan!.DeliveryLayout!.TaskDirectory;
        defaults.DeliveryRootDirectory = Path.Combine(Root, "later");
        var read = await plans.GetAsync(created.Id, default);
        Assert.AreEqual(destination, read!.Plan!.DeliveryLayout!.TaskDirectory);
        Assert.IsTrue(read.CanEditDeliverySettings);
        var changed = await plans.UpdateAsync(created.Id, new(created.Task.PlanRevision,
            DeliverySettings: new() {RootDirectory = Path.Combine(Root, "explicit")}), default);
        Assert.AreEqual(created.Plan.OutputDirectory, changed.Plan!.OutputDirectory);
        Assert.AreNotEqual(destination, changed.Plan.DeliveryLayout!.TaskDirectory);
        var downloadMoved = await plans.UpdateAsync(created.Id, new(changed.Task.PlanRevision, Root + "-download"), default);
        Assert.AreEqual(changed.Plan.DeliveryLayout.TaskDirectory, downloadMoved.Plan!.DeliveryLayout!.TaskDirectory);
        Assert.AreEqual(3, downloadMoved.PlanHistory.Count);
    }

    [TestMethod]
    public async Task LegacyNullAndPastExecutionStayFrozenEvenAfterGlobalChanges()
    {
        var services = await BuildServices();
        await using var scopeLifetime = (IAsyncDisposable) services;
        var plans = services.GetRequiredService<AcquisitionPlanService>(); var db = services.GetRequiredService<BakabaseDbContext>();
        var created = await plans.CreatePreparedPlanAsync(Plan(), []);
        var row = (await db.AcquisitionTasks.FindAsync(created.Id))!;
        row.BuilderStateJson = "{}";
        row.PlanJson = JsonSerializer.Serialize(Plan(), WorkflowJson.Options);
        row.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, Plan(), [], DateTime.UtcNow, PreviousRunId: 999)}, WorkflowJson.Options);
        await db.SaveChangesAsync();
        var read = (await plans.GetAsync(created.Id, default))!;
        Assert.IsNull(read.DeliverySettings); Assert.IsFalse(read.CanEditDeliverySettings);
        var unchanged = await plans.UpdateAsync(created.Id, new(created.Task.PlanRevision, Root), default);
        Assert.IsNull(unchanged.Plan!.DeliveryLayout);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => plans.UpdateAsync(created.Id,
            new(unchanged.Task.PlanRevision, DeliverySettings: new()), default));
    }

    [TestMethod]
    public async Task DraftPreparationIgnoresClientMappingAndKeepsFrozenNamesOnReprepare()
    {
        var services = await BuildServices();
        await using var scopeLifetime = (IAsyncDisposable) services;
        var plans = services.GetRequiredService<AcquisitionPlanService>();
        var draft = await plans.CreateDraftAsync(new("Initial", OutputDirectory: Root), default);
        var first = await plans.PrepareDraftAsync(draft.Id, new(draft.Task.PlanRevision, Plan("Initial") with
            {DeliveryLayout = new() {TaskId = 999, RootDirectory = "/forged", TaskDirectory = "/forged/path"}}), default);
        Assert.AreEqual(draft.Id, first.Plan!.DeliveryLayout!.TaskId);
        var second = await plans.PrepareDraftAsync(draft.Id, new(first.Task.PlanRevision, Plan("Renamed")), default);
        Assert.AreEqual(first.Plan.DeliveryLayout.TaskDirectory, second.Plan!.DeliveryLayout!.TaskDirectory);
    }
}
