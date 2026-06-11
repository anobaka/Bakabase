namespace Bakabase.Modules.Player.Components;

/// <summary>
/// File-name helpers that treat both '/' and '\\' as separators. Player
/// executable paths and cached file paths may carry Windows separators even
/// when the code runs elsewhere (tests, server mode), where
/// <see cref="Path.GetFileName(string)"/> would return the whole string.
/// </summary>
internal static class CrossPlatformPath
{
    public static string GetFileName(string path)
    {
        var index = path.LastIndexOfAny(['/', '\\']);
        return index >= 0 ? path[(index + 1)..] : path;
    }

    public static string GetFileNameWithoutExtension(string path)
        => Path.GetFileNameWithoutExtension(GetFileName(path));
}
