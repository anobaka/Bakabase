namespace Bakabase.Abstractions.Models.Input;

public record ExtensionGroupAddInputModel(string Name, HashSet<string>? Extensions);