using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonRule
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int Order { get; set; }

    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyValueScope? PropertyValueScope { get; set; }

    public ComparisonMode Mode { get; set; }
    public string? Parameter { get; set; }
    public bool Normalize { get; set; }

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; } = 1.0;

    public NullValueBehavior OneNullBehavior { get; set; } = NullValueBehavior.Skip;
    public NullValueBehavior BothNullBehavior { get; set; } = NullValueBehavior.Skip;
}
