using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Components;

/// <summary>Small shared checks for local files versus remotely streamed media.</summary>
public static class PlayerFileCapabilities
{
    public static string Extension(string path) =>
        Path.GetExtension(CrossPlatformPath.GetFileName(path));

    public static bool IsHttpTarget(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // A program, archive or an unknown file needs a real filesystem path. HTTP does
    // not make it runnable; in particular, never turn an .exe into a media URL.
    public static bool CanStream(string fileNameOrPath)
    {
        var extension = Extension(fileNameOrPath);
        return InternalOptions.AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static bool SupportsFile(KnownPlayerDefinition definition, string fileNameOrPath) =>
        definition.SupportedExtensions == null ||
        definition.SupportedExtensions.Contains(Extension(fileNameOrPath), StringComparer.OrdinalIgnoreCase);

    public static MediaLibraryPlayer? SelectConfigured(ResourceProfilePlayerOptions? options, string fileNameOrPath)
    {
        var extension = Extension(fileNameOrPath);
        return options?.Players?.FirstOrDefault(p =>
                   NormalizeExtensions(p.Extensions)?.Contains(extension) == true) ??
               options?.Players?.FirstOrDefault(p => NormalizeExtensions(p.Extensions) == null);
    }

    public static IReadOnlySet<string>? NormalizeExtensions(IEnumerable<string>? extensions)
    {
        var result = extensions?.Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim()).Select(e => e.StartsWith('.') ? e : $".{e}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return result is {Count: > 0} ? result : null;
    }

    public static bool SupportsHttp(MediaLibraryPlayer player, KnownPlayerDefinition? known = null) =>
        player.SupportsHttp ?? (known ?? KnownPlayerDefinitions.MatchByExecutable(player.ExecutablePath))?.SupportsHttp == true;
}
