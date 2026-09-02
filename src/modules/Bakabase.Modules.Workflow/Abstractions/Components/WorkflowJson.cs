using System.Text.Json;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// The serializer settings every workflow payload passes through. Payloads are written once
/// (event bus / manual run) and read back later by the runner, so the two ends must agree —
/// keeping one instance here is what makes that true by construction rather than by three
/// copies happening to match.
/// </summary>
public static class WorkflowJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
