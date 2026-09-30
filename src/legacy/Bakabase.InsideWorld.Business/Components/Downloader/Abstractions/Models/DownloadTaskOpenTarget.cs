namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;

/// <summary>A task-owned output to open, or a downloaded torrent to reveal in its folder.</summary>
public sealed record DownloadTaskOpenTarget(string Path, bool OpenInDirectory);
