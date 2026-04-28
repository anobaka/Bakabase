namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>
/// Bridges between AppData-relative storage form and absolute filesystem paths.
/// All operations are pure string transformations — no disk I/O.
///
/// Storage convention: paths under any AppData root are persisted as relative
/// (forward-slash separated, no leading slash). Paths outside AppData (user
/// disk, URLs) are persisted unchanged.
/// </summary>
public interface IAppDataPathRelocator
{
    /// <summary>
    /// Resolve a stored path to a current absolute path.
    /// - Relative input → resolved against current AppData root.
    /// - Absolute input under any candidate AppData root (current / last-observed /
    ///   prev / Velopack-stripped) → rebased onto current AppData root using the
    ///   longest matching prefix.
    /// - Absolute input outside all candidates, or URL, or null/empty → returned as-is.
    /// </summary>
    string? Resolve(string? stored);

    /// <summary>
    /// Convert an absolute path to AppData-relative form when it falls under any
    /// candidate root (longest match wins). Already-relative inputs, URLs, and
    /// absolute paths outside all candidates are returned unchanged.
    /// </summary>
    string? Relativize(string? path);
}
