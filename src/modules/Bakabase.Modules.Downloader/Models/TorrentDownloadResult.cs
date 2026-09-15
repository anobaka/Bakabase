namespace Bakabase.Modules.Downloader.Models;

/// <summary>Verified local files, rooted at Directory, ready for the caller's next action.</summary>
public sealed record TorrentDownloadResult(string Directory, IReadOnlyList<string> Files);
