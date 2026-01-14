using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonPlanViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ResourceSearch? Search { get; set; }
    public double Threshold { get; set; }
    public List<ComparisonRuleViewModel> Rules { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int? ResultGroupCount { get; set; }
}
