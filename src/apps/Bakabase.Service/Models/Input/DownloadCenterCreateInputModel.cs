using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;

namespace Bakabase.Service.Models.Input;

/// <summary>Manual creation cannot claim workflow, source, or parent ownership.</summary>
public sealed class DownloadCenterCreateInputModel
{
    [Required] public string Provider { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? MagnetUri { get; set; }
    public string? TorrentUrl { get; set; }
    public string? Url { get; set; }
    public string? FileName { get; set; }
}

public sealed class DownloadCenterBatchInputModel
{
    [Required] public string[] Ids { get; set; } = [];
    [Required] public string Action { get; set; } = string.Empty;
    public DownloadTaskActionOnConflict ActionOnConflict { get; set; } = DownloadTaskActionOnConflict.Ignore;
}
