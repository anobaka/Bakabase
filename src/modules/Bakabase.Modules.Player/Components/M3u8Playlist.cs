using System.Text;

namespace Bakabase.Modules.Player.Components;

public record M3u8Entry(string Path, string? Title = null);

/// <summary>
/// Builds temporary .m3u8 playlist files used to hand a whole batch of files
/// to an external player in one launch.
/// </summary>
public static class M3u8Playlist
{
    public const string Extension = ".m3u8";
    private const string FileNamePrefix = "bakabase-batch-";

    /// <summary>
    /// Builds extended-m3u content. Paths keep their separators; callers on
    /// Windows should pass backslash paths (players are stricter about
    /// playlist entries than about command-line arguments).
    /// </summary>
    public static string Build(IEnumerable<M3u8Entry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            var title = SanitizeTitle(entry.Title ?? CrossPlatformPath.GetFileNameWithoutExtension(entry.Path));
            sb.Append("#EXTINF:-1,").Append(title).Append('\n');
            sb.Append(SanitizePath(entry.Path)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes the playlist to a uniquely named file under
    /// <paramref name="directory"/> and returns its full path.
    /// </summary>
    public static async Task<string> WriteTempFileAsync(string directory, IEnumerable<M3u8Entry> entries,
        CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var fileName = $"{FileNamePrefix}{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}{Extension}";
        var fullPath = System.IO.Path.Combine(directory, fileName);
        // UTF-8 without BOM per the m3u8 convention; PotPlayer/VLC/mpv all
        // accept it and non-ASCII titles/paths survive.
        await File.WriteAllTextAsync(fullPath, Build(entries), new UTF8Encoding(false), ct);
        return fullPath;
    }

    /// <summary>
    /// Deletes generated playlists older than <paramref name="retention"/>.
    /// Best-effort: IO failures are swallowed.
    /// </summary>
    public static void SweepOldFiles(string directory, TimeSpan retention)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var threshold = DateTime.Now - retention;
            foreach (var file in Directory.EnumerateFiles(directory, $"{FileNamePrefix}*{Extension}"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // A locked or already-deleted file must not break playback.
                }
            }
        }
        catch
        {
            // Sweeping is opportunistic.
        }
    }

    private static string SanitizeTitle(string title) =>
        title.Replace("\r", " ").Replace("\n", " ");

    private static string SanitizePath(string path) =>
        path.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
