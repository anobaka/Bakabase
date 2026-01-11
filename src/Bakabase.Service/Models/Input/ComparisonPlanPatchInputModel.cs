using System.Collections.Generic;
using Bakabase.Modules.Comparison.Models.Input;

namespace Bakabase.Service.Models.Input;

public record ComparisonPlanPatchInputModel
{
    public string? Name { get; set; }
    public ResourceSearchInputModel? Search { get; set; }
    public double? Threshold { get; set; }
    public List<ComparisonRuleInputModel>? Rules { get; set; }
}
