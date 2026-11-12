using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionWorkflowParameterTests
{
    private static AcquisitionPlan Plan(ArtifactStoragePolicy? policy) => new()
    {
        Title = "Resource", OutputDirectory = Path.Combine(Path.GetTempPath(), "parameter-downloads"), StoragePolicy = policy,
        Parts = [new() {Id = "main", SelectedSourceId = "source", Sources =
            [new() {Id = "source", Kind = "http", Url = "https://example.test/file.zip"}],
            Processing = new() {Requirement = "notRequired"}}]
    };

    [TestMethod]
    public void CallerPolicyIsFrozenAndBoundWithoutLosingFixedPartAndNestedStepValues()
    {
        var original = Plan(new() {SourceCleanup = "quarantine", ProcessingDirectoryTemplate = "caller/{OperationId}/{Attempt}"});
        var part = original.Parts[0] with
        {
            StoragePolicy = new() {CleanupIntermediates = false},
            Processing = new() {Requirement = "required", Steps = [new()
                {Id = "extract", Op = "extractArchive", StoragePolicy = new() {ProcessingDirectoryTemplate = "nested/{OperationId}/{Attempt}"}}]}
        };
        var plan = original with {Parts = [part]};
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        Assert.AreEqual(7, compiled.Parameters.Count);
        Assert.IsTrue(compiled.ParameterSnapshot!.Values.All(v => v.Source == "caller"));
        var source = JsonSerializer.Deserialize<CloudWorkflowConfig>(compiled.Nodes[0].ConfigJson, WorkflowJson.Options)!;
        var target = CloudStoragePolicyBinding.Apply(AcquisitionPlanCompiler.Workset(7, 1, plan, []).Targets,
            source.StoragePolicy, source.TargetStorageOverrides).Single();
        Assert.AreEqual("quarantine", target.StoragePolicy!.SourceCleanup);
        Assert.IsFalse(target.StoragePolicy.CleanupIntermediates);
        var processing = compiled.Nodes.Single(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind);
        Assert.IsFalse(processing.ParameterBindings.Any(b => b.ParameterKey == "storage.cleanupIntermediates"));
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(processing.ConfigJson, WorkflowJson.Options)!;
        Assert.IsFalse(config.StoragePolicy!.CleanupIntermediates);
        Assert.AreEqual("caller/{OperationId}/{Attempt}", config.StoragePolicy.ProcessingDirectoryTemplate);
        Assert.AreEqual("nested/{OperationId}/{Attempt}", config.Plan.Steps.Single().StoragePolicy!.ProcessingDirectoryTemplate);
        var restored = WorkflowRunPlans.Read(WorkflowRunPlans.Serialize(compiled))!;
        Assert.AreEqual(WorkflowRunPlans.Serialize(compiled), WorkflowRunPlans.Serialize(WorkflowInvocationResolver.Resolve(restored)));
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(restored,
            new() {Overrides = ArtifactWorkflowParameters.Values(new() {SourceCleanup = "delete"})}));
    }

    [TestMethod]
    public void AllLocalContinuationReceivesBoundPolicyInsteadOfOldPayloadPolicy()
    {
        var original = Plan(new() {SourceCleanup = "quarantine"});
        var plan = original with {Parts = [original.Parts[0] with
            {ContinueFrom = new(7, 101, "artifact", "operation", "fingerprint")} ]};
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        Assert.IsFalse(compiled.Nodes.Any(n => n.Kind == "download.selectSources"));
        var config = JsonSerializer.Deserialize<AcquisitionPlanContinueFilesConfig>(compiled.Nodes.First().ConfigJson, WorkflowJson.Options)!;
        Assert.AreEqual("quarantine", config.StoragePolicy!.SourceCleanup);
        Assert.AreEqual(6, compiled.Nodes.First().ParameterBindings.Count);
    }

    [TestMethod]
    public void SharedStorageContractSupportsDifferentCallersAndLeavesFixedNodeFieldsUntouched()
    {
        var template = new Bakabase.Modules.Workflow.Abstractions.Models.Domain.WorkflowRunPlan
        {
            Name = "Reusable", TriggerKind = "test", Parameters = ArtifactWorkflowParameters.Declarations(), Nodes = [new()
            {
                NodeId = "files", Kind = "test", ConfigJson = JsonSerializer.Serialize(new
                    {storagePolicy = new ArtifactStoragePolicy(), fixedBehavior = "verify-outputs"}, WorkflowJson.Options),
                ParameterBindings = ArtifactWorkflowParameters.Bindings()
            }]
        };
        var callerA = WorkflowInvocationResolver.Resolve(template, new()
            {CallerDefaults = ArtifactWorkflowParameters.Values(new() {ProcessingDirectoryTemplate = "A/{OperationId}/{Attempt}"})});
        var callerB = WorkflowInvocationResolver.Resolve(template, new()
            {CallerDefaults = ArtifactWorkflowParameters.Values(new() {ProcessingDirectoryTemplate = "B/{OperationId}/{Attempt}", Materialization = "copy"})});
        StringAssert.Contains(callerA.Nodes[0].ConfigJson, "A/{OperationId}/{Attempt}");
        StringAssert.Contains(callerB.Nodes[0].ConfigJson, "B/{OperationId}/{Attempt}");
        StringAssert.Contains(callerB.Nodes[0].ConfigJson, "verify-outputs");
        StringAssert.Contains(callerB.Nodes[0].ConfigJson, "\"materialization\":\"copy\"");
        StringAssert.Contains(callerA.Nodes[0].ConfigJson, "\"materialization\":\"auto\"");
        Assert.IsNull(template.ParameterSnapshot);
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(template, new WorkflowInvocationInput
            {Overrides = new Dictionary<string, JsonElement> {{"fixedBehavior", JsonSerializer.SerializeToElement("skip-verification")}}}));
    }

    [TestMethod]
    public void ExplicitBaiduLayoutIsBoundAndCallerCanOverrideBeforeSnapshot()
    {
        var initial = Plan(null);
        var baidu = initial with {Parts = [initial.Parts[0] with {Sources = [new()
            {Id = "source", Kind = "share", ProviderId = "baidu", Url = "https://pan.baidu.com/s/example"}]}]};
        var legacy = AcquisitionPlanCompiler.Compile(baidu);
        Assert.IsNull(legacy.ParameterSnapshot);
        Assert.AreEqual("{}", legacy.Nodes.Single(n => n.NodeId == "sources.transfer").ConfigJson);
        Assert.AreEqual(0, legacy.Parameters.Count);
        var configured = baidu with {DownloadLayout = "currentDirectory"};
        var compiled = AcquisitionPlanCompiler.Compile(configured, invocation: new()
        {
            Overrides = new Dictionary<string, JsonElement> { ["download.layout"] = JsonSerializer.SerializeToElement("separateDirectory") }
        });
        var transfer = compiled.Nodes.Single(n => n.NodeId == "sources.transfer");
        Assert.AreEqual("separateDirectory", JsonSerializer.Deserialize<CloudWorkflowConfig>(transfer.ConfigJson, WorkflowJson.Options)!.DownloadLayout);
        Assert.AreEqual("download.layout", transfer.ParameterBindings.Single().ParameterKey);
        Assert.AreEqual("override", compiled.ParameterSnapshot!.Values.Single().Source);
        Assert.ThrowsException<ArgumentException>(() => AcquisitionPlanCompiler.Compile(configured, invocation: new()
            {Overrides = new Dictionary<string, JsonElement> { ["download.layout"] = JsonSerializer.SerializeToElement("invalid") }}));
        Assert.ThrowsException<ArgumentException>(() => AcquisitionPlanCompiler.Compile(baidu, invocation: new()
            {Overrides = new Dictionary<string, JsonElement> { ["download.layout"] = JsonSerializer.SerializeToElement("separateDirectory") }}));
        Assert.IsFalse(AcquisitionPlanCompiler.Compile(Plan(null) with {DownloadLayout = "separateDirectory"})
            .Parameters.Any(p => p.Key == "download.layout"));
    }

    [TestMethod]
    public void LegacyPlansStayUnparameterizedAndKeepTheirExistingNodeShape()
    {
        var compiled = AcquisitionPlanCompiler.Compile(Plan(null));
        Assert.AreEqual(0, compiled.Parameters.Count);
        Assert.IsNull(compiled.ParameterSnapshot);
        Assert.IsTrue(compiled.Nodes.All(n => n.ParameterBindings.Count == 0));
        Assert.IsFalse(compiled.Nodes.Any(n => n.Kind == "download.cleanupArtifacts"));
    }
}
