namespace Bakabase.Abstractions.Models.Domain;

public record ExtensionGroup(int Id, string Name, HashSet<string>? Extensions);