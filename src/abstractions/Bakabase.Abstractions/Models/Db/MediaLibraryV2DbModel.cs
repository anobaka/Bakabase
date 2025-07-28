namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryV2DbModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Paths { get; set; } = null!;
    public int? TemplateId { get; set; }
    public int ResourceCount { get; set; }
    public string? Color { get; set; }
    public string? Players { get; set; }
}