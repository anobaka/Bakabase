namespace Bakabase.Modules.Comparison.Models.Domain;

/// <summary>
/// Stores the score and values for a single rule in a comparison pair
/// </summary>
public class RuleScoreDetail
{
    /// <summary>
    /// The rule ID
    /// </summary>
    public int RuleId { get; set; }

    /// <summary>
    /// The order of the rule
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// The score for this rule (0-1)
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// The weight of this rule
    /// </summary>
    public double Weight { get; set; }

    /// <summary>
    /// The value from resource 1 (serialized as string)
    /// </summary>
    public string? Value1 { get; set; }

    /// <summary>
    /// The value from resource 2 (serialized as string)
    /// </summary>
    public string? Value2 { get; set; }

    /// <summary>
    /// Whether this rule was skipped (due to null value handling)
    /// </summary>
    public bool IsSkipped { get; set; }

    /// <summary>
    /// Whether this rule triggered a veto
    /// </summary>
    public bool IsVetoed { get; set; }
}
