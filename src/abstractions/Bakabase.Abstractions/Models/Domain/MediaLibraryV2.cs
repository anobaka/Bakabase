namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryV2
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Path { get; set; } = null!;
    public int? TemplateId { get; set; }
    public int ResourceCount { get; set; }

    public MediaLibraryTemplate? Template { get; set; }
}