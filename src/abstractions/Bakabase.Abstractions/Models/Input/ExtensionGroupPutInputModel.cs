namespace Bakabase.Abstractions.Models.Input;

public record ExtensionGroupPutInputModel(string Name, HashSet<string> Extensions);