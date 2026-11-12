using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

/// <summary>
/// A private, immutable execution recipe. Nodes execute in list order; this is not a DAG.
/// Template identity is provenance only. Configurations may contain private execution inputs
/// and must never be returned as generic workflow output metadata.
/// </summary>
public sealed record WorkflowRunPlan
{
    public IReadOnlyList<WorkflowParameterDefinition> Parameters { get; init; } = [];
    public WorkflowInvocationSnapshot? ParameterSnapshot { get; init; }

    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public required string TriggerKind { get; init; }
    public string? TriggerFilterJson { get; init; }
    public int? TemplateDefinitionId { get; init; }
    public IReadOnlyList<WorkflowRunPlanNode> Nodes { get; init; } = [];
}

public sealed record WorkflowRunPlanNode
{
    public IReadOnlyList<WorkflowParameterBinding> ParameterBindings { get; init; } = [];

    public required string NodeId { get; init; }
    public required string Kind { get; init; }
    public string ConfigJson { get; init; } = "{}";
    public string? Notes { get; init; }
    public WorkflowActivityErrorBehavior OnItemError { get; init; } = WorkflowActivityErrorBehavior.Fail;
}

/// <summary>Safe plan identity for history and recovery UI; never includes node configuration.</summary>
public sealed record WorkflowRunPlanNodeView(string NodeId, string Kind, int Order, string? Notes);
