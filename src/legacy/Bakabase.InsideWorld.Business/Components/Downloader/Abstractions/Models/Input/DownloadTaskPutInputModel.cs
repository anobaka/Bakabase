namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;

public record DownloadTaskPutInputModel
{
    public long? Interval { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public string? Checkpoint { get; set; }
    public bool AutoRetry { get; set; }
    public string? Options { get; set; }

    /// <summary>
    /// Optional. When non-empty the task's download path is updated; callers that
    /// don't intend to touch it leave it null/empty (preserving the previous
    /// behaviour where the path was immutable on edit). Consumed by both single-task
    /// and batch editing.
    /// </summary>
    public string? DownloadPath { get; set; }
}