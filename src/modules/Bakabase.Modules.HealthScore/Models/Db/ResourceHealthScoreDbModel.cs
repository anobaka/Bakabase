namespace Bakabase.Modules.HealthScore.Models.Db;

public record ResourceHealthScoreDbModel
{
    public int ResourceId { get; set; }
    public int ProfileId { get; set; }
    public decimal Score { get; set; }
    public string ProfileHash { get; set; } = string.Empty;
    public string? MatchedRulesJson { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.Now;
}
