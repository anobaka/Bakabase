using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("unknown-config")]
    [DataRow("concrete-input")]
    [DataRow("mismatched-value")]
    [DataRow("wrong-binding")]
    public async Task Frozen_download_allocation_is_limited_to_its_validated_snapshot_and_bound_config_field(string variant)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var original = StorageInvocation(root);
        const string key = "storage.downloadDirectory";
        var configuration = JsonSerializer.SerializeToNode(new CloudWorkflowConfig {OutputDirectory = root}, WorkflowJson.Options)!;
        if (variant == "unknown-config") configuration["unknown"] = true;
        if (variant == "concrete-input") configuration["remoteDirectory"] = root;
        var node = new WorkflowRunPlanNode {NodeId = "select", Kind = "download.selectSources", ConfigJson = configuration.ToJsonString(),
            ParameterBindings = [new() {ParameterKey = key, ConfigPath = "/outputDirectory"}]};
        var plan = original with {ParameterSnapshot = null,
            Parameters = original.Parameters.Append(new WorkflowParameterDefinition {Key = key, Type = "string", Format = "directory", Required = true, DisplayInRun = true}).ToArray(),
            Nodes = original.Nodes.Append(node).ToArray()};
        plan = WorkflowInvocationResolver.Resolve(plan, new() {CallerDefaults = new Dictionary<string, JsonElement> {
            [ArtifactWorkflowParameters.ProcessingWorkspaceParameter] = JsonSerializer.SerializeToElement(root), [key] = JsonSerializer.SerializeToElement(root)}});
        var frozen = JsonSerializer.SerializeToNode(plan, WorkflowJson.Options)!;
        if (variant == "mismatched-value")
        {
            configuration["outputDirectory"] = Path.Combine(root, "other"); frozen["nodes"]![1]!["configJson"] = configuration.ToJsonString();
        }
        if (variant == "wrong-binding") frozen["nodes"]![1]!["parameterBindings"]![0]!["configPath"] = "/remoteDirectory";
        var frozenJson = frozen.ToJsonString();
        await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(r => r.PlanSnapshotJson, frozenJson));
        Assert.AreEqual(variant == "valid", (await Preview(final)).Single().Eligible);
    }

    private WorkflowRunPlan StorageInvocation(string allocationRoot, string layer = "caller", string kind = "fs.processArtifacts")
    {
        var definition = ArtifactWorkflowParameters.Declarations().Single(p =>
            p.Key == ArtifactWorkflowParameters.ProcessingWorkspaceParameter) with
        {
            DefaultValue = JsonSerializer.SerializeToElement(allocationRoot),
            AllowedValues = [JsonSerializer.SerializeToElement(allocationRoot)]
        };
        var plan = new WorkflowRunPlan
        {
            Name = "Unrelated parallel local processing", TriggerKind = "manual", Parameters = [definition],
            Nodes = [new()
            {
                NodeId = "processing", Kind = kind,
                ConfigJson = kind == AcquisitionPlanProcessingActivity.ActivityKind
                    ? JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig("part", new() {Requirement = "notRequired"},
                        StoragePolicy: new() {ProcessingWorkspaceDirectory = allocationRoot}), WorkflowJson.Options)
                    : JsonSerializer.Serialize(new FileArtifactProcessingConfig
                        {StoragePolicy = new() {ProcessingWorkspaceDirectory = allocationRoot}}, WorkflowJson.Options),
                ParameterBindings = ArtifactWorkflowParameters.Bindings().Where(b => b.ParameterKey == definition.Key).ToArray()
            }]
        };
        var values = new Dictionary<string, JsonElement> {{definition.Key, JsonSerializer.SerializeToElement(allocationRoot)}};
        return WorkflowInvocationResolver.Resolve(plan, layer switch
        {
            "caller" => new() {CallerDefaults = values}, "override" => new() {Overrides = values}, _ => null
        });
    }

    [DataTestMethod]
    [DataRow("default", "fs.processArtifacts")]
    [DataRow("caller", "fs.processArtifacts")]
    [DataRow("override", "fs.processArtifacts")]
    [DataRow("caller", AcquisitionPlanProcessingActivity.ActivityKind)]
    public async Task Parallel_frozen_storage_allocation_parameters_do_not_block_successful_completion_cleanup(string layer, string kind)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var frozen = WorkflowRunPlans.Serialize(StorageInvocation(root, layer, kind));
        await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(set => set.SetProperty(r => r.PlanSnapshotJson, frozen));
        await new PublishedArtifactCleanupCompletionObserver(db).StageAsync(new() {Id = 1, Status = WorkflowRunStatus.Success}, new object(), default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.IsTrue((await Preview(final)).Single().Eligible);
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        await cleanup.ReclaimRequestedAsync(final.Id, default); // Settle after actual retirement released ancestor references.
        Assert.IsFalse(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
        var other = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 3);
        Assert.AreEqual(WorkflowRunStatus.Waiting, other.Status);
        Assert.AreEqual(frozen, other.PlanSnapshotJson, "The other run's frozen contract must not be rewritten.");
        var metadata = JsonNode.Parse((await store.GetArtifactAsync(final.Id))!.MetadataJson!)!;
        Assert.AreEqual("completed", metadata["publicationCleanup"]!["status"]!.GetValue<string>());
        Assert.AreEqual(1, metadata["publicationCleanup"]!["removedFiles"]!.GetValue<int>());
    }

    [DataTestMethod]
    [DataRow("unknown-parameter")]
    [DataRow("wrong-format")]
    [DataRow("wrong-type")]
    [DataRow("unknown-source")]
    [DataRow("missing-binding")]
    [DataRow("actual-input-binding")]
    [DataRow("additional-input-binding")]
    [DataRow("unknown-activity")]
    [DataRow("mismatched-config-value")]
    [DataRow("duplicate-value-key")]
    [DataRow("duplicate-config-key")]
    [DataRow("unknown-plan-field")]
    [DataRow("unknown-snapshot-field")]
    [DataRow("missing-plan-version")]
    [DataRow("future-plan-version")]
    [DataRow("nested-input-path")]
    [DataRow("unknown-parameter-also-uses-root")]
    [DataRow("candidate-descendant-allocation")]
    [DataRow("lookalike-in-payload")]
    public async Task Only_verified_allocation_parameter_locations_receive_the_configuration_path_role(string defect)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var plan = StorageInvocation(defect == "candidate-descendant-allocation" ? source.RootDirectory : root);
        var node = JsonNode.Parse(WorkflowRunPlans.Serialize(plan))!;
        var declaration = node["parameters"]![0]!;
        var parameter = node["parameterSnapshot"]!["values"]![0]!;
        var activity = node["nodes"]![0]!;
        var binding = activity["parameterBindings"]![0]!;
        var config = JsonNode.Parse(activity["configJson"]!.GetValue<string>())!;
        switch (defect)
        {
            case "unknown-parameter":
                declaration["key"] = "user.inputDirectory"; parameter["key"] = "user.inputDirectory";
                binding["parameterKey"] = "user.inputDirectory"; break;
            case "wrong-format": declaration["format"] = null; break;
            case "wrong-type": parameter["type"] = "object"; break;
            case "unknown-source": parameter["source"] = "arbitrary"; break;
            case "missing-binding": activity["parameterBindings"] = new JsonArray(); break;
            case "actual-input-binding":
                binding["configPath"] = "/actualInput"; config["actualInput"] = root; break;
            case "additional-input-binding":
                activity["parameterBindings"]!.AsArray().Add(new JsonObject
                    {["parameterKey"] = ArtifactWorkflowParameters.ProcessingWorkspaceParameter, ["configPath"] = "/actualInput"});
                config["actualInput"] = root; break;
            case "unknown-activity": activity["kind"] = "custom.inputConsumer"; break;
            case "mismatched-config-value": config["storagePolicy"]!["processingWorkspaceDirectory"] = Path.Combine(root, "other"); break;
            case "unknown-plan-field": node["unknownField"] = true; break;
            case "unknown-snapshot-field": node["parameterSnapshot"]!["unknownField"] = true; break;
            case "missing-plan-version": node.AsObject().Remove("version"); break;
            case "future-plan-version": node["version"] = 99; break;
            case "nested-input-path": config["actualInputs"] = new JsonObject {["value"] = root}; break;
            case "unknown-parameter-also-uses-root":
                node["parameters"]!.AsArray().Add(JsonSerializer.SerializeToNode(new WorkflowParameterDefinition
                    {Key = "user.input", Type = "string", Format = "directory"}, WorkflowJson.Options));
                node["parameterSnapshot"]!["values"]!.AsArray().Add(JsonSerializer.SerializeToNode(new WorkflowResolvedParameter
                    {Key = "user.input", Type = "string", Value = JsonSerializer.SerializeToElement(root), Source = "caller"}, WorkflowJson.Options)); break;
        }
        var configJson = config.ToJsonString();
        if (defect == "duplicate-config-key") configJson = configJson.Insert(1, "\"storagePolicy\":{},");
        activity["configJson"] = configJson;
        var json = node.ToJsonString();
        if (defect == "duplicate-value-key")
            json = json.Replace("\"source\":\"caller\"", "\"source\":\"caller\",\"SOURCE\":\"override\"", StringComparison.Ordinal);
        if (defect == "lookalike-in-payload")
            await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(set => set.SetProperty(r => r.PayloadJson,
                JsonSerializer.Serialize(new {savedPlan = json}, Json)));
        else await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(set => set.SetProperty(r => r.PlanSnapshotJson, json));
        var preview = (await Preview(final)).Single();
        Assert.IsFalse(preview.Eligible, defect);
        Assert.AreEqual("ReferencedPath", preview.Reason, defect);
        Assert.IsTrue(File.Exists(source.Files[0])); Assert.IsTrue(File.Exists(final.Files[0]));
    }
}
