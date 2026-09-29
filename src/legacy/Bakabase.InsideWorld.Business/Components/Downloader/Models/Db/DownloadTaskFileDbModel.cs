using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;

/// <summary>
/// A file that a download task wrote or recognized as its own. The path is scoped by task id:
/// multiple tasks may share an output directory without sharing their downloaded-byte totals.
/// </summary>
public class DownloadTaskFileDbModel
{
    public int DownloadTaskId { get; set; }
    [Required] public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
}
