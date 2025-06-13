namespace Bakabase.InsideWorld.Business.Components.Downloader.Models.Input;

public record DownloadTaskPutInputModel
{
    public long? Interval { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public string? Checkpoint { get; set; }
    public bool AutoRetry { get; set; }
}