using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Identifies exact JSON values in a validated frozen invocation, never a property
/// name or a parameter-key prefix. All other JSON still passes through the reference guard.</summary>
internal static class ArtifactWorkflowParameterReferences
{
    private static readonly JsonSerializerOptions Strict = new(WorkflowJson.Options)
        {UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow};

    internal static IReadOnlySet<JsonElement> AllocationValues(JsonElement document, CancellationToken ct,
        out IReadOnlyDictionary<JsonElement, IReadOnlyList<string>> nestedFields)
    {
        var result = new HashSet<JsonElement>();
        var nested = new Dictionary<JsonElement, IReadOnlyList<string>>(); nestedFields = nested;
        try
        {
            if (document.ValueKind != JsonValueKind.Object || !Unique(document, ct) ||
                new[] {"version", "name", "triggerKind", "parameters", "parameterSnapshot", "nodes"}
                    .Any(name => Property(document, name) == null)) return result;
            // Unknown members cannot disappear during typed deserialization and accidentally
            // grant a looser path role. Read validates the runner's complete saved contract.
            var json = document.GetRawText();
            if (json.Length > 8 * 1024 * 1024) return result;
            _ = JsonSerializer.Deserialize<WorkflowRunPlan>(json, Strict);
            var plan = WorkflowRunPlans.Read(json);
            if (plan?.ParameterSnapshot == null) return result;
            var configs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var node in plan.Nodes)
            {
                ct.ThrowIfCancellationRequested();
                using var config = JsonDocument.Parse(node.ConfigJson);
                if (config.RootElement.ValueKind != JsonValueKind.Object || !Unique(config.RootElement, ct)) return result;
                configs[node.NodeId] = config.RootElement.Clone();
            }
            var known = ArtifactWorkflowParameters.Declarations().Single(p =>
                p.Key == ArtifactWorkflowParameters.ProcessingWorkspaceParameter);
            var declaration = plan.Parameters.SingleOrDefault(p => p.Key == known.Key);
            var resolved = plan.ParameterSnapshot.Values.SingleOrDefault(p => p.Key == known.Key);
            if (declaration == null || resolved == null || declaration.Type != known.Type || declaration.Format != known.Format ||
                declaration.Sensitive != known.Sensitive || declaration.DisplayInRun != known.DisplayInRun ||
                resolved.Value.ValueKind != JsonValueKind.String ||
                !Path.IsPathFullyQualified(resolved.Value.GetString()!)) return result;
            var bindings = plan.Nodes.SelectMany(node => node.ParameterBindings.Where(b => b.ParameterKey == known.Key)
                .Select(binding => (Node: node, Binding: binding))).ToArray();
            if (bindings.Length == 0) return result;
            foreach (var (node, binding) in bindings)
            {
                ct.ThrowIfCancellationRequested();
                if (!ArtifactWorkflowParameters.IsAllocationRootBinding(node.Kind, binding)) return result;
                var policyElement = Property(configs[node.NodeId], "storagePolicy");
                if (policyElement is not {ValueKind: JsonValueKind.Object} policyJson ||
                    Property(policyJson, "processingWorkspaceDirectory") is not { } boundValue ||
                    !JsonElement.DeepEquals(boundValue, resolved.Value)) return result;
                var policy = JsonSerializer.Deserialize<ArtifactStoragePolicy>(policyJson.GetRawText(), Strict);
                if (policy == null) return result;
                ArtifactStoragePolicies.Validate(policy);
            }
            // Keep elements from the original document: equality identifies that exact JSON
            // location, so an identical value inside a nested input/unknown parameter cannot
            // inherit the allocation role. The original JSON is never rewritten.
            var snapshot = Property(document, "parameterSnapshot")!.Value;
            var values = Property(snapshot, "values")!.Value;
            foreach (var value in values.EnumerateArray())
                if (Property(value, "key")?.GetString() == known.Key)
                    result.Add(Property(value, "value")!.Value);
            foreach (var parameter in Property(document, "parameters")!.Value.EnumerateArray())
                if (Property(parameter, "key")?.GetString() == known.Key)
                {
                    if (Property(parameter, "defaultValue") is {ValueKind: JsonValueKind.String} fallback) result.Add(fallback);
                    if (Property(parameter, "allowedValues") is {ValueKind: JsonValueKind.Array} allowed)
                        foreach (var value in allowed.EnumerateArray()) result.Add(value);
                }
            const string downloadKey = "storage.downloadDirectory";
            var download = plan.Parameters.SingleOrDefault(p => p.Key == downloadKey);
            var downloadValue = plan.ParameterSnapshot.Values.SingleOrDefault(p => p.Key == downloadKey);
            if (download != null || downloadValue != null)
            {
                if (download is not {Type: "string", Format: "directory", Required: true, Sensitive: false, DisplayInRun: true} ||
                    downloadValue?.Value.ValueKind != JsonValueKind.String || !Path.IsPathFullyQualified(downloadValue.Value.GetString()!))
                    return new HashSet<JsonElement>();
                var downloadBindings = plan.Nodes.SelectMany(node => node.ParameterBindings.Where(b => b.ParameterKey == downloadKey)
                    .Select(binding => (Node: node, Binding: binding))).ToArray();
                if (downloadBindings.Length == 0) return new HashSet<JsonElement>();
                foreach (var (node, binding) in downloadBindings)
                {
                    if (node.Kind != "download.selectSources" || binding.ConfigPath != "/outputDirectory" ||
                        Property(configs[node.NodeId], "outputDirectory") is not { } bound || !JsonElement.DeepEquals(bound, downloadValue.Value) ||
                        JsonSerializer.Deserialize<CloudWorkflowConfig>(configs[node.NodeId].GetRawText(), Strict) == null)
                        return new HashSet<JsonElement>();
                }
                foreach (var value in values.EnumerateArray())
                    if (Property(value, "key")?.GetString() == downloadKey) result.Add(Property(value, "value")!.Value);
                foreach (var parameter in Property(document, "parameters")!.Value.EnumerateArray())
                    if (Property(parameter, "key")?.GetString() == downloadKey)
                    {
                        if (Property(parameter, "defaultValue") is {ValueKind: JsonValueKind.String} fallback) result.Add(fallback);
                        if (Property(parameter, "allowedValues") is {ValueKind: JsonValueKind.Array} allowed)
                            foreach (var value in allowed.EnumerateArray()) result.Add(value);
                    }
                foreach (var node in Property(document, "nodes")!.Value.EnumerateArray())
                    if (downloadBindings.Any(b => b.Node.NodeId == Property(node, "nodeId")?.GetString()))
                        nested.Add(Property(node, "configJson")!.Value, ["outputDirectory"]);
            }
            return result;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        { nested.Clear(); return new HashSet<JsonElement>(); }
    }

    private static JsonElement? Property(JsonElement value, string name) => value.ValueKind != JsonValueKind.Object ? null :
        value.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(p => (JsonElement?)p.Value).SingleOrDefault();

    private static bool Unique(JsonElement value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || !Unique(property.Value, ct)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (!Unique(item, ct)) return false;
        return true;
    }
}
