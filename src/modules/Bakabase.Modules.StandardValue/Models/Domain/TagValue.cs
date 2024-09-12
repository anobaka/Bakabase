namespace Bakabase.Modules.StandardValue.Models.Domain;

public record TagValue(string? Group, string Name)
{
    public string? Group { get; set; } = Group;
    public string Name { get; set; } = Name;

    public override string ToString()
    {
        return string.IsNullOrEmpty(Group) ? Name : $"{Group}:{Name}";
    }
}