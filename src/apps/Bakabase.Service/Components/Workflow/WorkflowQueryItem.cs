namespace Bakabase.Service.Components.Workflow;

/// <summary>
/// Intermediate item produced by a query-extraction transform (e.g. the LLM node) and
/// consumed by a search transform. Carries the type tag <see cref="WorkflowItemTypes.SearchQuery"/>.
/// </summary>
public record WorkflowQueryItem
{
    public string Query { get; init; } = "";

    /// <summary>The originating item's title, kept for notifications / debugging.</summary>
    public string? SourceTitle { get; init; }
}
