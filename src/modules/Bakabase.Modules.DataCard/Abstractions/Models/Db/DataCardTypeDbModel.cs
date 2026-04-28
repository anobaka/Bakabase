using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.DataCard.Abstractions.Models.Db;

public record DataCardTypeDbModel
{
    [Key] public int Id { get; set; }
    [MaxLength(256)] public string Name { get; set; } = null!;
    public string? PropertyIds { get; set; }
    public string? IdentityPropertyIds { get; set; }
    public string? NameTemplate { get; set; }
    public string? DisplayTemplate { get; set; }
    public string? MatchRules { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
