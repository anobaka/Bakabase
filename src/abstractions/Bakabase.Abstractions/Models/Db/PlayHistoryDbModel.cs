namespace Bakabase.Abstractions.Models.Db;

public record PlayHistoryDbModel
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public string? Item { get; set; }
    public DateTime PlayedAt { get; set; }
}