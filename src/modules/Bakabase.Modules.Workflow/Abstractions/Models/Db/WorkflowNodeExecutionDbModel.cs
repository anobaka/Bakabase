using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Db;

/// <summary>One durable invocation of a configured node; retries never replace earlier attempts.</summary>
public sealed class WorkflowNodeExecutionDbModel
{
    [Key] public long Id { get; set; }
    public int WorkflowRunId { get; set; }
    public string NodeId { get; set; } = "";
    public int StepIndex { get; set; }
    public string Kind { get; set; } = "";
    public int Attempt { get; set; }
    public string Status { get; set; } = "running";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int FailedCount { get; set; }
    public string ResumeKind { get; set; } = "initial";
    public string? WaitReason { get; set; }
    public string OutputsJson { get; set; } = "[]";
    public bool OutputsTruncated { get; set; }
    /// <summary>Safe metadata captured from received items before execution. Null means not recorded.</summary>
    public string? InputsJson { get; set; }
    public bool InputsTruncated { get; set; }
}
