namespace Bakabase.Abstractions.Models.Domain;

public record BuiltinPropertyValue
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int Scope { get; set; }
    public decimal? Rating { get; set; }
    public string? Introduction { get; set; }
}