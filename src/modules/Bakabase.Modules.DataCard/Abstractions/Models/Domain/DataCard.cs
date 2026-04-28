namespace Bakabase.Modules.DataCard.Abstractions.Models.Domain;

public record DataCard
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<DataCardPropertyValue>? PropertyValues { get; set; }
}
