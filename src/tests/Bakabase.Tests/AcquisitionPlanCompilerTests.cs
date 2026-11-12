using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionPlanCompilerTests
{
    [TestMethod]
    public void Delivery_review_follows_all_explicit_steps_and_is_not_an_artifact_transparent_node()
    {
        var nodes = AcquisitionPlanCompiler.Compile(Plan()).Nodes.ToList();
        var review = nodes.Single(n => n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind);
        Assert.IsTrue(nodes.FindLastIndex(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind) < nodes.IndexOf(review));
        Assert.IsTrue(nodes.IndexOf(review) < nodes.FindIndex(n => n.Kind == "fs.publishArtifacts"));
        Assert.IsFalse((object)new AssessFileArtifactDeliveryActivity() is IWorkflowArtifactTransparentActivity);
        var keep = Plan(); keep = keep with {Parts = [keep.Parts[0] with {Processing = new() {Requirement = "notRequired"}}]};
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(keep).Nodes.Any(n => n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind));
    }

    [TestMethod]
    public void Mixed_continuation_only_sends_remote_parts_to_download_nodes()
    {
        var original = Plan();
        var local = original.Parts[0] with {ContinueFrom = new(7, 123, "artifact", "operation", "fingerprint"),
            Processing = new() {Requirement = "required", Steps = [new() {Id = "remaining", Op = "extractArchive"}]}};
        var remote = new AcquisitionPlanPart {Id = "other", SelectedSourceId = "other-source", Processing = new() {Requirement = "notRequired"},
            Sources = [new() {Id = "other-source", Url = "https://example.org/other.zip", Kind = "http"}]};
        var plan = original with {Parts = [local, remote]};
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        var workset = AcquisitionPlanCompiler.Workset(7, 2, plan, []);
        CollectionAssert.AreEqual(new[] {"other"}, workset.Targets.Select(t => t.Id).ToArray());
        var restore = compiled.Nodes.Single(n => n.Kind == AcquisitionPlanContinueFilesActivity.ActivityKind);
        var restoreConfig = JsonSerializer.Deserialize<AcquisitionPlanContinueFilesConfig>(restore.ConfigJson, WorkflowJson.Options)!;
        Assert.AreEqual("main", restoreConfig.Targets.Single().Target.Id);
        Assert.IsTrue(compiled.Nodes.ToList().IndexOf(restore) > compiled.Nodes.ToList().FindIndex(n => n.Kind == "download.awaitCompletion"));
        var firstProcess = compiled.Nodes.First(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind);
        Assert.AreEqual(local.ContinueFrom, JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(firstProcess.ConfigJson, WorkflowJson.Options)!.ContinueFrom);
        _ = AcquisitionPlanCompiler.Compile(plan, [AcquisitionPlanCompiler.ToSource(remote.Sources[0], remote)]);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("currentDirectory")]
    [DataRow("archiveDirectory")]
    public void CompiledProcessingNodesKeepTheCallerLayoutAndStepOverridesIndependent(string? layout)
    {
        var original = Plan() with {ExtractionLayout = layout};
        var compiled = AcquisitionPlanCompiler.Compile(original);
        var node = compiled.Nodes.First(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind);
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options)!;
        Assert.AreEqual(layout, config.ExtractionLayout);
        if (layout == null) Assert.IsFalse(node.ConfigJson.Contains("extractionLayout"));
        var target = new CloudDownloadTarget {Id = config.TargetId};
        var inherited = AcquisitionPlanProcessingActivity.EffectivePlan(config, target);
        Assert.AreEqual(layout, inherited.Steps.Single(s => s.Op == "extractArchive").ExtractionLayout);
        var explicitPlan = config.Plan with {Steps = config.Plan.Steps.Select(s => s.Op == "extractArchive" ?
            s with {ExtractionLayout = "archiveDirectory"} : s).ToList()};
        Assert.AreEqual("archiveDirectory", AcquisitionPlanProcessingActivity.EffectivePlan(config with {Plan = explicitPlan}, target)
            .Steps.Single(s => s.Op == "extractArchive").ExtractionLayout);
        var settings = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(compiled.Nodes.Single(n =>
            n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind).ConfigJson, WorkflowJson.Options)!;
        Assert.AreEqual(layout, settings.ExtractionLayout);
    }

    [DataTestMethod]
    [DataRow("keep")]
    [DataRow("quarantine")]
    [DataRow("delete")]
    public void SourceCleanupOnlyTargetsNewlyAcquiredPartsAndNeverBorrowedContinuationInputs(string policy)
    {
        var original = Plan() with {StoragePolicy = new() {SourceCleanup = policy}};
        var local = original.Parts[0] with {ContinueFrom = new(7, 123, "artifact", "operation", "fingerprint")};
        var remote = new AcquisitionPlanPart {Id = "remote", SelectedSourceId = "remote-source",
            Processing = new() {Requirement = "notRequired"},
            Sources = [new() {Id = "remote-source", Url = "https://example.org/new.zip", Kind = "http"}]};
        var compiled = AcquisitionPlanCompiler.Compile(original with {Parts = [local, remote]});
        var cleanup = compiled.Nodes.Where(n => n.Kind == "download.cleanupArtifacts").ToArray();
        Assert.AreEqual(1, cleanup.Length, "A continuation does not authorize this run to remove another run's input.");
        var config = JsonSerializer.Deserialize<ArtifactCleanupConfig>(cleanup[0].ConfigJson, WorkflowJson.Options)!;
        Assert.AreEqual("remote", config.TargetId);
        Assert.IsTrue(config.UseTargetStoragePolicy);
        Assert.IsTrue(compiled.Nodes.ToList().IndexOf(cleanup[0]) >
            compiled.Nodes.ToList().FindIndex(n => n.Kind == "fs.publishArtifacts"));
        var onlyLocal = AcquisitionPlanCompiler.Compile(original with {Parts = [local]});
        Assert.IsFalse(onlyLocal.Nodes.Any(n => n.Kind == "download.cleanupArtifacts"));
        Assert.IsFalse(onlyLocal.Nodes.Any(n => n.Kind == "download.enqueueSupported" || n.Kind == "baidu.transfer"));
    }

    [TestMethod]
    public void Saved_unknown_alias_plan_can_render_steps_and_compile_without_rewriting_its_source()
    {
        var original = Plan();
        var source = new AcquisitionPlanSource {Id = "selected", Url = "https://pixeldra.in/u/file-id",
            Kind = "share", ProviderId = "unknown", AccessCode = "saved-code", ArchivePassword = "saved-password"};
        var plan = original with {Parts = [original.Parts[0] with {Sources = [source], Processing = null}]};
        var before = JsonSerializer.Serialize(plan);

        var compiled = AcquisitionPlanCompiler.Compile(plan);
        var workset = AcquisitionPlanCompiler.Workset(42, 1, plan, []);

        Assert.IsTrue(compiled.Nodes.Any(n => n.Kind == "download.enqueueSupported"));
        Assert.IsFalse(compiled.Nodes.Any(n => n.Kind == "baidu.transfer"));
        Assert.AreEqual("pixeldrain", workset.Targets.Single().Sources.Single().ProviderId);
        Assert.AreEqual(source.Url, workset.Targets.Single().Sources.Single().Url);
        Assert.AreEqual(source.AccessCode, workset.Targets.Single().Sources.Single().AccessCode);
        Assert.AreEqual(source.ArchivePassword, workset.Targets.Single().Sources.Single().ArchivePassword);
        Assert.AreEqual(before, JsonSerializer.Serialize(plan));
        Assert.AreEqual("unknown", JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(
            compiled.Nodes.Single(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind).ConfigJson,
            WorkflowJson.Options)!.Plan.Requirement);
    }

    [TestMethod]
    public void CoverMetadataNeverBecomesAnExecutionSourceOrPublishedFile()
    {
        var original = Plan();
        var plan = original with {CoverCandidates = [new() {Url = "https://example.test/cover.jpg", Kind = "cover"}]};
        Assert.AreEqual(JsonSerializer.Serialize(AcquisitionPlanCompiler.Compile(original)),
            JsonSerializer.Serialize(AcquisitionPlanCompiler.Compile(plan)));
        var workset = AcquisitionPlanCompiler.Workset(1, 1, plan, []);
        Assert.AreEqual(1, workset.Targets.Count);
        Assert.IsFalse(JsonSerializer.Serialize(workset).Contains("cover.jpg"));
        var selected = new ExHentaiAcquisitionPlanBuilder(null!).BuildPlan(
            new("gallery", "Gallery", "https://exhentai.org/g/1/abcdef/", "https://example.test/cover.jpg"), "Title", "/downloads");
        Assert.AreEqual("https://example.test/cover.jpg", selected.CoverCandidates.Single().Url);
        Assert.AreEqual(1, selected.Parts.Count);
        Assert.ThrowsException<ArgumentException>(() => AcquisitionPlanCompiler.Validate(plan with
            {CoverCandidates = [new() {Url = "file:///private/file"}]}));
    }

    private static AcquisitionPlan Plan() => new()
    {
        Title = "Example", OutputDirectory = "/downloads", Parts = [new()
        {
            Id = "main", SelectedSourceId = "selected", Sources =
            [new() {Id = "selected", Url = "https://example.org/file.zip", Kind = "http"},
             new() {Id = "mirror", Url = "https://mirror.example.org/file.zip", Kind = "http"}],
            Processing = new() {Requirement = "required", Steps =
            [new() {Id = "rename", Op = "renameExtension", Extension = ".zip", Selector = "file.jpg"},
             new() {Id = "open", Op = "extractArchive", Input = "rename", Password = "secret-archive-password"},
             new() {Id = "move", Op = "moveFile", Input = "open", TargetDirectory = "finished"}]}
        }]
    };

    [TestMethod]
    public void ExplicitInstructionsBecomeSeparateStableNodesWithoutLeakingPasswordsInDescriptions()
    {
        var plan = Plan(); var compiled = AcquisitionPlanCompiler.Compile(plan);
        Assert.IsNull(compiled.TemplateDefinitionId);
        var processors = compiled.Nodes.Where(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind).ToList();
        Assert.AreEqual(3, processors.Count);
        CollectionAssert.AreEqual(new[] {"rename", "open", "move"}, processors.Select(n =>
            JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(n.ConfigJson, WorkflowJson.Options)!.StopAfterStepId).ToArray());
        Assert.IsTrue(processors.All(n => !n.Notes!.Contains("secret-archive-password")));
        CollectionAssert.AreEqual(compiled.Nodes.Select(n => n.NodeId).ToArray(), AcquisitionPlanCompiler.Compile(plan).Nodes.Select(n => n.NodeId).ToArray());
        var workset = AcquisitionPlanCompiler.Workset(9, 2, plan, []);
        Assert.AreEqual(1, workset.Targets.Count, "Mirrors are not required parts.");
        Assert.AreEqual("selected", workset.Targets.Single().SelectedSourceId);
    }

    [TestMethod]
    public void NodeNotesDescribeEffectiveManifestInputs()
    {
        var plan = Plan();
        var part = plan.Parts[0];
        var steps = part.Processing!.Steps.ToList();
        steps[1] = steps[1] with {Inputs = ["download", "rename"], Input = "ignored"};
        plan = plan with {Parts = [part with {Processing = part.Processing with {Steps = steps}}]};
        var open = AcquisitionPlanCompiler.Compile(plan).Nodes.Single(n => n.Notes?.StartsWith("extractArchive") == true);
        StringAssert.Contains(open.Notes!, "download, rename");
        Assert.IsFalse(open.Notes!.Contains("ignored"));
    }

    [TestMethod]
    public void UnknownProcessingRemainsAnExplicitConfirmationNode()
    {
        var plan = Plan(); plan = plan with {Parts = [plan.Parts[0] with {Processing = null}]};
        var chain = AcquisitionPlanCompiler.Compile(plan).Nodes.ToList();
        var processing = chain.Single(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind);
        var assessment = chain.Single(n => n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind);
        Assert.IsTrue(chain.IndexOf(processing) < chain.IndexOf(assessment));
        Assert.IsTrue(chain.IndexOf(assessment) < chain.FindIndex(n => n.Kind == "fs.publishArtifacts"));
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(processing.ConfigJson, WorkflowJson.Options)!;
        Assert.AreEqual("unknown", config.Plan.Requirement);
        Assert.IsNull(config.StopAfterStepId);
        Assert.AreEqual(0, config.Plan.Steps.Count);
    }

    [TestMethod]
    public void MultipleExplicitPartsStayRequiredAndAnUnselectedMirrorIsRejected()
    {
        var plan = Plan();
        plan = plan with {Parts = [plan.Parts[0], new()
        {Id = "patch", SelectedSourceId = "patch-source", Sources = [new() {Id = "patch-source", Url = "https://example.org/patch.zip", Kind = "http"}],
            Processing = new() {Requirement = "notRequired"}}]};
        var workset = AcquisitionPlanCompiler.Workset(9, 2, plan, []);
        Assert.AreEqual(2, workset.Targets.Count); Assert.IsTrue(workset.Targets.All(t => !t.Excluded));
        Assert.ThrowsException<ArgumentException>(() => AcquisitionPlanCompiler.Compile(plan with
        {Parts = [plan.Parts[0] with {SelectedSourceId = null}]}));
    }

    [TestMethod]
    public void CheckedShortLinkProviderAddsTransferBeforeDownloadWithoutChangingOriginalLocator()
    {
        const string original = "https://cm1.hk/s/example";
        const string destination = "https://pan.baidu.com/s/1example";
        var plan = Plan();
        plan = plan with {Parts = [plan.Parts[0] with
        {Sources = [new() {Id = "selected", Url = original, Kind = "auto"}]}]};
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(plan).Nodes.Any(n => n.Kind == "baidu.transfer"));
        var checkedSource = new CloudSource
        {
            Id = "selected", Url = destination, ProviderId = "baidu", Kind = "share",
            OriginalUrl = original, ResolutionChain = [original, destination]
        };
        var compiled = AcquisitionPlanCompiler.Compile(plan, [checkedSource]);
        var kinds = compiled.Nodes.Select(n => n.Kind).ToList();
        Assert.AreEqual(1, kinds.Count(k => k == "baidu.transfer"));
        Assert.IsTrue(kinds.IndexOf("baidu.transfer") < kinds.IndexOf("download.enqueueSupported"));
        Assert.AreEqual(original, plan.Parts[0].Sources.Single().Url);
        Assert.AreEqual(original, checkedSource.OriginalUrl);
        CollectionAssert.AreEqual(new[] {original, destination}, checkedSource.ResolutionChain);
        Assert.ThrowsException<ArgumentException>(() => AcquisitionPlanCompiler.Compile(plan,
            [checkedSource with {Id = "not-selected"}]));
    }
}
