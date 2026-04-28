namespace Bakabase.Modules.DataCard.Abstractions.Models.Db;

public record DataCardPropertyValueDbModel
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public int PropertyId { get; set; }
    public string? Value { get; set; }
    public int Scope { get; set; }
}
