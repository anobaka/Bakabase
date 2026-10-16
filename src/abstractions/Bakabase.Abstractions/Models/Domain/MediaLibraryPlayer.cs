namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryPlayer
{
    public HashSet<string>? Extensions { get; set; }
    public string ExecutablePath { get; set; } = null!;
    public string Command { get; set; } = "{0}";

    /// <summary>
    /// Whether this player accepts HTTP media URLs. Null uses the known-player catalog;
    /// it never restricts opening local files with an existing custom player.
    /// </summary>
    public bool? SupportsHttp { get; set; }
}
