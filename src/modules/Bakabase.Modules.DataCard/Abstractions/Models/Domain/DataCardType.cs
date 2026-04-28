namespace Bakabase.Modules.DataCard.Abstractions.Models.Domain;

public record DataCardType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<int>? PropertyIds { get; set; }
    public List<int>? IdentityPropertyIds { get; set; }
    public string? NameTemplate { get; set; }
    public DataCardDisplayTemplate? DisplayTemplate { get; set; }
    public DataCardMatchRules? MatchRules { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
