using System;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;

/// <summary>Durable per-work output. Survives source task deletion and workflow retries.</summary>
public class DownloadResultDbModel
{
    public int Id { get; set; }
    public int DownloadTaskId { get; set; }
    public ThirdPartyId ThirdPartyId { get; set; }
    [Required] public string SourceKey { get; set; } = string.Empty;
    [Required] public string Name { get; set; } = string.Empty;
    public DownloadResultKind Kind { get; set; }
    /// <summary>Managed torrent metadata file, or the root containing this work's files.</summary>
    [Required] public string Path { get; set; } = string.Empty;
    /// <summary>Original user-selected output directory; never the managed metadata cache.</summary>
    [Required] public string DownloadDirectory { get; set; } = string.Empty;
    /// <summary>Exact absolute file paths owned by this work; never inferred by scanning a shared folder.</summary>
    [Required] public string FilesJson { get; set; } = "[]";
    [Required] public string Fingerprint { get; set; } = string.Empty;
    [Required] public string DeduplicationKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Optional post-processing definition, frozen on the source task at creation.</summary>
    public int? WorkflowDefinitionId { get; set; }
}
