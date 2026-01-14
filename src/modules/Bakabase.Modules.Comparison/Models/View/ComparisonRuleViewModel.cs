using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonRuleViewModel
{
    public int Id { get; set; }
    public int Order { get; set; }

    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public PropertyValueScope? PropertyValueScope { get; set; }

    public ComparisonMode Mode { get; set; }
    public object? Parameter { get; set; }
    public bool Normalize { get; set; }

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; }

    public NullValueBehavior OneNullBehavior { get; set; }
    public NullValueBehavior BothNullBehavior { get; set; }
}
