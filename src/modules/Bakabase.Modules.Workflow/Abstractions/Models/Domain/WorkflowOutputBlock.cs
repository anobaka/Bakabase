namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

/// <summary>
/// Inspection-only, explicitly selected output. Never put credentials, raw exception text,
/// signed URLs or execution/checkpoint payloads in these fields. Large file lists belong to
/// the existing artifact manifest and are retrieved by artifact id.
/// </summary>
public sealed record WorkflowOutputBlock
{
    public required string Kind { get; init; }
    public required string Key { get; init; }
    public string? LabelKey { get; init; }
    public string? Label { get; init; }
    public string? ArtifactId { get; init; }
    public string? OperationId { get; init; }
    public string? Directory { get; init; }
    public int? FileCount { get; init; }
    public long? TotalBytes { get; init; }
    public string? State { get; init; }
    public string? CleanupReceiptId { get; init; }
    public string? CleanupStatus { get; init; }
    public string? ProviderId { get; init; }
    public string? MethodId { get; init; }
    public string? EngineId { get; init; }
    public string? RemotePath { get; init; }
    public string? Url { get; init; }
    public string? TaskKind { get; init; }
    public string? TaskId { get; init; }
    public string? Value { get; init; }
    public string? Code { get; init; }
    public string? Severity { get; init; }
    public string? Message { get; init; }
}

public sealed record WorkflowNodeExecution
{
    public long Id { get; init; }
    public string NodeId { get; init; } = "";
    public int StepIndex { get; init; }
    public string Kind { get; init; } = "";
    public int Attempt { get; init; }
    public string Status { get; init; } = "running";
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime StartedAt { get; init; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? CompletedAt { get; init; }
    public int InputCount { get; init; }
    public int OutputCount { get; init; }
    public int FailedCount { get; init; }
    public string ResumeKind { get; init; } = "initial";
    public string? WaitReason { get; init; }
    public List<WorkflowOutputBlock> Outputs { get; init; } = [];
    public bool OutputsTruncated { get; init; }
    public List<WorkflowOutputBlock> Inputs { get; init; } = [];
    public bool InputsRecorded { get; init; }
    public bool InputsTruncated { get; init; }
}
