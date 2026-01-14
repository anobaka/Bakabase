using Bakabase.Modules.Comparison.Models.Domain;

namespace Bakabase.Modules.Comparison.Models.View;

public class ComparisonResultPairViewModel
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int Resource1Id { get; set; }
    public int Resource2Id { get; set; }
    public double TotalScore { get; set; }
    public List<RuleScoreDetailViewModel>? RuleScores { get; set; }
}

public class RuleScoreDetailViewModel
{
    public int RuleId { get; set; }
    public int Order { get; set; }
    public double Score { get; set; }
    public double Weight { get; set; }
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
    public bool IsSkipped { get; set; }
    public bool IsVetoed { get; set; }
}
