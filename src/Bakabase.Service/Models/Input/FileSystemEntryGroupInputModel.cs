namespace Bakabase.Service.Models.Input;

public record FileSystemEntryGroupInputModel
{
    public string[] Paths { get; set; } = [];
    public bool GroupInternal { get; set; }
}