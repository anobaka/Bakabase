using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;

/// <summary>A platform download belonging to an existing acquisition, never an independent continuation.</summary>
public class DownloadResultOwnerDbModel
{
    [Key] public int DownloadTaskId { get; set; }
    public int AcquisitionTaskId { get; set; }
    public int WorkflowRunId { get; set; }
    public int ResourceId { get; set; }
}
