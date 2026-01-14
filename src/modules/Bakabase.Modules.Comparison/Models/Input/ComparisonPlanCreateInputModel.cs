using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Comparison.Models.Input;

public record ComparisonPlanCreateInputModel
{
    public string Name { get; set; } = string.Empty;
    public ResourceSearch? Search { get; set; }
    public double Threshold { get; set; } = 80;
    public List<ComparisonRuleInputModel> Rules { get; set; } = [];
}
