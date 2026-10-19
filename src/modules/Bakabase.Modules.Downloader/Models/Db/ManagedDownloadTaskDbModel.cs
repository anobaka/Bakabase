using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Downloader.Models.Db;

/// <summary>Durable common identity and ownership, independent of provider scheduling and configuration.</summary>
public sealed class ManagedDownloadTaskDbModel
{
    [Key] public string Id { get; set; } = string.Empty;
    [Required] public string Category { get; set; } = string.Empty;
    [Required] public string Provider { get; set; } = string.Empty;
    [Required] public string ProviderTaskId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public int? SiteTaskId { get; set; }
    public string OriginKind { get; set; } = "manual";
    public string? OriginId { get; set; }
    public int? WorkflowRunId { get; set; }
    public int? WorkflowDefinitionId { get; set; }
    public int? DownloadResultId { get; set; }
    /// <summary>Explicit child stop intent survives parent continuation and application restarts.</summary>
    public bool UserStopped { get; set; }
    public DateTime CreatedAt { get; set; }
}
