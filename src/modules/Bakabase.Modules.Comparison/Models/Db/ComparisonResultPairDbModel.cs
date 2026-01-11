using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonResultPairDbModel
{
    [Key]
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int Resource1Id { get; set; }
    public int Resource2Id { get; set; }
    public double TotalScore { get; set; }
    /// <summary>
    /// JSON serialized list of RuleScoreDetail
    /// </summary>
    public string? RuleScoresJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
