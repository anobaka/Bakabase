using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Components;

public interface IHealthScoreEngine
{
    HealthScoreEvaluation Evaluate(HealthScoreProfile profile, ResourceMatcherEvaluationContext ctx);
}

public record HealthScoreEvaluation
{
    public required decimal Score { get; init; }
    public required IReadOnlyList<HealthScoreMatchedRule> MatchedRules { get; init; }
}

public record HealthScoreMatchedRule
{
    public required int RuleId { get; init; }
    public string? RuleName { get; init; }
    public required decimal Delta { get; init; }
}
