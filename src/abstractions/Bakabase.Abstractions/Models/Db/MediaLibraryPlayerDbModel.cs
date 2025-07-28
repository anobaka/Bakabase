namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryPlayerDbModel
{
    public HashSet<string>? Extensions { get; set; }
    public string ExecutablePath { get; set; } = null!;
    public string Command { get; set; } = "{0}";
}