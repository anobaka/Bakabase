using System.Text.Json;

namespace Bakabase.Modules.Workflow.Abstractions.Models;

/// <summary>Explicit public invocation contract. Undeclared node configuration is never overridable.</summary>
public sealed record WorkflowParameterDefinition
{
    public string Key { get; init; } = "";
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string Type { get; init; } = "string";
    public string? Format { get; init; }
    public bool Required { get; init; }
    [Newtonsoft.Json.JsonConverter(typeof(WorkflowJsonElementConverter))]
    public JsonElement? DefaultValue { get; init; }
    [Newtonsoft.Json.JsonProperty(ItemConverterType = typeof(WorkflowJsonElementConverter))]
    public IReadOnlyList<JsonElement>? AllowedValues { get; init; }
    public bool Sensitive { get; init; }
    /// <summary>Explicit opt-in for bounded, non-secret scalar values in run history.</summary>
    public bool DisplayInRun { get; init; }
}

public sealed record WorkflowParameterBinding
{
    public string ParameterKey { get; init; } = "";
    /// <summary>JSON Pointer to an existing object member; arrays and root replacement are not supported.</summary>
    public string ConfigPath { get; init; } = "";
}

public sealed record WorkflowInvocationInput
{
    public IReadOnlyDictionary<string, JsonElement>? CallerDefaults { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Overrides { get; init; }
}

/// <summary>Private immutable run data. Never expose this snapshot as public output metadata.</summary>
public sealed record WorkflowInvocationSnapshot
{
    public IReadOnlyList<WorkflowResolvedParameter> Values { get; init; } = [];
}

public sealed record WorkflowResolvedParameter
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public required JsonElement Value { get; init; }
    public required string Source { get; init; }
    public bool Sensitive { get; init; }
    public bool DisplayInRun { get; init; }
}

public sealed record WorkflowParameterSourceView
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public required string Source { get; init; }
    public bool HasValue { get; init; }
    public bool Sensitive { get; init; }
    public string? DisplayValue { get; init; }
}
