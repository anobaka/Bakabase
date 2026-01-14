using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 搜索条件（复用 ResourceSearch 领域模型）
    /// </summary>
    public ResourceSearch? Search { get; set; }
    public double Threshold { get; set; } = 80;
    public List<ComparisonRule> Rules { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
}
