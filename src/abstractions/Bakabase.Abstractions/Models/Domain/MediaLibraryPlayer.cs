namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryPlayer
{
    public HashSet<string>? Extensions { get; set; }
    public string ExecutablePath { get; set; } = null!;
    public string Command { get; set; } = "{0}";
}