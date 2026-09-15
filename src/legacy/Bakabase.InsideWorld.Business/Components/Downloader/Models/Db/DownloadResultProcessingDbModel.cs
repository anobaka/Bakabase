using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;

/// <summary>Durable delivery and content state for one producer result; independent of its source task's lifetime.</summary>
public class DownloadResultProcessingDbModel
{
    [Key] public int DownloadResultId { get; set; }
    public int? WorkflowRunId { get; set; }
    public string? ContentsDirectory { get; set; }
    public string? ContentsFilesJson { get; set; }
    public DateTime? ContentsReadyAt { get; set; }
    public int? ResourceId { get; set; }
    public string? DispatchError { get; set; }
    public bool FilterDidNotMatch { get; set; }
    public DateTime? LastAttemptAt { get; set; }
}
