using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>A native site's temporary remote copy, independent of workflow ownership and the local download result.</summary>
public sealed class CloudDownloadOwnedCopyDbModel
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public int DownloadTaskId { get; set; }
    public string OperationKey { get; set; } = "";
    public string SourceIdentity { get; set; } = "";
    public string? CopyJson { get; set; }
    public int? ResultId { get; set; }
    public string? ResultKey { get; set; }
    public string State { get; set; } = "waitingDownload";
    public string? ReasonCode { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
