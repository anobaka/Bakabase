namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryV2DbModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Path { get; set; } = null!;
    public int TemplateId { get; set; }
}