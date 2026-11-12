using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Components;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowInvocationParameterTests
{
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
    private static WorkflowRunPlan Plan() => new()
    {
        Name = "Parameter contract", TriggerKind = "test",
        Parameters = [new() { Key = "storage.directory", DefaultValue = Json("/default"), DisplayInRun = true },
            new() { Key = "cleanup", Type = "boolean", DefaultValue = Json(false), DisplayInRun = true }],
        Nodes = [new() { NodeId = "process", Kind = "test.process", ConfigJson = "{\"directory\":\"/fixed\",\"policy\":{\"cleanup\":false,\"local\":\"keep\"}}",
            ParameterBindings = [new() { ParameterKey = "storage.directory", ConfigPath = "/directory" },
                new() { ParameterKey = "cleanup", ConfigPath = "/policy/cleanup" }] }]
    };

    [TestMethod]
    public void ExplicitBindingsApplyPrecedenceAndLeaveFixedFieldsAndInputUntouched()
    {
        var plan = Plan();
        var defaults = new Dictionary<string, JsonElement> { ["storage.directory"] = Json("/caller"), ["cleanup"] = Json(true) };
        var resolved = WorkflowInvocationResolver.Resolve(plan, new() { CallerDefaults = defaults,
            Overrides = new Dictionary<string, JsonElement> { ["storage.directory"] = Json("/invocation") } });
        defaults["cleanup"] = Json(false);
        using var config = JsonDocument.Parse(resolved.Nodes.Single().ConfigJson);
        Assert.AreEqual("/invocation", config.RootElement.GetProperty("directory").GetString());
        Assert.IsTrue(config.RootElement.GetProperty("policy").GetProperty("cleanup").GetBoolean());
        Assert.AreEqual("keep", config.RootElement.GetProperty("policy").GetProperty("local").GetString());
        Assert.AreEqual("override", resolved.ParameterSnapshot!.Values.Single(v => v.Key == "storage.directory").Source);
        Assert.AreEqual("caller", resolved.ParameterSnapshot.Values.Single(v => v.Key == "cleanup").Source);
        StringAssert.Contains(plan.Nodes.Single().ConfigJson, "/fixed");
        Assert.IsNull(plan.ParameterSnapshot);
        var stored = WorkflowRunPlans.Serialize(resolved);
        Assert.AreEqual(stored, WorkflowRunPlans.Serialize(WorkflowInvocationResolver.Resolve(WorkflowRunPlans.Read(stored)!)));
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(resolved,
            new() { Overrides = new Dictionary<string, JsonElement> { ["cleanup"] = Json(false) } }));
    }

    [TestMethod]
    public void InvalidTypesUndeclaredOverridesAndMissingRequiredValuesFailClosed()
    {
        foreach (var values in new[] {
                     new Dictionary<string, JsonElement> { ["unknown"] = Json(true) },
                     new Dictionary<string, JsonElement> { ["cleanup"] = Json("true") },
                     new Dictionary<string, JsonElement> { ["cleanup"] = Json<object?>(null) } })
            Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(Plan(), new() { Overrides = values }));
        var required = Plan() with { Parameters = [new() { Key = "storage.directory", Required = true },
            new() { Key = "cleanup", Type = "boolean" }] };
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(required));
        var choice = Plan() with { Parameters = [new() { Key = "storage.directory", DefaultValue = Json("invalid"),
            AllowedValues = [Json("keep"), Json("delete")] }, new() { Key = "cleanup", Type = "boolean" }] };
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(choice));
    }

    [TestMethod]
    public void JsonPointersRejectMissingMembersArraysRootAndOverlaps()
    {
        foreach (var path in new[] { "", "directory", "/missing", "/policy/missing", "/items/0", "/a~3b" })
        {
            var node = Plan().Nodes[0] with { ConfigJson = "{\"directory\":\"fixed\",\"policy\":{},\"items\":[0]}",
                ParameterBindings = [new() { ParameterKey = "storage.directory", ConfigPath = path }] };
            Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(Plan() with { Nodes = [node] }), path);
        }
        var overlapping = Plan().Nodes[0] with { ParameterBindings = [
            new() { ParameterKey = "storage.directory", ConfigPath = "/policy" },
            new() { ParameterKey = "cleanup", ConfigPath = "/policy/cleanup" }] };
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(Plan() with { Nodes = [overlapping] }));
    }

    [TestMethod]
    public void PointerEscapesResolveExactlyAndOnlyBoundMembersArePublic()
    {
        var node = Plan().Nodes[0] with { ConfigJson = "{\"a/b~c\":\"fixed\",\"other\":\"unchanged\"}",
            ParameterBindings = [new() { ParameterKey = "storage.directory", ConfigPath = "/a~1b~0c" }] };
        var resolved = WorkflowInvocationResolver.Resolve(Plan() with { Nodes = [node] });
        using var doc = JsonDocument.Parse(resolved.Nodes[0].ConfigJson);
        Assert.AreEqual("/default", doc.RootElement.GetProperty("a/b~c").GetString());
        Assert.AreEqual("unchanged", doc.RootElement.GetProperty("other").GetString());
    }

    [TestMethod]
    public void RunSummariesRequireExplicitDisplayOptInAndNeverExposeSecretsOrObjects()
    {
        var plan = Plan() with { Nodes = [], Parameters = [
            new() { Key = "directory", DefaultValue = Json("/Volumes/NAS/Downloads"), DisplayInRun = true },
            new() { Key = "hidden", DefaultValue = Json("private") },
            new() { Key = "secret", DefaultValue = Json("do-not-show"), DisplayInRun = true },
            new() { Key = "explicit", DefaultValue = Json("do-not-show"), Sensitive = true, DisplayInRun = true },
            new() { Key = "url", DefaultValue = Json("https://example.test/?token=private"), DisplayInRun = true },
            new() { Key = "data", Type = "object", DefaultValue = Json(new { token = "private" }), DisplayInRun = true }] };
        var resolved = WorkflowInvocationResolver.Resolve(plan);
        var summaries = WorkflowInvocationResolver.SafeSources(resolved.ParameterSnapshot);
        Assert.AreEqual("/Volumes/NAS/Downloads", summaries[0].DisplayValue);
        Assert.IsTrue(summaries.Skip(1).All(v => v.HasValue && v.DisplayValue == null));
        Assert.IsFalse(JsonSerializer.Serialize(summaries).Contains("do-not-show", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExistingUnparameterizedPlansKeepTheirFixedConfigurations()
    {
        const string json = "{\"version\":1,\"name\":\"Old run\",\"triggerKind\":\"test\",\"nodes\":[{\"nodeId\":\"old\",\"kind\":\"test\",\"configJson\":\"{\\\"path\\\":\\\"/old\\\"}\"}]}";
        var saved = WorkflowRunPlans.Read(json)!;
        var definition = WorkflowRunPlans.ToDefinition(saved);
        Assert.IsNull(definition.ParameterSnapshot);
        Assert.AreEqual("{\"path\":\"/old\"}", definition.Activities.Single().ConfigJson);
    }
    [TestMethod]
    public void HttpParameterJsonRoundTripsScalarsAndCollectionsWithoutJsonElementInternals()
    {
        const string definitionJson = "{\"key\":\"cleanup\",\"type\":\"boolean\",\"defaultValue\":false,\"allowedValues\":[false,true]}";
        var definition = Newtonsoft.Json.JsonConvert.DeserializeObject<WorkflowParameterDefinition>(definitionJson)!;
        Assert.IsFalse(definition.DefaultValue!.Value.GetBoolean());
        Assert.IsTrue(definition.AllowedValues![1].GetBoolean());
        var wire = Newtonsoft.Json.JsonConvert.SerializeObject(definition, new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        });
        using var parsed = JsonDocument.Parse(wire);
        Assert.IsFalse(parsed.RootElement.GetProperty("defaultValue").GetBoolean());
        Assert.IsFalse(wire.Contains("ValueKind", StringComparison.Ordinal));
        var input = Newtonsoft.Json.JsonConvert.DeserializeObject<Bakabase.Modules.Workflow.Abstractions.Models.Input.WorkflowManualRunInputModel>(
            "{\"parameterValues\":{\"cleanup\":true,\"path\":\"/NAS/Downloads\",\"limit\":42,\"nested\":{\"value\":false}}}")!;
        Assert.IsTrue(input.ParameterValues!["cleanup"].GetBoolean());
        Assert.AreEqual("/NAS/Downloads", input.ParameterValues["path"].GetString());
        Assert.AreEqual(42, input.ParameterValues["limit"].GetInt32());
        Assert.IsFalse(input.ParameterValues["nested"].GetProperty("value").GetBoolean());
    }

    [TestMethod]
    public void BoundedObjectValuesSupportLargeMultiPartDirectoryMaps()
    {
        var directories = Enumerable.Range(1, 100).ToDictionary(i => "part-" + i, i => "/NAS/" + new string('a', 250) + "/" + i);
        var plan = Plan() with { Nodes = [], Parameters = [new() { Key = "directories", Type = "object", DefaultValue = Json(directories) }] };
        Assert.AreEqual(100, WorkflowInvocationResolver.Resolve(plan).ParameterSnapshot!.Values[0].Value.EnumerateObject().Count());
        var oversized = plan with { Parameters = [new() { Key = "directories", DefaultValue = Json(new string('x', 262145)) }] };
        Assert.ThrowsException<ArgumentException>(() => WorkflowInvocationResolver.Resolve(oversized));
    }

    [TestMethod]
    public void NestedPlanningContextsInheritTheFrozenSnapshotAndOutputReporter()
    {
        var snapshot = WorkflowInvocationResolver.Resolve(Plan()).ParameterSnapshot;
        var context = new Bakabase.Modules.Workflow.Abstractions.Components.WorkflowExecutionContext
        {
            RunId = 1, WorkflowDefinitionId = 0, TriggerKind = "test", Payload = new object(), ActivityConfigJson = "{}",
            ResumePromptJson = "{\"serverOnly\":true}",
            ParameterSnapshot = snapshot, Services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider(),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        };
        var nested = context.ForPlannedActivity("{\"nested\":true}", []);
        Assert.AreSame(snapshot, nested.ParameterSnapshot);
        Assert.AreSame(context.ReportOutputMetadata, nested.ReportOutputMetadata);
        Assert.IsNull(nested.ResumePromptJson, "A nested operation cannot inherit the parent's resume authority.");
    }

}
