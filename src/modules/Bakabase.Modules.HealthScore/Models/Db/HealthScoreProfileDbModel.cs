using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.HealthScore.Models.Db;

public record HealthScoreProfileDbModel
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public decimal BaseScore { get; set; } = 100m;

    /// <summary>JSON: <see cref="Bakabase.Abstractions.Models.Domain.ResourceSearchFilterGroup"/> via Search module DbModel.</summary>
    public string? MembershipFilterJson { get; set; }

    /// <summary>JSON: List of <see cref="Bakabase.Modules.HealthScore.Models.HealthScoreRule"/>.</summary>
    public string? RulesJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
