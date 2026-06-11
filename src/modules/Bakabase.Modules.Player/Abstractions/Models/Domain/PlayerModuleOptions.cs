namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

public class PlayerModuleOptions
{
    /// <summary>
    /// Directory for generated temporary playlist files. Must live under the
    /// AppData directory (see appdata-paths rule); wired by the host. When
    /// null, falls back to a "bakabase/playlists" folder under the system
    /// temp directory.
    /// </summary>
    public string? TempPlaylistDirectory { get; set; }

    /// <summary>
    /// Hard cap on the total number of files a single batch-play launch may
    /// contain, guarding against e.g. AllFiles over image-heavy resources.
    /// </summary>
    public int MaxTotalFiles { get; set; } = 1000;

    /// <summary>
    /// Maximum command line length for the multi-file-arguments launch
    /// method (Windows limit is 32767 chars).
    /// </summary>
    public int MaxCommandLineLength { get; set; } = 30000;

    /// <summary>
    /// Temporary playlists older than this are swept on each launch.
    /// </summary>
    public TimeSpan TempPlaylistRetention { get; set; } = TimeSpan.FromDays(1);
}
