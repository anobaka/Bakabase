using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Resolves declared values once, before payload preparation or execution. Fixed node fields are
/// untouched. JSON Pointer is deliberately limited to existing object members, never evaluation.
/// </summary>
public static class WorkflowInvocationResolver
{
    private const int MaximumParameters = 64;
    private const int MaximumValueCharacters = 262144;
    private static readonly Regex KeyPattern = new("^[A-Za-z][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex PrivateKey = new("token|password|secret|cookie|authorization|credential|api.?key|bduss|access.?code", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static WorkflowRunPlan Resolve(WorkflowRunPlan plan, WorkflowInvocationInput? input = null) =>
        ResolveCore(plan, input, requireValues: true);

    internal static WorkflowRunPlan Preview(WorkflowRunPlan plan, WorkflowInvocationInput? input = null) =>
        ResolveCore(plan, input, requireValues: false);

    private static WorkflowRunPlan ResolveCore(WorkflowRunPlan plan, WorkflowInvocationInput? input, bool requireValues)
    {
        // Round-trip detaches lists, caller dictionaries and JsonDocument lifetimes.
        plan = WorkflowRunPlans.Read(WorkflowRunPlans.Serialize(plan))!;
        var declarations = ValidateContract(plan);
        if (plan.ParameterSnapshot != null)
        {
            if (input?.CallerDefaults?.Count > 0 || input?.Overrides?.Count > 0)
                throw new ArgumentException("A saved workflow invocation cannot receive new parameter values.");
            return plan;
        }
        var values = new Dictionary<string, WorkflowResolvedParameter>(StringComparer.Ordinal);
        foreach (var parameter in plan.Parameters)
            if (parameter.DefaultValue is { } value)
                Set(parameter.Key, value, "default");
        Apply(input?.CallerDefaults, "caller");
        Apply(input?.Overrides, "override");
        if (requireValues)
            foreach (var parameter in plan.Parameters.Where(p => p.Required))
                if (!values.ContainsKey(parameter.Key))
                    throw new ArgumentException($"Workflow parameter '{parameter.Key}' requires a value.");

        return plan with
        {
            ParameterSnapshot = new WorkflowInvocationSnapshot { Values = values.Values.ToArray() },
            Nodes = plan.Nodes.Select(node =>
            {
                if (node.ParameterBindings.Count == 0) return node;
                var config = JsonNode.Parse(node.ConfigJson)!;
                foreach (var binding in node.ParameterBindings)
                {
                    if (!values.TryGetValue(binding.ParameterKey, out var parameter)) continue;
                    var (parent, member) = Locate(config, binding.ConfigPath);
                    parent[member] = JsonNode.Parse(parameter.Value.GetRawText());
                }
                return node with { ConfigJson = config.ToJsonString(WorkflowJson.Options) };
            }).ToArray()
        };

        void Apply(IReadOnlyDictionary<string, JsonElement>? layer, string source)
        {
            if (layer == null) return;
            if (layer.Count > MaximumParameters) throw new ArgumentException("Too many workflow parameter values.");
            foreach (var (key, value) in layer) Set(key, value, source);
        }
        void Set(string key, JsonElement value, string source)
        {
            if (!declarations.TryGetValue(key, out var declaration))
                throw new ArgumentException("An invocation contains an undeclared workflow parameter.");
            ValidateValue(declaration, value);
            values[key] = new WorkflowResolvedParameter
            {
                Key = key, Type = declaration.Type, Value = value.Clone(), Source = source,
                Sensitive = declaration.Sensitive, DisplayInRun = declaration.DisplayInRun
            };
        }
    }

    internal static IReadOnlyDictionary<string, WorkflowParameterDefinition> ValidateContract(WorkflowRunPlan plan)
    {
        if (plan.Parameters == null || plan.Parameters.Count > MaximumParameters)
            throw new ArgumentException("Provide a bounded workflow parameter contract.");
        var declarations = new Dictionary<string, WorkflowParameterDefinition>(StringComparer.Ordinal);
        foreach (var parameter in plan.Parameters)
        {
            if (parameter == null || parameter.Key == null || parameter.Key.Length > 128 ||
                !KeyPattern.IsMatch(parameter.Key) || !declarations.TryAdd(parameter.Key, parameter) ||
                parameter.Type is not ("string" or "boolean" or "integer" or "number" or "object" or "array") ||
                parameter.Format is not (null or "directory") || (parameter.Format != null && parameter.Type != "string") ||
                parameter.Label?.Length > 256 || parameter.Description?.Length > 2048 || parameter.AllowedValues?.Count > 64)
                throw new ArgumentException("Workflow parameters require unique keys and supported, bounded types.");
            if (parameter.AllowedValues is { } allowed)
                foreach (var value in allowed)
                {
                    if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                        throw new ArgumentException("Allowed parameter values must be scalars.");
                    ValidateValue(parameter with { AllowedValues = null }, value);
                }
            if (parameter.DefaultValue is { } defaultValue) ValidateValue(parameter, defaultValue);
        }
        foreach (var node in plan.Nodes)
        {
            if (node.ParameterBindings == null || node.ParameterBindings.Count > MaximumParameters)
                throw new ArgumentException("Provide bounded node parameter bindings.");
            if (node.ParameterBindings.Count == 0) continue;
            JsonNode config;
            try { config = JsonNode.Parse(node.ConfigJson) ?? throw new ArgumentException("A bound node requires an object configuration."); }
            catch (JsonException) { throw new ArgumentException("A bound node requires valid JSON configuration."); }
            var paths = new List<string>();
            foreach (var binding in node.ParameterBindings)
            {
                if (binding == null || binding.ParameterKey == null || !declarations.ContainsKey(binding.ParameterKey))
                    throw new ArgumentException("Node bindings must reference a declared workflow parameter.");
                Locate(config, binding.ConfigPath);
                if (paths.Any(path => path == binding.ConfigPath || path.StartsWith(binding.ConfigPath + "/", StringComparison.Ordinal) ||
                                      binding.ConfigPath.StartsWith(path + "/", StringComparison.Ordinal)))
                    throw new ArgumentException("Node parameter bindings must not overlap.");
                paths.Add(binding.ConfigPath);
            }
        }
        if (plan.ParameterSnapshot != null)
        {
            if (plan.ParameterSnapshot.Values == null || plan.ParameterSnapshot.Values.Count > MaximumParameters)
                throw new ArgumentException("Invalid workflow parameter snapshot.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in plan.ParameterSnapshot.Values)
            {
                if (value == null || !keys.Add(value.Key) || !declarations.TryGetValue(value.Key, out var declaration) ||
                    value.Type != declaration.Type || value.Source is not ("default" or "caller" or "override") ||
                    value.Sensitive != declaration.Sensitive || value.DisplayInRun != declaration.DisplayInRun)
                    throw new ArgumentException("Invalid workflow parameter snapshot.");
                ValidateValue(declaration, value.Value);
            }
            foreach (var declaration in plan.Parameters.Where(p => p.Required))
                if (!keys.Contains(declaration.Key)) throw new ArgumentException("A saved invocation is missing a required parameter.");
        }
        return declarations;
    }

    private static void ValidateValue(WorkflowParameterDefinition parameter, JsonElement value)
    {
        var correctType = parameter.Type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number),
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => false
        };
        if (!correctType || value.GetRawText().Length > MaximumValueCharacters)
            throw new ArgumentException($"Workflow parameter '{parameter.Key}' requires a bounded {parameter.Type} value.");
        if (parameter.AllowedValues?.Count > 0 && !parameter.AllowedValues.Any(candidate => JsonElement.DeepEquals(candidate, value)))
            throw new ArgumentException($"Workflow parameter '{parameter.Key}' is not an allowed value.");
    }

    private static (JsonObject Parent, string Member) Locate(JsonNode config, string? pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer.Length > 1024 || pointer[0] != '/' || pointer.Any(char.IsControl))
            throw new ArgumentException("A node binding requires an object-member JSON Pointer.");
        var parts = pointer[1..].Split('/');
        if (parts.Length > 16) throw new ArgumentException("A node parameter binding is too deeply nested.");
        for (var i = 0; i < parts.Length; i++)
        {
            // RFC 6901 decoding: malformed escape sequences never alias another path.
            if (Regex.IsMatch(parts[i], "~(?:[^01]|$)")) throw new ArgumentException("Invalid JSON Pointer escape.");
            parts[i] = parts[i].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        }
        var current = config;
        foreach (var part in parts[..^1])
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current) || current == null)
                throw new ArgumentException("A node binding must reference an existing object member.");
        }
        if (current is not JsonObject parent || !parent.ContainsKey(parts[^1]))
            throw new ArgumentException("A node binding must reference an existing object member.");
        return (parent, parts[^1]);
    }

    public static IReadOnlyList<WorkflowParameterSourceView> SafeSources(WorkflowInvocationSnapshot? snapshot) =>
        snapshot?.Values.Take(MaximumParameters).Select(value => new WorkflowParameterSourceView
        {
            Key = value.Key, Type = value.Type, Source = value.Source, HasValue = true,
            Sensitive = value.Sensitive, DisplayValue = SafeValue(value)
        }).ToArray() ?? [];

    private static string? SafeValue(WorkflowResolvedParameter value)
    {
        if (!value.DisplayInRun || value.Sensitive || PrivateKey.IsMatch(value.Key) ||
            value.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return null;
        var text = value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString()! : value.Value.GetRawText();
        if (text.Any(char.IsControl) || text.Contains("://", StringComparison.Ordinal) ||
            text.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) || text.Length > 1024) return null;
        return text;
    }
}
