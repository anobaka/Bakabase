namespace Bakabase.Modules.HealthScore.Models.Db;

/// <summary>
/// Persistence shape for a scoring rule. Goes through JSON round-trip via
/// <see cref="HealthScoreProfileDbModel.RulesJson"/>. Property values are
/// stored as StandardValue-serialized strings on
/// <see cref="ResourceMatcherLeafDbModel.PropertyValue"/>, so JSON
/// (de)serialization never touches them as untyped <see cref="object"/>
/// fields and JsonElement never appears in the round-trip.
/// </summary>
public record HealthScoreRuleDbModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ResourceMatcherDbModel Match { get; set; } = new();
    public decimal Delta { get; set; }
}
