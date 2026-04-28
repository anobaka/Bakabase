using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.HealthScore.Models;

public record HealthScoreProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public decimal BaseScore { get; set; } = 100m;
    public ResourceSearchFilterGroup? MembershipFilter { get; set; }
    public List<HealthScoreRule> Rules { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Hash over the score-affecting fields. Excludes Name, Enabled, Priority.</summary>
    public string? ProfileHash { get; set; }
}
