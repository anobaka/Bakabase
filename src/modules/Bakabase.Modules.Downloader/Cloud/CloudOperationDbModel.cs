using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Written before an external side effect; receipts and task bindings survive workflow retries.</summary>
public sealed class CloudOperationDbModel
{
    [Key] public string Id { get; set; } = "";
    public int WorkflowRunId { get; set; }
    public string TargetId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RequestJson { get; set; } = "";
    public string State { get; set; } = "planned";
    public string? ReceiptJson { get; set; }
    public int? DownloadTaskId { get; set; }
    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
