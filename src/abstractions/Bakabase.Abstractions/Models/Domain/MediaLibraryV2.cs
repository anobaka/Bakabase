namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryV2
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<string> Paths { get; set; } = [];
    public int? TemplateId { get; set; }
    public int ResourceCount { get; set; }
    public string? Color { get; set; }

    public MediaLibraryTemplate? Template { get; set; }
}