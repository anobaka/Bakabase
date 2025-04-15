namespace Bakabase.Abstractions.Models.Domain;

public record ReservedPropertyValue
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int Scope { get; set; }
    public decimal? Rating { get; set; }
    public string? Introduction { get; set; }
    public List<string>? CoverPaths { get; set; }
    public DateTime? PlayedAt { get; set; }
}