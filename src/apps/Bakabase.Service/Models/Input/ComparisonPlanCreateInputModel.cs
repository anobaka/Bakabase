using System.Collections.Generic;
using Bakabase.Modules.Comparison.Models.Input;

namespace Bakabase.Service.Models.Input;

public record ComparisonPlanCreateInputModel
{
    public string Name { get; set; } = string.Empty;
    public ResourceSearchInputModel? Search { get; set; }
    public double Threshold { get; set; } = 80;
    public List<ComparisonRuleInputModel> Rules { get; set; } = [];
}
