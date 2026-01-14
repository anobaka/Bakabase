using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Comparison.Models.Input;

public record ComparisonPlanPatchInputModel
{
    public string? Name { get; set; }
    public ResourceSearch? Search { get; set; }
    public double? Threshold { get; set; }
    public List<ComparisonRuleInputModel>? Rules { get; set; }
}
